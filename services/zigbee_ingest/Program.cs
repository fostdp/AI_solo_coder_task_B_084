
using MassTransit;
using Prometheus;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using ZigBeeIngest.Worker;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting ZigBee UDP Ingest Worker Service...");

    var builder = Host.CreateDefaultBuilder(args);

    builder.ConfigureAppConfiguration((context, config) =>
    {
        var env = context.HostingEnvironment;
        config.SetBasePath(AppContext.BaseDirectory)
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
              .AddEnvironmentVariables("ZIGBEE_")
              .AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());
    });

    builder.UseSerilog((context, services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", "ZigBeeIngest.Worker")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/log-.txt",
                rollingInterval: Serilog.RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ServiceName}] {Message:lj}{NewLine}{Exception}");
    });

    builder.ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqSettings>(config.GetSection("RabbitMq"));
        services.Configure<UdpSettings>(config.GetSection("Udp"));
        services.Configure<BatchingSettings>(config.GetSection("Batching"));

        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((ctx, cfg) =>
            {
                var rabbitSettings = ctx.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqSettings>>().Value;

                cfg.Host(rabbitSettings.Host, rabbitSettings.Port, rabbitSettings.VirtualHost, h =>
                {
                    h.Username(rabbitSettings.Username);
                    h.Password(rabbitSettings.Password);
                    h.Heartbeat(TimeSpan.FromSeconds(30));
                });

                cfg.UseMessageRetry(r => r.Exponential(
                    retryLimit: rabbitSettings.RetryCount,
                    minDelay: TimeSpan.FromMilliseconds(100),
                    maxDelay: TimeSpan.FromMilliseconds(rabbitSettings.RetryIntervalMs * 5),
                    delta: TimeSpan.FromMilliseconds(rabbitSettings.RetryIntervalMs)));

                cfg.UseCircuitBreaker(cb =>
                {
                    cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                    cb.TripThreshold = 0.5;
                    cb.ActiveThreshold = 5;
                    cb.ResetInterval = TimeSpan.FromMinutes(1);
                });

                cfg.ConfigureJsonSerializerOptions(opt =>
                {
                    opt.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                });

                cfg.Publish<SensorDataReceived>(p =>
                {
                    p.ExchangeName = QueueNames.Exchanges.Sensor;
                    p.ExchangeType = "topic";
                });

                cfg.Send<SensorDataReceived>(s =>
                {
                    s.UseCorrelationId(ctx => ctx.CorrelationId);
                });

                cfg.ReceiveEndpoint(QueueNames.SensorData, e =>
                {
                    e.PrefetchCount = rabbitSettings.PrefetchCount;
                    e.ExchangeType = "topic";
                    e.Bind(QueueNames.Exchanges.Sensor, x =>
                    {
                        x.RoutingKey = "#";
                        x.ExchangeType = "topic";
                    });
                });
            });
        });

        services.AddMassTransitHostedService();
        services.AddHostedService<ZigBeeUdpListenerWorker>();
        services.AddHealthChecks();

        var metricsServer = new MetricServer(port: 9101);
        services.AddSingleton(metricsServer);
    });

    var host = builder.Build();

    var server = host.Services.GetRequiredService<MetricServer>();
    server.Start();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ZigBee UDP Ingest Worker Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
