using MassTransit;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;
using TextileMonitoring.Data.Entities;
using VocClassifier.Service.Models;
using VocClassifier.Service.Services;

namespace VocClassifier.Service.Consumers;

public class VocSensorConsumer : IConsumer<VocSensorDataReceived>
{
    private readonly IRandomForestVocClassifier _classifier;
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger _logger;

    public VocSensorConsumer(
        IRandomForestVocClassifier classifier,
        TextileMonitoringDbContext dbContext,
        IPublishEndpoint publishEndpoint,
        ILogger logger)
    {
        _classifier = classifier;
        _dbContext = dbContext;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<VocSensorDataReceived> context)
    {
        var sensorData = context.Message;

        _logger.Information("Received VocSensorDataReceived: {CorrelationId}, Sensor: {SensorCode}, Textile: {TextileId}",
            sensorData.CorrelationId, sensorData.SensorCode, sensorData.TextileId);

        try
        {
            var classification = _classifier.Classify(sensorData);

            _logger.Information("VOC Classification complete: {CorrelationId} -> Species: {Species}, Confidence: {Confidence:F4}, Severity: {Severity}",
                sensorData.CorrelationId, classification.PredictedSpecies, classification.Confidence, classification.EarlyWarningSeverity);

            var sensor = await GetOrCreateSensorAsync(sensorData.SensorCode, sensorData.TextileId, context.CancellationToken);
            var savedSensorData = await SaveVocSensorDataAsync(sensorData, sensor?.Id ?? 0, context.CancellationToken);
            var savedRecord = await SaveClassificationRecordAsync(sensorData, classification, sensor?.Id ?? 0, savedSensorData?.Id, context.CancellationToken);

            var result = new VocClassificationResult
            {
                CorrelationId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                SourceSensorCorrelationId = sensorData.CorrelationId,
                TextileId = sensorData.TextileId,
                SensorCode = sensorData.SensorCode,
                PredictedMoldSpecies = classification.PredictedSpecies,
                Confidence = classification.Confidence,
                SpeciesProbabilities = classification.SpeciesProbabilities,
                ModelVersion = "rf-voc-v1.0",
                EstimatedBiomassMg = classification.EstimatedBiomassMg,
                EstimatedGrowthStageDays = classification.EstimatedGrowthDays,
                MycotoxinRiskIndex = classification.MycotoxinRiskIndex,
                SynergisticPestFungiIndex = classification.SynergisticPestFungiIndex,
                DecisionTreeVotes = classification.DecisionTreeVotes,
                OobErrorRateBps = classification.OobErrorRateBps,
                FeatureImportanceGiniTop3 = classification.FeatureImportanceGiniTop3,
                EarlyWarningSeverity = classification.EarlyWarningSeverity,
                PredictedIncubationHours = classification.PredictedIncubationHours
            };

            await _publishEndpoint.Publish(result, context.CancellationToken);

            _logger.Information("Published VocClassificationResult: {ResultCorrelationId} for sensor {SourceCorrelationId}",
                result.CorrelationId, sensorData.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to process VocSensorDataReceived: {CorrelationId}", sensorData.CorrelationId);
            throw;
        }
    }

    private async Task<Sensor?> GetOrCreateSensorAsync(string sensorCode, int textileId, CancellationToken ct)
    {
        var sensor = await _dbContext.Sensors
            .FirstOrDefaultAsync(s => s.SensorCode == sensorCode, ct);

        if (sensor == null)
        {
            sensor = new Sensor
            {
                SensorCode = sensorCode,
                TextileId = textileId,
                SensorType = SensorType.VocSensor,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow
            };
            _dbContext.Sensors.Add(sensor);
            await _dbContext.SaveChangesAsync(ct);
        }
        else
        {
            sensor.LastSeenAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(ct);
        }

        return sensor;
    }

    private async Task<VocSensorData> SaveVocSensorDataAsync(VocSensorDataReceived data, int sensorId, CancellationToken ct)
    {
        var entity = new VocSensorData
        {
            SensorId = sensorId,
            TextileId = data.TextileId,
            CorrelationId = data.CorrelationId,
            ReadingTime = data.Timestamp,
            ToluenePPB = (decimal)data.ToluenePPB,
            XylenePPB = (decimal)data.XylenePPB,
            EthylbenzenePPB = (decimal)data.EthylbenzenePPB,
            FormaldehydePPB = (decimal)data.FormaldehydePPB,
            AcetaldehydePPB = (decimal)data.AcetaldehydePPB,
            _1Octen3OlPPB = (decimal)data._1Octen3OlPPB,
            GeosminPPT = (decimal)data.GeosminPPT,
            _2MethylisoborneolPPT = (decimal)data._2MethylisoborneolPPT,
            TotalVolatilePPB = (decimal)data.TotalVolatilePPB,
            AirflowMetered = (decimal)data.AirflowMetered,
            Temperature = data.Temperature.HasValue ? (decimal?)data.Temperature.Value : null,
            Humidity = data.Humidity.HasValue ? (decimal?)data.Humidity.Value : null,
            ZigBeeSignalStrength = data.SignalStrength,
            SensorStatus = data.SensorStatus,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.VocSensorData.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
        return entity;
    }

    private async Task<VocClassificationRecord> SaveClassificationRecordAsync(
        VocSensorDataReceived sensorData,
        VocClassificationOutput classification,
        int sensorId,
        long? sourceVocDataId,
        CancellationToken ct)
    {
        var probabilitiesJson = JsonConvert.SerializeObject(
            classification.SpeciesProbabilities.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value
            ));

        var entity = new VocClassificationRecord
        {
            TextileId = sensorData.TextileId,
            SensorId = sensorId,
            SourceVocDataId = sourceVocDataId,
            CorrelationId = Guid.NewGuid(),
            SourceSensorCorrelationId = sensorData.CorrelationId,
            ClassifiedAt = DateTime.UtcNow,
            PredictedMoldSpeciesId = (int)classification.PredictedSpecies,
            PredictedMoldSpeciesName = classification.PredictedSpecies.ToString(),
            Confidence = (decimal)classification.Confidence,
            ProbabilitiesJson = probabilitiesJson,
            ModelVersion = "rf-voc-v1.0",
            EstimatedBiomassMg = (decimal)classification.EstimatedBiomassMg,
            EstimatedGrowthStageDays = (decimal)classification.EstimatedGrowthDays,
            MycotoxinRiskIndex = (decimal)classification.MycotoxinRiskIndex,
            EarlyWarningSeverity = classification.EarlyWarningSeverity,
            PredictedIncubationHours = (decimal)classification.PredictedIncubationHours,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.VocClassificationRecords.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
        return entity;
    }
}

public class VocSensorConsumerDefinition : ConsumerDefinition<VocSensorConsumer>
{
    private const string ServiceName = "voc-classifier";
    private const string EventName = "voc-sensor";

    public VocSensorConsumerDefinition()
    {
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<VocSensorConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Immediate(3));
        endpointConfigurator.UseDeadLetterQueue($"textile-monitoring.{ServiceName}.{EventName}.dlq");

        endpointConfigurator.ConfigureConsumer<VocSensorConsumer>(context);
    }
}
