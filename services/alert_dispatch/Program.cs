using MassTransit;
using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using TextileMonitoring.AlertDispatch.Consumers;
using TextileMonitoring.AlertDispatch.Models;
using TextileMonitoring.AlertDispatch.Services;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Data;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Alert Dispatch Service...");

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
            .Enrich.WithProperty("ServiceName", "TextileMonitoring.AlertDispatch")
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
        services.Configure<NotificationConfig>(config.GetSection("Notifications"));
        services.Configure<AlertDispatchOptions>(config.GetSection("AlertDispatch"));

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

        services.AddHttpClient<IDingTalkNotifier, DingTalkNotifier>();
        services.AddTransient<IEmailNotifier, EmailNotifier>();
        services.AddTransient<IAlertRepository, AlertRepository>();
        services.AddTransient<IAlertDispatchService, AlertDispatchService>();
        services.AddSingleton(Log.Logger);

        services.AddMassTransit(x =>
        {
            x.AddConsumer<AlertTriggeredConsumer, AlertTriggeredConsumerDefinition>();

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

                cfg.Send<AlertTriggered>(s =>
                {
                    s.UseCorrelationId(ctx => ctx.CorrelationId);
                });

                cfg.Publish<AlertDispatched>(p =>
                {
                    p.ExchangeType = "topic";
                });

                cfg.ReceiveEndpoint("textile-monitoring.alert-dispatch.alert-triggered", e =>
                {
                    e.PrefetchCount = rabbitConfig.PrefetchCount;
                    e.UseMessageRetry(r => r.Immediate(3));
                    e.UseDeadLetterQueue("textile-monitoring.alert-dispatch.alert-triggered.dlq");
                    e.ConfigureConsumer<AlertTriggeredConsumer>(busContext);
                });

                cfg.ReceiveEndpoint("textile-monitoring.alert-dispatch.dead-letter", e =>
                {
                    e.Handler<AlertTriggered>(async ctx =>
                    {
                        var logger = ctx.GetService<Serilog.ILogger>() ?? Serilog.Log.Logger;
                        logger.Error("Dead letter received for AlertTriggered: {@Data}",
                            new { ctx.Message.CorrelationId, ctx.Message.AlertLevel, ctx.Message.TextileName });
                        await Task.CompletedTask;
                    });
                });
            });
        });

        services.AddMassTransitHostedService();
        services.AddHealthChecks();

        var metricsServer = new MetricServer(port: 9104);
        services.AddSingleton(metricsServer);
    });

    var host = builder.Build();

    var server = host.Services.GetRequiredService<MetricServer>();
    server.Start();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Alert Dispatch Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
