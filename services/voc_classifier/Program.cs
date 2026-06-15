using MassTransit;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;
using VocClassifier.Service.Consumers;
using VocClassifier.Service.Models;
using VocClassifier.Service.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/voc-classifier-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting VOC Classifier Service...");

    var builder = Host.CreateDefaultBuilder(args);

    builder.ConfigureAppConfiguration((context, config) =>
    {
        var env = context.HostingEnvironment;
        config.SetBasePath(AppContext.BaseDirectory)
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
              .AddEnvironmentVariables("TEXTILE_")
              .AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());
    });

    builder.UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", "VocClassifier.Service")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/voc-classifier-.txt",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}");
    });

    builder.ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqConfig>(config.GetSection("RabbitMq"));
        services.Configure<DatabaseConfig>(config.GetSection("Database"));
        services.Configure<RandomForestConfig>(config.GetSection("RandomForest"));

        var dbConfig = config.GetSection("Database").Get<DatabaseConfig>();
        services.AddDbContext<TextileMonitoringDbContext>(options =>
        {
            var connectionString = config.GetConnectionString("DefaultConnection");
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.CommandTimeout(dbConfig!.CommandTimeout);
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

        services.AddTransient<IRandomForestVocClassifier, RandomForestVocClassifier>();
        services.AddSingleton(Log.Logger);

        services.AddMassTransit(x =>
        {
            x.AddConsumer<VocSensorConsumer, VocSensorConsumerDefinition>();

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

                cfg.Send<VocClassificationResult>(s =>
                {
                    s.UseCorrelationId(ctx => ctx.CorrelationId);
                });

                cfg.Publish<VocClassificationResult>(p =>
                {
                    p.ExchangeType = "topic";
                });

                cfg.ReceiveEndpoint(QueueNames.VocSensor, e =>
                {
                    e.PrefetchCount = rabbitConfig.PrefetchCount;
                    e.ConfigureConsumeTopology = false;
                    e.UseMessageRetry(r => r.Immediate(3));
                    e.UseDeadLetterQueue($"{QueueNames.VocSensor}.dlq");

                    e.Bind(QueueNames.Exchanges.Sensor, s =>
                    {
                        s.RoutingKey = RoutingKeys.VocSensor;
                        s.ExchangeType = "topic";
                    });

                    e.ConfigureConsumer<VocSensorConsumer>(busContext);
                });

                cfg.ReceiveEndpoint("textile-monitoring.voc-classifier.dead-letter", e =>
                {
                    e.Handler<VocSensorDataReceived>(async ctx =>
                    {
                        var logger = ctx.GetService<Serilog.ILogger>() ?? Serilog.Log.Logger;
                        logger.Error("Dead letter received for VocSensorDataReceived: {@Data}",
                            new { ctx.Message.CorrelationId, ctx.Message.SensorCode, ctx.Message.TextileId });
                        await Task.CompletedTask;
                    });
                });
            });
        });

        services.AddMassTransitHostedService();
        services.AddHealthChecks();

        var metricsServer = new MetricServer(port: 9106);
        services.AddSingleton(metricsServer);
    });

    var host = builder.Build();

    var server = host.Services.GetRequiredService<MetricServer>();
    server.Start();

    Log.Information("VOC Classifier Service started successfully. Metrics on port 9106.");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "VOC Classifier Service terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
