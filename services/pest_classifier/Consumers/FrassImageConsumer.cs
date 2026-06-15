using MassTransit;
using Microsoft.EntityFrameworkCore;
using PestClassifier.Service.Services;
using Prometheus;
using System.Text.Json;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Contracts.RabbitMQ;
using TextileMonitoring.Data;
using TextileMonitoring.Data.Entities;

namespace PestClassifier.Service.Consumers;

public class FrassImageConsumer : IConsumer<FrassImageCaptured>
{
    private readonly CnnFrassClassifierService _classifier;
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly ILogger<FrassImageConsumer> _logger;

    private static readonly Counter ClassificationRequests = Metrics
        .CreateCounter("pest_classification_requests_total", "Total number of pest classification requests",
            new CounterConfiguration { LabelNames = new[] { "sensor_code", "textile_id" } });

    private static readonly Histogram ClassificationLatency = Metrics
        .CreateHistogram("pest_classification_latency_seconds", "Classification latency in seconds",
            new HistogramConfiguration
            {
                Buckets = Histogram.LinearBuckets(0.04, 0.01, 10),
                LabelNames = new[] { "predicted_species" }
            });

    private static readonly Gauge ActiveClassifications = Metrics
        .CreateGauge("pest_classification_active", "Number of active classifications in progress");

    private static readonly Counter ClassificationErrors = Metrics
        .CreateCounter("pest_classification_errors_total", "Total number of classification errors");

    public FrassImageConsumer(
        CnnFrassClassifierService classifier,
        TextileMonitoringDbContext dbContext,
        ILogger<FrassImageConsumer> logger)
    {
        _classifier = classifier;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<FrassImageCaptured> context)
    {
        var message = context.Message;

        _logger.Debug(
            "Received FrassImageCaptured: CorrelationId={CorrelationId}, TextileId={TextileId}, Sensor={SensorCode}, Particles={ParticleCount}",
            message.CorrelationId, message.TextileId, message.SensorCode, message.ParticleCount);

        using (ActiveClassifications.TrackInProgress())
        {
            ClassificationRequests
                .WithLabels(message.SensorCode, message.TextileId.ToString())
                .Inc();

            try
            {
                var (classificationResult, inferenceLatency) = _classifier.Classify(message);

                ClassificationLatency
                    .WithLabels(classificationResult.PredictedSpecies.ToString())
                    .Observe(inferenceLatency / 1000.0);

                await SaveToDatabaseAsync(message, classificationResult);

                await context.Publish(classificationResult, c =>
                {
                    c.SetRoutingKey(QueueNames.RoutingKeys.PestClassified);
                });

                _logger.Information(
                    "Classified frass image: SourceId={SourceId}, TextileId={TextileId}, Species={Species}, Confidence={Confidence:F2}%, Latency={Latency}ms, Population={Population}, Risk={Risk}",
                    message.CorrelationId,
                    classificationResult.TextileId,
                    classificationResult.PredictedSpecies,
                    classificationResult.Confidence * 100,
                    inferenceLatency,
                    classificationResult.EstimatedPopulationSize,
                    classificationResult.RiskSeverityScore);
            }
            catch (Exception ex)
            {
                ClassificationErrors.Inc();
                _logger.Error(
                    ex,
                    "Error processing frass image classification: CorrelationId={CorrelationId}, TextileId={TextileId}",
                    message.CorrelationId,
                    message.TextileId);
                throw;
            }
        }
    }

    private async Task SaveToDatabaseAsync(FrassImageCaptured message, PestClassificationResult result)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var sensor = await _dbContext.Sensors
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SensorCode == message.SensorCode);

            var sensorId = sensor?.Id ?? 0;

            var frassCapture = new FrassImageCapture
            {
                TextileId = message.TextileId,
                SensorId = sensorId,
                CorrelationId = message.CorrelationId,
                CaptureTime = message.Timestamp,
                ImageWidth = message.ImageWidth,
                ImageHeight = message.ImageHeight,
                PixelDepth = message.PixelDepth,
                Magnification = (decimal)message.Magnification,
                AverageParticleArea = (decimal)message.AverageParticleArea,
                ParticleCount = (int)message.ParticleCount,
                MeanGrayscale = (decimal)message.MeanGrayscale,
                TextureEntropy = (decimal)message.TextureEntropy,
                EllipticityMean = (decimal)message.EllipticityMean,
                AspectRatioMean = (decimal)message.AspectRatioMean,
                SolidityMean = (decimal)message.SolidityMean,
                FrassDensityCorrelated = (decimal)message.FrassDensityCorrelated,
                Temperature = message.Temperature.HasValue ? (decimal)message.Temperature.Value : null,
                Humidity = message.Humidity.HasValue ? (decimal)message.Humidity.Value : null,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.FrassImageCaptures.Add(frassCapture);
            await _dbContext.SaveChangesAsync();

            var probabilitiesJson = JsonSerializer.Serialize(
                result.SpeciesProbabilities.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value));

            var classificationRecord = new PestClassificationRecord
            {
                TextileId = message.TextileId,
                SensorId = sensorId,
                SourceImageId = frassCapture.Id,
                CorrelationId = result.CorrelationId,
                SourceImageCorrelationId = message.CorrelationId,
                ClassifiedAt = result.Timestamp,
                PredictedSpeciesId = (int)result.PredictedSpecies,
                PredictedSpeciesName = result.PredictedSpecies.ToString(),
                Confidence = (decimal)result.Confidence,
                ProbabilitiesJson = probabilitiesJson,
                ModelVersion = result.ModelVersion,
                InferenceLatencyMs = (decimal)result.InferenceLatencyMs,
                PredictedInstars = result.PredictedInstars,
                EstimatedPopulationSize = (decimal)result.EstimatedPopulationSize,
                RiskSeverityScore = result.RiskSeverityScore,
                RecommendedAction = result.RecommendedAction,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.PestClassificationRecords.Add(classificationRecord);
            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.Debug(
                "Saved classification records: ImageCaptureId={ImageId}, ClassificationRecordId={RecordId}",
                frassCapture.Id,
                classificationRecord.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
