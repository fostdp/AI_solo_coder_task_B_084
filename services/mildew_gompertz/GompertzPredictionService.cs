using Microsoft.Extensions.Options;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using TextileMonitoring.MildewGompertz.Models;

namespace TextileMonitoring.MildewGompertz;

public class GompertzPredictionService
{
    private readonly MildewModelConfig _config;
    private readonly ILogger _logger;

    public GompertzPredictionService(
        IOptions<MildewModelConfig> config,
        ILogger logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public MildewPredictionGenerated CalculatePrediction(
        int textileId,
        string textileName,
        double initialCFU,
        double avgTemperature,
        double avgHumidity,
        string? dominantFungiType,
        int horizonDays)
    {
        _logger.Debug("Calculating Gompertz prediction for TextileId: {TextileId}, InitialCFU: {InitialCFU}, Horizon: {HorizonDays} days",
            textileId, initialCFU, horizonDays);

        var adjustedParams = AdjustParametersByEnvironment(avgTemperature, avgHumidity);

        double A = adjustedParams.K;
        double C = adjustedParams.Rho;
        double y0 = Math.Max(initialCFU, _config.DefaultInitialCFU);
        double B = Math.Log(A / y0);

        var predictionPoints = GeneratePredictionPoints(A, B, C, y0, horizonDays);

        double finalCFU = predictionPoints.Last().FungiCFU;
        double maxCFU = predictionPoints.Max(p => p.FungiCFU);
        double inflectionPointDay = CalculateInflectionPoint(B, C);
        double doublingTimeHours = CalculateDoublingTime(C);

        var riskLevel = CalculateRiskLevel(maxCFU);
        double confidence = CalculateConfidence(avgTemperature, avgHumidity);

        var prediction = new MildewPredictionGenerated
        {
            CorrelationId = Guid.NewGuid(),
            TextileId = textileId,
            HorizonDays = horizonDays,
            ModelType = "Gompertz",
            InitialCFU = Math.Round(y0, 2),
            CarryingCapacityK = Math.Round(A, 2),
            GrowthRateRho = Math.Round(C, 4),
            AvgTemperature = Math.Round(avgTemperature, 2),
            AvgHumidity = Math.Round(avgHumidity, 2),
            PredictionPoints = predictionPoints,
            FinalCFU = Math.Round(finalCFU, 2),
            MaxCFU = Math.Round(maxCFU, 2),
            DominantFungiType = dominantFungiType,
            RiskLevel = riskLevel,
            Confidence = Math.Round(confidence, 4),
            InflectionPointDay = Math.Round(inflectionPointDay, 2),
            DoublingTimeHours = Math.Round(doublingTimeHours, 2)
        };

        _logger.Information("Gompertz prediction completed for TextileId: {TextileId}, MaxCFU: {MaxCFU}, Risk: {RiskLevel}, Inflection: {InflectionDay} days",
            textileId, maxCFU, riskLevel, inflectionPointDay);

        return prediction;
    }

    private (double K, double Rho) AdjustParametersByEnvironment(double temperature, double humidity)
    {
        double baseK = _config.DefaultCarryingCapacityKf;
        double baseRho = _config.DefaultGrowthRateRho;

        double tempFactor = 1.0 + _config.TemperatureSensitivity * (temperature - 22.0);
        double humFactor = 1.0 + _config.HumiditySensitivity * (humidity - 55.0);

        tempFactor = Math.Max(0.5, Math.Min(2.0, tempFactor));
        humFactor = Math.Max(0.5, Math.Min(2.0, humFactor));

        double combinedFactor = tempFactor * humFactor * _config.SynergyInteractionPhi;

        double adjustedK = baseK * combinedFactor;
        double adjustedRho = baseRho * combinedFactor;

        _logger.Debug("Environment adjustment: TempFactor={TempFactor}, HumFactor={HumFactor}, Combined={CombinedFactor}",
            Math.Round(tempFactor, 4), Math.Round(humFactor, 4), Math.Round(combinedFactor, 4));

        return (adjustedK, adjustedRho);
    }

    private List<MildewPoint> GeneratePredictionPoints(double A, double B, double C, double y0, int horizonDays)
    {
        var points = new List<MildewPoint>(horizonDays + 1);
        double cumulativeSporeCount = 0;

        for (int day = 0; day <= horizonDays; day++)
        {
            double cfu = CalculateGompertz(A, B, C, day);
            double growthRate = CalculateGrowthRate(A, B, C, day);
            double sporeProduction = cfu * 0.1;
            cumulativeSporeCount += sporeProduction;

            points.Add(new MildewPoint
            {
                Day = day,
                FungiCFU = Math.Round(cfu, 4),
                GrowthRate = Math.Round(growthRate, 6),
                CumulativeSporeCount = Math.Round(cumulativeSporeCount, 2)
            });
        }

        return points;
    }

    private double CalculateGompertz(double A, double B, double C, double t)
    {
        return A * Math.Exp(-B * Math.Exp(-C * t));
    }

    private double CalculateGrowthRate(double A, double B, double C, double t)
    {
        double expCt = Math.Exp(-C * t);
        return A * B * C * expCt * Math.Exp(-B * expCt);
    }

    private double CalculateInflectionPoint(double B, double C)
    {
        if (B <= 0 || C <= 0) return 0;
        return Math.Log(B) / C;
    }

    private double CalculateDoublingTime(double C)
    {
        if (C <= 0) return double.PositiveInfinity;
        return (Math.Log(2) / C) * 24;
    }

    private string CalculateRiskLevel(double maxCFU)
    {
        return maxCFU switch
        {
            < 100 => "Low",
            < 200 => "Medium",
            < 300 => "High",
            _ => "Critical"
        };
    }

    private double CalculateConfidence(double temperature, double humidity)
    {
        double tempScore = 1.0 - Math.Abs(temperature - 22.0) / 20.0;
        double humScore = 1.0 - Math.Abs(humidity - 55.0) / 50.0;

        tempScore = Math.Max(0.5, Math.Min(1.0, tempScore));
        humScore = Math.Max(0.5, Math.Min(1.0, humScore));

        return (tempScore + humScore) / 2.0;
    }

    public AlertTriggered? CreateAlertIfNeeded(
        MildewPredictionGenerated prediction,
        string textileName,
        double currentCFU)
    {
        double threshold = _config.MoldDetectionThreshold;

        if (currentCFU > threshold)
        {
            var alertLevel = currentCFU > 300 ? "Critical" : "Warning";

            _logger.Warning("Mold alert triggered for TextileId: {TextileId}, CurrentCFU: {CurrentCFU}, Threshold: {Threshold}",
                prediction.TextileId, currentCFU, threshold);

            return new AlertTriggered
            {
                CorrelationId = Guid.NewGuid(),
                TextileId = prediction.TextileId,
                TextileName = textileName,
                AlertType = "MoldGrowth",
                AlertLevel = alertLevel,
                Title = $"霉菌超标警报 - {textileName}",
                Description = $"检测到真菌CFU值 {currentCFU:F2} 超过阈值 {threshold}，预测30天内最高可达 {prediction.MaxCFU:F2}",
                ActualValue = currentCFU,
                Threshold = threshold,
                SourcePredictionId = prediction.CorrelationId.ToString(),
                Recommendation = "建议立即采取防霉措施：降低湿度、增加通风、使用防霉剂",
                Metadata = new Dictionary<string, object>
                {
                    ["Model"] = prediction.ModelType,
                    ["HorizonDays"] = prediction.HorizonDays,
                    ["GrowthRate"] = prediction.GrowthRateRho,
                    ["CarryingCapacity"] = prediction.CarryingCapacityK,
                    ["InflectionPointDay"] = prediction.InflectionPointDay,
                    ["DoublingTimeHours"] = prediction.DoublingTimeHours,
                    ["PredictedMaxCFU"] = prediction.MaxCFU,
                    ["DominantFungiType"] = prediction.DominantFungiType ?? "Unknown"
                }
            };
        }

        return null;
    }
}
