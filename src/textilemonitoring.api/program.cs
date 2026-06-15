
using System.Text.Json.Serialization;
using MassTransit;
using Prometheus;
using Serilog;
using TextileMonitoring.API.Services;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;
using TextileMonitoring.Data.Repositories;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("古代织绣品虫蛀与霉变协同监测系统 - API Gateway 正在启动...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ServiceName", "API.Gateway")
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/log-.txt",
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            retainedFileCountLimit: 31,
            rollOnFileSizeLimit: true));

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "古代织绣品虫蛀与霉变协同监测系统 API Gateway",
            Version = "v1",
            Description = "织绣品保护实验室 - 微服务架构API网关"
        });
    });

    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
    });

    builder.Services.AddHealthChecks();

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddTextileDbContext(connectionString);
    builder.Services.AddDataRepositories();

    builder.Services.AddScoped<IPredictionGatewayService, PredictionGatewayService>();
    builder.Services.AddScoped<IAlertService, AlertService>();
    builder.Services.AddScoped<ISensorDataService, SensorDataService>();

    builder.Services.AddHttpClient();

    var rabbitMqConfig = builder.Configuration.GetSection("RabbitMQ");
    builder.Services.AddMassTransit(x =>
    {
        x.SetKebabCaseEndpointNameFormatter();
        x.SetSnakeCaseEntityNameFormatter();

        x.AddRequestClient<SensorDataReceived>(new Uri($"exchange:{QueueNames.Exchanges.Sensor}"));

        x.UsingRabbitMq((busContext, cfg) =>
        {
            var host = rabbitMqConfig["Host"] ?? "localhost";
            var port = ushort.Parse(rabbitMqConfig["Port"] ?? "5672");
            var vhost = rabbitMqConfig["VirtualHost"] ?? "/";
            var username = rabbitMqConfig["Username"] ?? "guest";
            var password = rabbitMqConfig["Password"] ?? "guest";
            var prefetchCount = ushort.Parse(rabbitMqConfig["PrefetchCount"] ?? "16");
            var retryCount = int.Parse(rabbitMqConfig["RetryCount"] ?? "3");

            cfg.Host(host, port, vhost, h =>
            {
                h.Username(username);
                h.Password(password);
                h.Heartbeat(TimeSpan.FromSeconds(30));
            });

            cfg.UseMessageRetry(r => r.Exponential(
                retryLimit: retryCount,
                minDelay: TimeSpan.FromMilliseconds(100),
                maxDelay: TimeSpan.FromSeconds(10),
                delta: TimeSpan.FromMilliseconds(500)));

            cfg.UseCircuitBreaker(cb =>
            {
                cb.TrackingPeriod = TimeSpan.FromMinutes(1);
                cb.TripThreshold = 0.5;
                cb.ActiveThreshold = 10;
                cb.ResetInterval = TimeSpan.FromMinutes(1);
            });

            cfg.ConfigureJsonSerializerOptions(opt =>
            {
                opt.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

            cfg.Send<SensorDataReceived>(s =>
            {
                s.UseCorrelationId(ctx => ctx.CorrelationId);
            });

            cfg.Publish<SensorDataReceived>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<PopulationPredictionGenerated>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<MildewPredictionGenerated>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<AlertTriggered>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.ReceiveEndpoint("textile.gateway.population", e =>
            {
                e.PrefetchCount = prefetchCount;
                e.UseMessageRetry(r => r.Immediate(retryCount));
                e.UseDeadLetterQueue("textile.gateway.population.dlq");
            });

            cfg.ReceiveEndpoint("textile.gateway.mildew", e =>
            {
                e.PrefetchCount = prefetchCount;
                e.UseMessageRetry(r => r.Immediate(retryCount));
                e.UseDeadLetterQueue("textile.gateway.mildew.dlq");
            });
        });
    });

    builder.Services.AddMassTransitHostedService();

    builder.Services.AddCors(options =>
    {
        var corsOrigins = builder.Configuration["Gateway:CorsOrigins"] ?? "*";
        options.AddPolicy("AllowAll", policy =>
        {
            if (corsOrigins == "*")
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            }
            else
            {
                policy.WithOrigins(corsOrigins.Split(','))
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            }
        });
    });

    var app = builder.Build();

    app.UseResponseCompression();

    app.UseHttpMetrics();

    if (app.Environment.IsDevelopment() || bool.Parse(builder.Configuration["Gateway:EnableSwagger"] ?? "true"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "织绣品监测 API Gateway V1");
        });
    }

    app.UseCors("AllowAll");

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthorization();

    app.MapControllers();

    app.MapMetrics("/metrics");

    app.MapHealthChecks("/healthz");

    app.MapFallbackToFile("index.html");

    app.Logger.LogInformation("古代织绣品虫蛀与霉变协同监测系统 - API Gateway 启动成功！");
    app.Logger.LogInformation("监听地址: http://localhost:5000");
    app.Logger.LogInformation("Swagger文档: http://localhost:5000/swagger");
    app.Logger.LogInformation("健康检查端点: http://localhost:5000/healthz");
    app.Logger.LogInformation("Prometheus指标: http://localhost:5000/metrics");
    app.Logger.LogInformation("架构模式: 微服务 + API网关 + RabbitMQ事件驱动");
    app.Logger.LogInformation("微服务: zigbee_ingest | population_sim | mildew_gompertz | alert_dispatch");

    app.Run("http://0.0.0.0:5000");
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Gateway 启动时发生致命错误");
}
finally
{
    Log.CloseAndFlush();
}
