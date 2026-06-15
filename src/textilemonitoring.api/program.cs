
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using MassTransit.ConsumeConfigurators;
using Prometheus;
using Serilog;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.Api.Hubs;
using TextileMonitoring.Api.Controllers;
using TextileMonitoring.API.Services;
using TextileMonitoring.API.SqlServer;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;
using TextileMonitoring.Data.Entities;
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

    builder.Services.AddSignalR()
        .AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
    builder.Services.AddScoped<ISqlServerBatchWriter, SqlServerBatchWriter>();
    builder.Services.AddScoped<IMonitoringHubService, MonitoringHubService>();

    builder.Services.AddHttpClient();

    var rabbitMqConfig = builder.Configuration.GetSection("RabbitMQ");
    builder.Services.AddMassTransit(x =>
    {
        x.SetKebabCaseEndpointNameFormatter();
        x.SetSnakeCaseEntityNameFormatter();

        x.AddRequestClient<SensorDataReceived>(new Uri($"exchange:{QueueNames.Exchanges.Sensor}"));
        x.AddRequestClient<NitrogenTreatmentRequest>(new Uri($"exchange:{QueueNames.Exchanges.Treatment}"));
        x.AddRequestClient<AdHocVulnerabilityRequest>(new Uri($"exchange:{QueueNames.Exchanges.Vulnerability}"));

        x.AddConsumer<PestClassificationResultConsumer>();
        x.AddConsumer<VocClassificationResultConsumer>();
        x.AddConsumer<NitrogenTreatmentResultConsumer>();
        x.AddConsumer<VulnerabilityIndexGeneratedConsumer>();

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

            cfg.Send<NitrogenTreatmentRequest>(s =>
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

            cfg.Publish<PestClassificationResult>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<VocClassificationResult>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<VocSensorDataReceived>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<FrassImageCaptured>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<NitrogenTreatmentRequest>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<NitrogenTreatmentResult>(p =>
            {
                p.ExchangeType = "topic";
            });

            cfg.Publish<VulnerabilityIndexGenerated>(p =>
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

            cfg.ReceiveEndpoint("textile.gateway.pest-classification", e =>
            {
                e.PrefetchCount = prefetchCount;
                e.UseMessageRetry(r => r.Immediate(retryCount));
                e.UseDeadLetterQueue("textile.gateway.pest-classification.dlq");
                e.ConfigureConsumer<PestClassificationResultConsumer>(busContext);
            });

            cfg.ReceiveEndpoint("textile.gateway.voc-classification", e =>
            {
                e.PrefetchCount = prefetchCount;
                e.UseMessageRetry(r => r.Immediate(retryCount));
                e.UseDeadLetterQueue("textile.gateway.voc-classification.dlq");
                e.ConfigureConsumer<VocClassificationResultConsumer>(busContext);
            });

            cfg.ReceiveEndpoint("textile.gateway.treatment-result", e =>
            {
                e.PrefetchCount = prefetchCount;
                e.UseMessageRetry(r => r.Immediate(retryCount));
                e.UseDeadLetterQueue("textile.gateway.treatment-result.dlq");
                e.ConfigureConsumer<NitrogenTreatmentResultConsumer>(busContext);
            });

            cfg.ReceiveEndpoint("textile.gateway.vulnerability", e =>
            {
                e.PrefetchCount = prefetchCount;
                e.UseMessageRetry(r => r.Immediate(retryCount));
                e.UseDeadLetterQueue("textile.gateway.vulnerability.dlq");
                e.ConfigureConsumer<VulnerabilityIndexGeneratedConsumer>(busContext);
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

    app.MapHub<MonitoringHub>("/hub/monitoring");

    app.MapMetrics("/metrics");

    app.MapHealthChecks("/healthz");

    app.MapFallbackToFile("index.html");

    app.Logger.LogInformation("古代织绣品虫蛀与霉变协同监测系统 - API Gateway 启动成功！");
    app.Logger.LogInformation("监听地址: http://localhost:5000");
    app.Logger.LogInformation("Swagger文档: http://localhost:5000/swagger");
    app.Logger.LogInformation("健康检查端点: http://localhost:5000/healthz");
    app.Logger.LogInformation("Prometheus指标: http://localhost:5000/metrics");
    app.Logger.LogInformation("SignalR Hub: http://localhost:5000/hub/monitoring");
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

public class PestClassificationResultConsumer : IConsumer<PestClassificationResult>
{
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly IMonitoringHubService _hubService;
    private readonly ILogger<PestClassificationResultConsumer> _logger;

    public PestClassificationResultConsumer(
        TextileMonitoringDbContext dbContext,
        IMonitoringHubService hubService,
        ILogger<PestClassificationResultConsumer> logger)
    {
        _dbContext = dbContext;
        _hubService = hubService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PestClassificationResult> context)
    {
        var msg = context.Message;
        _logger.LogInformation("收到蛀虫分类结果: Textile={TextileId}, Species={Species}, Confidence={Confidence}",
            msg.TextileId, msg.PredictedSpecies, msg.Confidence);

        var sensor = await _dbContext.Sensors
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SensorCode == msg.SensorCode);

        var record = new PestClassificationRecord
        {
            TextileId = msg.TextileId,
            SensorId = sensor?.Id ?? 0,
            SourceImageId = null,
            CorrelationId = msg.CorrelationId,
            SourceImageCorrelationId = msg.SourceImageCorrelationId,
            ClassifiedAt = msg.Timestamp,
            PredictedSpeciesId = (int)msg.PredictedSpecies,
            PredictedSpeciesName = msg.PredictedSpecies.ToString(),
            Confidence = (decimal)msg.Confidence,
            ProbabilitiesJson = JsonSerializer.Serialize(msg.SpeciesProbabilities.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value)),
            ModelVersion = msg.ModelVersion,
            InferenceLatencyMs = (decimal)msg.InferenceLatencyMs,
            PredictedInstars = msg.PredictedInstars,
            EstimatedPopulationSize = (decimal)msg.EstimatedPopulationSize,
            RiskSeverityScore = msg.RiskSeverityScore,
            RecommendedAction = msg.RecommendedAction,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PestClassificationRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        await _hubService.SendPestClassificationUpdate(msg);
    }
}

public class VocClassificationResultConsumer : IConsumer<VocClassificationResult>
{
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly IMonitoringHubService _hubService;
    private readonly ILogger<VocClassificationResultConsumer> _logger;

    public VocClassificationResultConsumer(
        TextileMonitoringDbContext dbContext,
        IMonitoringHubService hubService,
        ILogger<VocClassificationResultConsumer> logger)
    {
        _dbContext = dbContext;
        _hubService = hubService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VocClassificationResult> context)
    {
        var msg = context.Message;
        _logger.LogInformation("收到VOC分类结果: Textile={TextileId}, Mold={Mold}, Confidence={Confidence}",
            msg.TextileId, msg.PredictedMoldSpecies, msg.Confidence);

        var sensor = await _dbContext.Sensors
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SensorCode == msg.SensorCode);

        var record = new VocClassificationRecord
        {
            TextileId = msg.TextileId,
            SensorId = sensor?.Id ?? 0,
            SourceVocDataId = null,
            CorrelationId = msg.CorrelationId,
            SourceSensorCorrelationId = msg.SourceSensorCorrelationId,
            ClassifiedAt = msg.Timestamp,
            PredictedMoldSpeciesId = (int)msg.PredictedMoldSpecies,
            PredictedMoldSpeciesName = msg.PredictedMoldSpecies.ToString(),
            Confidence = (decimal)msg.Confidence,
            ProbabilitiesJson = JsonSerializer.Serialize(msg.SpeciesProbabilities.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value)),
            ModelVersion = msg.ModelVersion,
            EstimatedBiomassMg = (decimal)msg.EstimatedBiomassMg,
            EstimatedGrowthStageDays = (decimal)msg.EstimatedGrowthStageDays,
            MycotoxinRiskIndex = (decimal)msg.MycotoxinRiskIndex,
            EarlyWarningSeverity = msg.EarlyWarningSeverity,
            PredictedIncubationHours = (decimal)msg.PredictedIncubationHours,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.VocClassificationRecords.Add(record);
        await _dbContext.SaveChangesAsync();

        await _hubService.SendVocUpdate(msg);
    }
}

public class NitrogenTreatmentResultConsumer : IConsumer<NitrogenTreatmentResult>
{
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly IMonitoringHubService _hubService;
    private readonly ILogger<NitrogenTreatmentResultConsumer> _logger;

    public NitrogenTreatmentResultConsumer(
        TextileMonitoringDbContext dbContext,
        IMonitoringHubService hubService,
        ILogger<NitrogenTreatmentResultConsumer> logger)
    {
        _dbContext = dbContext;
        _hubService = hubService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<NitrogenTreatmentResult> context)
    {
        var msg = context.Message;
        _logger.LogInformation("收到充氮治疗结果: Textile={TextileId}, Success={Success}",
            msg.TextileId, msg.IsSuccessCriteriaMet);

        var session = new NitrogenTreatmentSession
        {
            TextileId = msg.TextileId,
            CorrelationId = msg.CorrelationId,
            RequestCorrelationId = msg.RequestCorrelationId,
            RequestedBy = "System",
            TargetOrganismsId = 0,
            TargetOxygenConcentrationPct = 0,
            NitrogenFlowRateLpm = 0,
            ExposureDurationMinutes = 0,
            ChamberPressureKpa = 0,
            ChamberTemperatureC = 0,
            ChamberHumidityPct = 0,
            CurrentPestDensity = 0,
            CurrentFungiCFU = 0,
            PrimaryPestTargetId = null,
            PredictedEggMortalityRate = (decimal)msg.PredictedEggMortalityRate,
            PredictedLarvaeMortalityRate = (decimal)msg.PredictedLarvaeMortalityRate,
            PredictedAdultMortalityRate = (decimal)msg.PredictedAdultMortalityRate,
            PredictedFungiSterilityRate = (decimal)msg.PredictedFungiSterilityRate,
            CILowPct = (decimal)msg.ConfidenceIntervalLowPct,
            CIHighPct = (decimal)msg.ConfidenceIntervalHighPct,
            ProbitTransformValue = msg.ProbitTransformValue,
            LD99Minutes = (decimal)msg.CalculatedLethalDoseLD99Min,
            MinimumRequiredExposureMin = (decimal)msg.MinimumRequiredExposureMin,
            RecommendedSafetyExposureMin = (decimal)msg.RecommendedSafetyExposureMin,
            FiberStrengthDegradationPct = (decimal)msg.FiberStrengthDegradationEstimatedPct,
            ColorChangeDeltaE = (decimal)msg.ColorChangeDeltaE,
            SessionStatus = msg.IsSuccessCriteriaMet ? 2 : 3,
            IsSuccessCriteriaMet = msg.IsSuccessCriteriaMet,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Notes = msg.TreatmentRiskNotes
        };

        _dbContext.NitrogenTreatmentSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        await _hubService.SendTreatmentUpdate(msg);
    }
}

public class VulnerabilityIndexGeneratedConsumer : IConsumer<VulnerabilityIndexGenerated>
{
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly IMonitoringHubService _hubService;
    private readonly ILogger<VulnerabilityIndexGeneratedConsumer> _logger;

    public VulnerabilityIndexGeneratedConsumer(
        TextileMonitoringDbContext dbContext,
        IMonitoringHubService hubService,
        ILogger<VulnerabilityIndexGeneratedConsumer> logger)
    {
        _dbContext = dbContext;
        _hubService = hubService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VulnerabilityIndexGenerated> context)
    {
        var msg = context.Message;
        _logger.LogInformation("收到脆弱性评估结果: Textile={TextileId}, Rank={Rank}/{Total}, Priority={Priority}",
            msg.TextileId, msg.TopsisRankAmongAll, msg.TopsisTotalCount, msg.Priority);

        var assessment = new VulnerabilityAssessment
        {
            TextileId = msg.TextileId,
            CorrelationId = msg.CorrelationId,
            AssessmentDate = msg.Timestamp,
            TopsisScore = msg.TopsisScore,
            TopsisRank = msg.TopsisRankAmongAll,
            TopsisTotalCount = msg.TopsisTotalCount,
            PriorityId = (int)msg.Priority,
            PriorityName = msg.Priority.ToString(),
            CriteriaJson = JsonSerializer.Serialize(msg.Criteria),
            CompositePestDamageScore = msg.CompositePestDamageScore,
            CompositeMoldAreaScore = msg.CompositeMoldAreaScore,
            FiberTensileStrengthRemainingPct = msg.FiberTensileStrengthRemainingPct,
            DynastyScarcityValueScore = msg.DynastyScarcityValueScore,
            HistoricalSignificanceScore = msg.HistoricalSignificanceScore,
            RestorationCostEstimateCny = msg.RestorationCostEstimateCny,
            RelativeClosenessCC = msg.RelativeClosenessCC,
            ConsistencyRatioCR = msg.ConsistencyRatioCR,
            TreatmentCostBenefitRatio = msg.TreatmentCostBenefitRatio,
            ProjectedYearsIfNoAction = msg.ProjectedPreservationYearsIfNoAction,
            ProjectedYearsWithAction = msg.ProjectedPreservationYearsWithAction,
            ActionRecommendation = msg.ActionRecommendation,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.VulnerabilityAssessments.Add(assessment);
        await _dbContext.SaveChangesAsync();

        await _hubService.SendVulnerabilityUpdate(msg);
    }
}
