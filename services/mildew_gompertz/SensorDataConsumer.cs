using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.Data;
using TextileMonitoring.MildewGompertz.Models;

namespace TextileMonitoring.MildewGompertz;

public class SensorDataConsumer : IConsumer<SensorDataReceived>
{
    private readonly TextileMonitoringDbContext _dbContext;
    private readonly GompertzPredictionService _predictionService;
    private readonly MildewModelConfig _mildewConfig;
    private readonly ILogger _logger;

    private static readonly ConcurrentDictionary<int, SensorDataAggregator> _aggregators = new();

    public SensorDataConsumer(
        TextileMonitoringDbContext dbContext,
        GompertzPredictionService predictionService,
        IOptions<MildewModelConfig> mildewConfig,
        ILogger logger)
    {
        _dbContext = dbContext;
        _predictionService = predictionService;
        _mildewConfig = mildewConfig.Value;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SensorDataReceived> context)
    {
        var message = context.Message;

        if (!IsFungiSensorType(message.SensorType))
        {
            _logger.Verbose("Skipping non-fungi sensor data: {SensorType}, TextileId: {TextileId}",
                message.SensorType, message.TextileId);
            return;
        }

        _logger.Information("Processing fungi sensor data for TextileId: {TextileId}, CorrelationId: {CorrelationId}",
            message.TextileId, message.CorrelationId);

        try
        {
            var textile = await _dbContext.Textiles
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == message.TextileId);

            if (textile == null)
            {
                _logger.Warning("Textile not found: {TextileId}", message.TextileId);
                return;
            }

            var aggregator = _aggregators.GetOrAdd(message.TextileId, _ => new SensorDataAggregator());

            var currentCFU = message.FungiCFU ?? _mildewConfig.DefaultInitialCFU;
            aggregator.AddData(message, currentCFU);

            if (aggregator.HasEnoughData())
            {
                var aggregated = aggregator.GetAggregatedData();

                var prediction = _predictionService.CalculatePrediction(
                    textileId: message.TextileId,
                    textileName: textile.Name,
                    initialCFU: aggregated.AvgCFU,
                    avgTemperature: aggregated.AvgTemperature,
                    avgHumidity: aggregated.AvgHumidity,
                    dominantFungiType: aggregated.DominantFungiType,
                    horizonDays: _mildewConfig.DefaultHorizonDays);

                prediction.CorrelationId = message.CorrelationId;

                await context.Publish(prediction);

                _logger.Information("Published MildewPredictionGenerated for TextileId: {TextileId}, Horizon: {HorizonDays} days, MaxCFU: {MaxCFU}, Risk: {RiskLevel}",
                    message.TextileId, prediction.HorizonDays, prediction.MaxCFU, prediction.RiskLevel);

                var alert = _predictionService.CreateAlertIfNeeded(
                    prediction,
                    textile.Name,
                    aggregated.AvgCFU);

                if (alert != null)
                {
                    await context.Publish(alert);

                    _logger.Warning("Published AlertTriggered for TextileId: {TextileId}, Level: {AlertLevel}, CurrentCFU: {CurrentCFU}",
                        message.TextileId, alert.AlertLevel, aggregated.AvgCFU);
                }

                aggregator.Reset();
            }
            else
            {
                _logger.Debug("Aggregated {Count} samples for TextileId: {TextileId}, need {Required} samples",
                    aggregator.SampleCount, message.TextileId, SensorDataAggregator.RequiredSamples);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing fungi sensor data for TextileId: {TextileId}, CorrelationId: {CorrelationId}",
                message.TextileId, message.CorrelationId);
            throw;
        }
    }

    private static bool IsFungiSensorType(string sensorType)
    {
        if (string.IsNullOrWhiteSpace(sensorType)) return false;

        var lowerType = sensorType.ToLowerInvariant();
        return lowerType.Contains("fungi") ||
               lowerType.Contains("mold") ||
               lowerType.Contains("mould") ||
               lowerType == "2" ||
               lowerType == "fungisensor";
    }

    private class SensorDataAggregator
    {
        public const int RequiredSamples = 3;

        private readonly List<SensorDataReceived> _samples = new();
        private readonly List<double> _cfuValues = new();

        public int SampleCount => _samples.Count;

        public void AddData(SensorDataReceived message, double currentCFU)
        {
            _samples.Add(message);
            _cfuValues.Add(currentCFU);

            if (_samples.Count > RequiredSamples)
            {
                _samples.RemoveAt(0);
                _cfuValues.RemoveAt(0);
            }
        }

        public bool HasEnoughData()
        {
            return _samples.Count >= RequiredSamples;
        }

        public AggregatedSensorData GetAggregatedData()
        {
            var cfuValues = _cfuValues.Where(v => v > 0).ToList();
            var tempValues = _samples.Where(s => s.Temperature > 0).Select(s => s.Temperature).ToList();
            var humValues = _samples.Where(s => s.Humidity > 0).Select(s => s.Humidity).ToList();

            var fungiTypes = _samples
                .Where(s => !string.IsNullOrWhiteSpace(s.DominantFungiType))
                .GroupBy(s => s.DominantFungiType)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return new AggregatedSensorData
            {
                AvgCFU = cfuValues.Any() ? cfuValues.Average() : 100.0,
                AvgTemperature = tempValues.Any() ? tempValues.Average() : 22.0,
                AvgHumidity = humValues.Any() ? humValues.Average() : 55.0,
                DominantFungiType = fungiTypes?.Key
            };
        }

        public void Reset()
        {
            _samples.Clear();
            _cfuValues.Clear();
        }
    }

    private class AggregatedSensorData
    {
        public double AvgCFU { get; set; }
        public double AvgTemperature { get; set; }
        public double AvgHumidity { get; set; }
        public string? DominantFungiType { get; set; }
    }
}
