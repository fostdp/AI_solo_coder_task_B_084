using MassTransit;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;
using TreatmentSimulator.Service.Consumers;
using TreatmentSimulator.Service.Models;
using TreatmentSimulator.Service.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Treatment Simulator Service...");

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
            .Enrich.WithProperty("ServiceName", "TreatmentSimulator.Service")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}");
    });

    builder.ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqConfig>(config.GetSection("RabbitMQ"));
        services.Configure<DatabaseConfig>(config.GetSection("Database"));
        services.Configure<ProbitModelConfig>(config.GetSection("ProbitModel"));

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
        });

        services.AddTransient<INitrogenProbitSimulator, NitrogenProbitSimulator>();
        services.AddSingleton(Log.Logger);

        services.AddMassTransit(x =>
        {
            x.AddConsumer<TreatmentRequestConsumer, TreatmentRequestConsumerDefinition>();

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

                cfg.Send<NitrogenTreatmentRequest>(s =>
                {
                    s.UseCorrelationId(ctx => ctx.CorrelationId);
                });

                cfg.Publish<NitrogenTreatmentResult>(p =>
                {
                    p.ExchangeType = "topic";
                });

                cfg.ReceiveEndpoint(QueueNames.TreatmentRequest, e =>
                {
                    e.PrefetchCount = rabbitConfig.PrefetchCount;
                    e.ConfigureConsumeTopology = false;

                    e.UseMessageRetry(r => r.Immediate(3));
                    e.UseDeadLetterQueue($"{QueueNames.TreatmentRequest}.dlq");

                    e.Bind(QueueNames.Exchanges.Treatment, b =>
                    {
                        b.RoutingKey = QueueNames.RoutingKeys.TreatmentSubmit;
                        b.ExchangeType = "topic";
                    });

                    e.ConfigureConsumer<TreatmentRequestConsumer>(busContext);
                });

                cfg.ReceiveEndpoint($"{QueueNames.TreatmentRequest}.dead-letter", e =>
                {
                    e.Handler<NitrogenTreatmentRequest>(async ctx =>
                    {
                        var logger = ctx.GetService<Serilog.ILogger>() ?? Serilog.Log.Logger;
                        logger.Error("Dead letter received for NitrogenTreatmentRequest: {@Data}",
                            new { ctx.Message.CorrelationId, ctx.Message.TextileId, ctx.Message.TargetOrganisms });
                        await Task.CompletedTask;
                    });
                });
            });
        });

        services.AddMassTransitHostedService();
        services.AddHealthChecks();

        var metricsServer = new MetricServer(port: 9107);
        services.AddSingleton(metricsServer);
    });

    var host = builder.Build();

    var server = host.Services.GetRequiredService<MetricServer>();
    server.Start();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Treatment Simulator Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
