using MassTransit;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using PestClassifier.Service.Consumers;
using PestClassifier.Service.Models;
using PestClassifier.Service.Services;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/pest-classifier-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Pest Classifier Service...");

    var builder = Host.CreateDefaultBuilder(args);

    builder.UseSerilog((context, config) =>
    {
        config
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/pest-classifier-.txt", rollingInterval: RollingInterval.Day);
    });

    builder.ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqConfig>(config.GetSection("RabbitMq"));
        services.Configure<DatabaseConfig>(config.GetSection("Database"));
        services.Configure<CnnClassifierConfig>(config.GetSection("CnnClassifier"));

        var dbConfig = config.GetSection("Database").Get<DatabaseConfig>();
        services.AddDbContext<TextileMonitoringDbContext>(options =>
        {
            options.UseSqlServer(dbConfig!.ConnectionString, sqlOptions =>
            {
                sqlOptions.CommandTimeout(dbConfig.CommandTimeout);
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: dbConfig.MaxRetryCount,
                    maxRetryDelay: TimeSpan.FromSeconds(dbConfig.MaxRetryDelaySec),
                    errorNumbersToAdd: null);
            });
            if (dbConfig.EnableDetailedErrors)
                options.EnableDetailedErrors();
            if (dbConfig.EnableSensitiveDataLogging)
                options.EnableSensitiveDataLogging();
        });

        services.AddSingleton<CnnFrassClassifierService>();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<FrassImageConsumer>();

            x.SetKebabCaseEndpointNameFormatter();
            x.SetSnakeCaseEntityNameFormatter();

            x.UsingRabbitMq((busContext, cfg) =>
            {
                var rabbitConfig = busContext.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqConfig>>().Value;

                cfg.Host(rabbitConfig.Host, rabbitConfig.Port, rabbitConfig.VirtualHost, h =>
                {
                    h.Username(rabbitConfig.Username);
                    h.Password(rabbitConfig.Password);
                    h.Heartbeat(TimeSpan.FromSeconds(30));
                });

                cfg.UseMessageRetry(r => r.Exponential(
                    retryLimit: rabbitConfig.RetryCount,
                    minDelay: TimeSpan.FromMilliseconds(100),
                    maxDelay: TimeSpan.FromMilliseconds(rabbitConfig.RetryIntervalMs * 5),
                    delta: TimeSpan.FromMilliseconds(rabbitConfig.RetryIntervalMs)));

                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = 0.5;
                    cb.ActiveThreshold = 5;
                    cb.ResetInterval = TimeSpan.FromMinutes(1);
                });

                cfg.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)));

                cfg.ConfigureJsonSerializerOptions(opt =>
                {
                    opt.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                });

                cfg.Send<PestClassificationResult>(s =>
                {
                    s.UseCorrelationId(ctx => ctx.CorrelationId);
                });

                cfg.Publish<PestClassificationResult>(p =>
                {
                    p.ExchangeType = "topic";
                });

                cfg.ReceiveEndpoint(QueueNames.SensorData, e =>
                {
                    e.PrefetchCount = rabbitConfig.PrefetchCount;
                    e.Bind(QueueNames.Exchanges.Sensor, s =>
                    {
                        s.RoutingKey = QueueNames.RoutingKeys.FrassImage;
                        s.ExchangeType = "topic";
                    });
                    e.UseMessageRetry(r => r.Immediate(3));
                    e.UseDeadLetterQueue($"{QueueNames.SensorData}.dlq");
                    e.ConfigureConsumer<FrassImageConsumer>(busContext);
                });

                cfg.ReceiveEndpoint(QueueNames.PestClassification, e =>
                {
                    e.PrefetchCount = rabbitConfig.PrefetchCount;
                    e.Bind(QueueNames.Exchanges.Classification, s =>
                    {
                        s.RoutingKey = QueueNames.RoutingKeys.PestClassified;
                        s.ExchangeType = "topic";
                    });
                    e.UseMessageRetry(r => r.Immediate(3));
                    e.UseDeadLetterQueue($"{QueueNames.PestClassification}.dlq");
                });
            });
        });

        services.AddMassTransitHostedService();
        services.AddHealthChecks();

        var metricsServer = new MetricServer(port: 9105);
        services.AddSingleton(metricsServer);
    });

    var host = builder.Build();

    var server = host.Services.GetRequiredService<MetricServer>();
    server.Start();

    Log.Information("Pest Classifier Service started successfully, metrics on port 9105");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Pest Classifier Service terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
