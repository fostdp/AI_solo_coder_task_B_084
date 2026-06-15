using MathNet.Numerics;
using Microsoft.Extensions.Options;
using TextileMonitoring.Contracts.Messages;
using TreatmentSimulator.Service.Models;

namespace TreatmentSimulator.Service.Services;

public interface INitrogenProbitSimulator
{
    NitrogenTreatmentResult SimulateTreatment(NitrogenTreatmentRequest request);
}

public class NitrogenProbitSimulator : INitrogenProbitSimulator
{
    private readonly ProbitModelConfig _config;

    public NitrogenProbitSimulator(IOptions<ProbitModelConfig> config)
    {
        _config = config.Value;
    }

    public NitrogenTreatmentResult SimulateTreatment(NitrogenTreatmentRequest request)
    {
        var exposureMinutes = request.ExposureDurationMinutes;
        var oxygenPct = request.TargetOxygenConcentrationPct;
        var flowRate = request.NitrogenFlowRateLpm;
        var temp = request.ChamberTemperatureC;
        var humidity = request.ChamberHumidityPct;

        var tempFactor = CalculateTempFactor(temp);

        var eggMortality = CalculateMortality(exposureMinutes, oxygenPct, flowRate, tempFactor, _config.Eggs);
        var larvaeMortality = CalculateMortality(exposureMinutes, oxygenPct, flowRate, tempFactor, _config.Larvae);
        var adultMortality = CalculateMortality(exposureMinutes, oxygenPct, flowRate, tempFactor, _config.Adult);
        var fungiMortality = CalculateMortality(exposureMinutes, oxygenPct, flowRate, tempFactor, _config.Fungi);

        var avgYNormalized = CalculateAverageYNormalized(exposureMinutes, oxygenPct, flowRate, tempFactor);
        var probitValue = CalculateProbitValue(avgYNormalized);

        var se = _config.DeltaMethodBaseSE + _config.DeltaMethodExposureCoeff * exposureMinutes;
        var ciHalfWidth = _config.ConfidenceZScore * se;
        var avgMortality = (eggMortality + larvaeMortality + adultMortality + fungiMortality) / 4.0;
        var ciLow = Math.Max(0, avgMortality - ciHalfWidth) * 100;
        var ciHigh = Math.Min(100, avgMortality + ciHalfWidth) * 100;

        var ld99Min = CalculateLD99Minutes(oxygenPct, flowRate, tempFactor);
        var minRequiredExposure = CalculateMinRequiredExposure(request.TargetOrganisms, oxygenPct, flowRate, tempFactor);
        var recommendedSafetyExposure = minRequiredExposure * _config.SafetyFactor;

        var exposureHours = exposureMinutes / 60.0;
        var strengthLossPct = CalculateStrengthLoss(exposureHours, humidity, temp);
        var deltaE = CalculateDeltaE(exposureHours, temp, flowRate);

        var (isSuccess, riskNotes, monitoringDays) = EvaluateTreatmentSuccess(
            request.TargetOrganisms,
            eggMortality,
            larvaeMortality,
            adultMortality,
            fungiMortality,
            deltaE,
            strengthLossPct);

        return new NitrogenTreatmentResult
        {
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            RequestCorrelationId = request.CorrelationId,
            TextileId = request.TextileId,
            PredictedEggMortalityRate = eggMortality,
            PredictedLarvaeMortalityRate = larvaeMortality,
            PredictedAdultMortalityRate = adultMortality,
            PredictedFungiSterilityRate = fungiMortality,
            ConfidenceIntervalLowPct = ciLow,
            ConfidenceIntervalHighPct = ciHigh,
            ProbitTransformValue = probitValue,
            CalculatedLethalDoseLD99Min = ld99Min,
            MinimumRequiredExposureMin = minRequiredExposure,
            RecommendedSafetyExposureMin = recommendedSafetyExposure,
            FiberStrengthDegradationEstimatedPct = strengthLossPct,
            ColorChangeDeltaE = deltaE,
            IsSuccessCriteriaMet = isSuccess,
            TreatmentRiskNotes = riskNotes,
            PostTreatmentMonitoringDaysRecommended = monitoringDays
        };
    }

    private double CalculateTempFactor(double temp)
    {
        var delta = temp - _config.ReferenceTempC;
        return 1.0 + 0.045 * delta - 0.0015 * delta * delta;
    }

    private double CalculateDose(double exposureMinutes, double oxygenPct, double flowRate, double tempFactor, ProbitStageParameters stage)
    {
        var oxygenTerm = stage.K[0] * Math.Log(21.0 / oxygenPct);
        var flowTerm = stage.K[1] * (1.0 - Math.Exp(-flowRate / 6.0));
        var tempTerm = stage.K[2] * tempFactor;
        return exposureMinutes * (oxygenTerm + flowTerm + tempTerm);
    }

    private double CalculateMortality(double exposureMinutes, double oxygenPct, double flowRate, double tempFactor, ProbitStageParameters stage)
    {
        var dose = CalculateDose(exposureMinutes, oxygenPct, flowRate, tempFactor, stage);
        var yNormalized = stage.Beta * (Math.Log(dose / 100.0) - Math.Log(stage.LD50Minutes / 100.0));
        var mortality = 0.5 * SpecialFunctions.Erfc(-yNormalized / Math.Sqrt(2.0));
        return Math.Clamp(mortality * 100.0, 0.0, 100.0);
    }

    private double CalculateAverageYNormalized(double exposureMinutes, double oxygenPct, double flowRate, double tempFactor)
    {
        var stages = new[] { _config.Eggs, _config.Larvae, _config.Adult, _config.Fungi };
        var totalY = 0.0;
        foreach (var stage in stages)
        {
            var dose = CalculateDose(exposureMinutes, oxygenPct, flowRate, tempFactor, stage);
            totalY += stage.Beta * (Math.Log(dose / 100.0) - Math.Log(stage.LD50Minutes / 100.0));
        }
        return totalY / stages.Length;
    }

    private int CalculateProbitValue(double yNormalized)
    {
        var probit = 5.0 + yNormalized;
        return (int)Math.Round(probit);
    }

    private double SolveExposureForMortality(
        double targetMortalityRate,
        double oxygenPct,
        double flowRate,
        double tempFactor,
        ProbitStageParameters stage)
    {
        var targetProbit = 5.0 + Math.Sqrt(2.0) * SpecialFunctions.ErfcInv(2.0 * (1.0 - targetMortalityRate / 100.0));
        var targetY = targetProbit - 5.0;
        var lnDoseRatio = targetY / stage.Beta + Math.Log(stage.LD50Minutes / 100.0);
        var targetDose = 100.0 * Math.Exp(lnDoseRatio);

        var oxygenTerm = stage.K[0] * Math.Log(21.0 / oxygenPct);
        var flowTerm = stage.K[1] * (1.0 - Math.Exp(-flowRate / 6.0));
        var tempTerm = stage.K[2] * tempFactor;
        var perMinuteDose = oxygenTerm + flowTerm + tempTerm;

        return targetDose / perMinuteDose;
    }

    private double CalculateLD99Minutes(double oxygenPct, double flowRate, double tempFactor)
    {
        var stages = new[] { _config.Eggs, _config.Larvae, _config.Adult, _config.Fungi };
        var maxLd99 = 0.0;
        foreach (var stage in stages)
        {
            var ld99 = SolveExposureForMortality(99.0, oxygenPct, flowRate, tempFactor, stage);
            if (ld99 > maxLd99) maxLd99 = ld99;
        }
        return maxLd99;
    }

    private double CalculateMinRequiredExposure(TreatmentTarget target, double oxygenPct, double flowRate, double tempFactor)
    {
        var requiredStages = new List<ProbitStageParameters>();

        switch (target)
        {
            case TreatmentTarget.AllStages:
                requiredStages.AddRange(new[] { _config.Eggs, _config.Larvae, _config.Adult, _config.Fungi });
                break;
            case TreatmentTarget.EggsOnly:
                requiredStages.Add(_config.Eggs);
                break;
            case TreatmentTarget.LarvaeOnly:
                requiredStages.Add(_config.Larvae);
                break;
            case TreatmentTarget.AdultOnly:
                requiredStages.Add(_config.Adult);
                break;
            case TreatmentTarget.FungiSterilization:
                requiredStages.Add(_config.Fungi);
                break;
            default:
                requiredStages.AddRange(new[] { _config.Eggs, _config.Larvae, _config.Adult, _config.Fungi });
                break;
        }

        var maxExposure = 0.0;
        foreach (var stage in requiredStages)
        {
            var exposure = SolveExposureForMortality(_config.TargetMortalityPct, oxygenPct, flowRate, tempFactor, stage);
            if (exposure > maxExposure) maxExposure = exposure;
        }
        return maxExposure;
    }

    private double CalculateStrengthLoss(double exposureHours, double humidity, double temp)
    {
        return 0.012 * exposureHours + 0.005 * (100.0 - humidity) + 0.02 * Math.Max(0, temp - 28.0);
    }

    private double CalculateDeltaE(double exposureHours, double temp, double flowRate)
    {
        return 0.008 * exposureHours + 0.003 * Math.Abs(temp - 22.0) + 0.001 * flowRate * 0.1;
    }

    private (bool IsSuccess, string RiskNotes, int MonitoringDays) EvaluateTreatmentSuccess(
        TreatmentTarget target,
        double eggMortality,
        double larvaeMortality,
        double adultMortality,
        double fungiMortality,
        double deltaE,
        double strengthLossPct)
    {
        var targetMet = true;
        var risks = new List<string>();

        switch (target)
        {
            case TreatmentTarget.AllStages:
                if (eggMortality < 99.0) { targetMet = false; risks.Add($"虫卵死亡率不足: {eggMortality:F2}%"); }
                if (larvaeMortality < 99.0) { targetMet = false; risks.Add($"幼虫死亡率不足: {larvaeMortality:F2}%"); }
                if (adultMortality < 99.0) { targetMet = false; risks.Add($"成虫死亡率不足: {adultMortality:F2}%"); }
                if (fungiMortality < 99.0) { targetMet = false; risks.Add($"真菌灭活率不足: {fungiMortality:F2}%"); }
                break;
            case TreatmentTarget.EggsOnly:
                if (eggMortality < 99.0) { targetMet = false; risks.Add($"虫卵死亡率不足: {eggMortality:F2}%"); }
                break;
            case TreatmentTarget.LarvaeOnly:
                if (larvaeMortality < 99.0) { targetMet = false; risks.Add($"幼虫死亡率不足: {larvaeMortality:F2}%"); }
                break;
            case TreatmentTarget.AdultOnly:
                if (adultMortality < 99.0) { targetMet = false; risks.Add($"成虫死亡率不足: {adultMortality:F2}%"); }
                break;
            case TreatmentTarget.FungiSterilization:
                if (fungiMortality < 99.0) { targetMet = false; risks.Add($"真菌灭活率不足: {fungiMortality:F2}%"); }
                break;
        }

        if (deltaE > _config.MaxAllowedDeltaE)
        {
            targetMet = false;
            risks.Add($"色差超限: ΔE={deltaE:F3}");
        }

        if (strengthLossPct > _config.MaxAllowedStrengthLossPct)
        {
            targetMet = false;
            risks.Add($"纤维强度损失超限: {strengthLossPct:F3}%");
        }

        var minMortality = Math.Min(Math.Min(eggMortality, larvaeMortality), Math.Min(adultMortality, fungiMortality));
        int monitoringDays;
        if (minMortality >= 99.9)
        {
            monitoringDays = 14;
            if (risks.Count == 0) risks.Add("极高置信度，Critical级监测");
        }
        else if (minMortality >= 99.5)
        {
            monitoringDays = 7;
            if (risks.Count == 0) risks.Add("高置信度，High级监测");
        }
        else if (minMortality >= 99.0)
        {
            monitoringDays = 3;
            if (risks.Count == 0) risks.Add("中等置信度，Medium级监测");
        }
        else
        {
            monitoringDays = 1;
            if (risks.Count == 0) risks.Add("低置信度，Low级监测");
        }

        var riskNotes = risks.Count > 0 ? string.Join("; ", risks) : null;
        return (targetMet, riskNotes, monitoringDays);
    }
}
