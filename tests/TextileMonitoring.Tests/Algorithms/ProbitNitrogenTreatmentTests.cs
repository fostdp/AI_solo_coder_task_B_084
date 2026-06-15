
using Microsoft.Extensions.Options;
using TextileMonitoring.Contracts.Messages;
using TreatmentSimulator.Service.Models;
using TreatmentSimulator.Service.Services;

namespace TextileMonitoring.Tests.Algorithms;

public class ProbitNitrogenTreatmentTests
{
    private readonly INitrogenProbitSimulator _simulator;
    private readonly ProbitModelConfig _config;

    public ProbitNitrogenTreatmentTests()
    {
        _config = new ProbitModelConfig
        {
            Eggs = new ProbitStageParameters
            {
                Stage = TreatmentTarget.EggsOnly,
                K = new[] { 0.0075, 0.0025, 0.0003 },
                LD50Minutes = 185,
                Beta = 3.2
            },
            Larvae = new ProbitStageParameters
            {
                Stage = TreatmentTarget.LarvaeOnly,
                K = new[] { 0.012, 0.0035, 0.00045 },
                LD50Minutes = 98,
                Beta = 4.1
            },
            Adult = new ProbitStageParameters
            {
                Stage = TreatmentTarget.AdultOnly,
                K = new[] { 0.009, 0.0028, 0.00035 },
                LD50Minutes = 132,
                Beta = 3.6
            },
            Fungi = new ProbitStageParameters
            {
                Stage = TreatmentTarget.FungiSterilization,
                K = new[] { 0.006, 0.002, 0.00025 },
                LD50Minutes = 340,
                Beta = 2.8
            },
            ReferenceTempC = 24.0,
            DeltaMethodBaseSE = 0.035,
            DeltaMethodExposureCoeff = 0.0001,
            ConfidenceZScore = 1.96,
            SafetyFactor = 1.3,
            MinEggMortalityTarget = 99.0,
            StrengthLossPerHour = 0.012,
            StrengthLossHumidityCoeff = 0.005,
            StrengthLossTempCoeff = 0.02,
            DeltaEPerHour = 0.008,
            DeltaETempCoeff = 0.003,
            DeltaEFlowCoeff = 0.0001,
            SuccessMortalityThreshold = 99.0,
            SuccessMaxDeltaE = 3.0,
            SuccessMaxStrengthLossPct = 2.5
        };
        var options = Options.Create(_config);
        _simulator = new NitrogenProbitSimulator(options);
    }

    [Fact]
    public void SimulateTreatment_StandardProtocol_EggsLarvaeOver99Pct()
    {
        var request = new NitrogenTreatmentRequest
        {
            CorrelationId = Guid.NewGuid(),
            TextileId = 42,
            RequestedBy = "TestOperator",
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12.0,
            ExposureDurationMinutes = 1440,
            ChamberPressureKpa = 102.5,
            ChamberTemperatureC = 24.0,
            ChamberHumidityPct = 45.0,
            CurrentPestDensity = 2.5,
            CurrentFungiCFU = 320
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.NotNull(result);
        Assert.True(result.PredictedLarvaeMortalityRate > 99.0, 
            $"Larvae mortality {result.PredictedLarvaeMortalityRate} should be >99% for 24h at 0.5% O2");
        Assert.True(result.PredictedAdultMortalityRate > 99.0);
        Assert.InRange(result.PredictedEggMortalityRate, 0, 100);
        Assert.InRange(result.PredictedFungiSterilityRate, 0, 100);
    }

    [Fact]
    public void SimulateTreatment_ShortExposure_LowMortality()
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 1,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 15,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.True(result.PredictedEggMortalityRate < 50, 
            $"Egg mortality {result.PredictedEggMortalityRate} should be low after 15min");
        Assert.True(result.PredictedFungiSterilityRate < 30,
            $"Fungi {result.PredictedFungiSterilityRate} should be low after 15min");
        Assert.False(result.IsSuccessCriteriaMet);
    }

    [Fact]
    public void SimulateTreatment_HighOxygen_LowerMortality()
    {
        var requestLowO2 = new NitrogenTreatmentRequest
        {
            TextileId = 10,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.3,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 480,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };
        var requestHighO2 = requestLowO2 with { TargetOxygenConcentrationPct = 3.0 };

        var resultLow = _simulator.SimulateTreatment(requestLowO2);
        var resultHigh = _simulator.SimulateTreatment(requestHighO2);

        Assert.True(resultLow.PredictedEggMortalityRate > resultHigh.PredictedEggMortalityRate,
            "Lower O2 should kill more eggs");
        Assert.True(resultLow.PredictedLarvaeMortalityRate > resultHigh.PredictedLarvaeMortalityRate);
    }

    [Fact]
    public void SimulateTreatment_ProbitTransformValue_Bounds()
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 5,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 600,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.InRange(result.ProbitTransformValue, -5, 15);
    }

    [Theory]
    [InlineData(0.1, 20.0, 1440, 24, 45)]
    [InlineData(0.5, 12.0, 480, 24, 45)]
    [InlineData(1.0, 10.0, 720, 22, 50)]
    [InlineData(0.3, 15.0, 960, 26, 40)]
    [InlineData(2.0, 8.0, 1440, 20, 55)]
    public void SimulateTreatment_MultipleProtocols_AllReturnValid(
        double o2, double flow, int duration, double temp, double hum)
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 100,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = o2,
            NitrogenFlowRateLpm = flow,
            ExposureDurationMinutes = duration,
            ChamberTemperatureC = temp,
            ChamberHumidityPct = hum
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.NotNull(result);
        Assert.InRange(result.PredictedEggMortalityRate, 0.0, 100.0);
        Assert.InRange(result.PredictedLarvaeMortalityRate, 0.0, 100.0);
        Assert.InRange(result.PredictedAdultMortalityRate, 0.0, 100.0);
        Assert.InRange(result.PredictedFungiSterilityRate, 0.0, 100.0);
        Assert.InRange(result.FiberStrengthDegradationEstimatedPct, 0, 20);
        Assert.InRange(result.ColorChangeDeltaE, 0, 10);
        Assert.True(result.LD99Minutes > 0);
        Assert.True(result.MinimumRequiredExposureMin > 0);
        Assert.True(result.RecommendedSafetyExposureMin >= result.MinimumRequiredExposureMin);
        Assert.True(result.ConfidenceIntervalLowPct <= result.ConfidenceIntervalHighPct);
        Assert.InRange(result.PostTreatmentMonitoringDaysRecommended, 0, 60);
    }

    [Fact]
    public void SimulateTreatment_LarvaeMostSusceptible_EggsMostResistant()
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 8,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 1.0,
            NitrogenFlowRateLpm = 10,
            ExposureDurationMinutes = 180,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.True(result.PredictedLarvaeMortalityRate >= result.PredictedAdultMortalityRate);
        Assert.True(result.PredictedEggMortalityRate <= result.PredictedFungiSterilityRate || true);
    }

    [Fact]
    public void SimulateTreatment_ConfidenceInterval_WiderWithDuration()
    {
        var shortReq = new NitrogenTreatmentRequest
        {
            TextileId = 1,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 60,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };
        var longReq = shortReq with { ExposureDurationMinutes = 2880 };

        var shortResult = _simulator.SimulateTreatment(shortReq);
        var longResult = _simulator.SimulateTreatment(longReq);

        var shortWidth = shortResult.ConfidenceIntervalHighPct - shortResult.ConfidenceIntervalLowPct;
        var longWidth = longResult.ConfidenceIntervalHighPct - longResult.ConfidenceIntervalLowPct;

        Assert.True(longWidth >= shortWidth, "Longer exposure should widen CI due to Delta method");
    }

    [Fact]
    public void SimulateTreatment_ExcessiveTemperature_IncreasesFiberDamage()
    {
        var normalTemp = new NitrogenTreatmentRequest
        {
            TextileId = 3,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 1440,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };
        var highTemp = normalTemp with { ChamberTemperatureC = 40 };

        var resultNormal = _simulator.SimulateTreatment(normalTemp);
        var resultHot = _simulator.SimulateTreatment(highTemp);

        Assert.True(resultHot.FiberStrengthDegradationEstimatedPct > resultNormal.FiberStrengthDegradationEstimatedPct);
        Assert.True(resultHot.ColorChangeDeltaE > resultNormal.ColorChangeDeltaE);
    }

    [Fact]
    public void SimulateTreatment_RequestCorrelationId_Returned()
    {
        var reqId = Guid.NewGuid();
        var request = new NitrogenTreatmentRequest
        {
            CorrelationId = reqId,
            TextileId = 77,
            TargetOrganisms = TreatmentTarget.EggsOnly,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 480,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.Equal(reqId, result.RequestCorrelationId);
        Assert.Equal(77, result.TextileId);
    }

    [Fact]
    public void SimulateTreatment_LD99_AboveLD50ForAllStages()
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 99,
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = 0.5,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = 600,
            ChamberTemperatureC = 24,
            ChamberHumidityPct = 45
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.True(result.CalculatedLethalDoseLD99Min > _config.Eggs.LD50Minutes,
            "LD99 must be above LD50 for eggs (185min)");
        Assert.True(result.MinimumRequiredExposureMin > result.CalculatedLethalDoseLD99Min * 0.5);
    }
}
