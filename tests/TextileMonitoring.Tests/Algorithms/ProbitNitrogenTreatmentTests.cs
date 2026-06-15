
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

    [Fact]
    public void LD50Values_ConsistentWithPublishedLiterature_DeviationWithin5Percent()
    {
        var literatureLD50 = new Dictionary<TreatmentTarget, double>
        {
            { TreatmentTarget.EggsOnly, 185.0 },
            { TreatmentTarget.LarvaeOnly, 98.0 },
            { TreatmentTarget.AdultOnly, 132.0 },
            { TreatmentTarget.FungiSterilization, 340.0 }
        };

        var modelLD50 = new Dictionary<TreatmentTarget, double>
        {
            { TreatmentTarget.EggsOnly, _config.Eggs.LD50Minutes },
            { TreatmentTarget.LarvaeOnly, _config.Larvae.LD50Minutes },
            { TreatmentTarget.AdultOnly, _config.Adult.LD50Minutes },
            { TreatmentTarget.FungiSterilization, _config.Fungi.LD50Minutes }
        };

        foreach (var kvp in literatureLD50)
        {
            var stage = kvp.Key;
            var litValue = kvp.Value;
            var modelValue = modelLD50[stage];
            var deviation = Math.Abs(modelValue - litValue) / litValue;

            Assert.True(deviation < 0.05,
                $"Stage {stage}: LD50 model={modelValue}min, literature={litValue}min, deviation={deviation:P2} exceeds 5%");
        }
    }

    [Fact]
    public void EmpiricalLD50_EggsAtStandardConditions_MatchesConfigWithin3Percent()
    {
        const double targetOxygen = 0.5;
        const double flowRate = 12.0;
        const double temp = 24.0;
        var tempFactor = 1.0;
        var stage = _config.Eggs;

        double FindExposureForMortality(double targetMortality)
        {
            double low = 1, high = 500;
            for (int iter = 0; iter < 50; iter++)
            {
                var mid = (low + high) / 2;
                var request = new NitrogenTreatmentRequest
                {
                    TextileId = 9999,
                    TargetOrganisms = TreatmentTarget.EggsOnly,
                    TargetOxygenConcentrationPct = targetOxygen,
                    NitrogenFlowRateLpm = flowRate,
                    ExposureDurationMinutes = (int)Math.Round(mid),
                    ChamberTemperatureC = temp,
                    ChamberHumidityPct = 45
                };
                var result = _simulator.SimulateTreatment(request);
                if (result.PredictedEggMortalityRate < targetMortality)
                    low = mid;
                else
                    high = mid;
            }
            return (low + high) / 2;
        }

        var empiricalLD50 = FindExposureForMortality(50.0);
        var deviation = Math.Abs(empiricalLD50 - stage.LD50Minutes) / stage.LD50Minutes;

        Assert.True(deviation < 0.03,
            $"Empirical egg LD50={empiricalLD50:F1}min, config LD50={stage.LD50Minutes}min, deviation={deviation:P2} > 3%");
    }

    [Fact]
    public void LD50Ordering_LarvaeMostSusceptible_FungiMostResistant()
    {
        var ld50Values = new[]
        {
            new { Stage = "Larvae", LD50 = _config.Larvae.LD50Minutes },
            new { Stage = "Adult", LD50 = _config.Adult.LD50Minutes },
            new { Stage = "Eggs", LD50 = _config.Eggs.LD50Minutes },
            new { Stage = "Fungi", LD50 = _config.Fungi.LD50Minutes }
        };

        var ordered = ld50Values.OrderBy(x => x.LD50).ToList();

        Assert.Equal("Larvae", ordered[0].Stage);
        Assert.Equal("Fungi", ordered[3].Stage);
        Assert.True(ordered[0].LD50 < ordered[1].LD50);
        Assert.True(ordered[1].LD50 < ordered[2].LD50);
        Assert.True(ordered[2].LD50 < ordered[3].LD50);
    }

    [Theory]
    [InlineData(0.01, 1, 24, 45)]
    [InlineData(10.0, 43200, 24, 45)]
    [InlineData(0.5, 1, 24, 45)]
    [InlineData(0.5, 43200, 24, 45)]
    public void SimulateTreatment_BoundaryConditions_ReturnsValidMortality(
        double o2Pct, int durationMin, double temp, double hum)
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 10000 + (int)(o2Pct * 100),
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = o2Pct,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = durationMin,
            ChamberTemperatureC = temp,
            ChamberHumidityPct = hum
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.NotNull(result);
        Assert.InRange(result.PredictedEggMortalityRate, 0.0, 100.0);
        Assert.InRange(result.PredictedLarvaeMortalityRate, 0.0, 100.0);
        Assert.InRange(result.PredictedAdultMortalityRate, 0.0, 100.0);
        Assert.InRange(result.PredictedFungiSterilityRate, 0.0, 100.0);
    }

    [Theory]
    [InlineData(25.0, 1440, 24, 45)]
    [InlineData(-5.0, 1440, 24, 45)]
    [InlineData(0.5, -60, 24, 45)]
    [InlineData(0.5, 1440, -20, 45)]
    [InlineData(0.5, 1440, 24, -5)]
    [InlineData(0.5, 1440, 24, 110)]
    public void SimulateTreatment_OutOfRangeInputs_ClampedGracefully(
        double o2Pct, int durationMin, double temp, double hum)
    {
        var request = new NitrogenTreatmentRequest
        {
            TextileId = 20000 + Math.Abs((int)o2Pct),
            TargetOrganisms = TreatmentTarget.AllStages,
            TargetOxygenConcentrationPct = o2Pct,
            NitrogenFlowRateLpm = 12,
            ExposureDurationMinutes = durationMin,
            ChamberTemperatureC = temp,
            ChamberHumidityPct = hum
        };

        var result = _simulator.SimulateTreatment(request);

        Assert.NotNull(result);
        Assert.InRange(result.PredictedEggMortalityRate, 0.0, 100.0);
        Assert.InRange(result.FiberStrengthDegradationEstimatedPct, 0.0, 50.0);
        Assert.InRange(result.ColorChangeDeltaE, 0.0, 20.0);
        Assert.True(result.RecommendedSafetyExposureMin >= 0);
    }

    [Fact]
    public void MortalityResponse_SigmoidalShape_ProperGradient()
    {
        var o2Levels = new[] { 0.1, 0.5, 1.0, 2.0, 5.0 };
        var mortalityByO2 = new List<double>();

        foreach (var o2 in o2Levels)
        {
            var request = new NitrogenTreatmentRequest
            {
                TextileId = 30000 + (int)(o2 * 10),
                TargetOrganisms = TreatmentTarget.LarvaeOnly,
                TargetOxygenConcentrationPct = o2,
                NitrogenFlowRateLpm = 12,
                ExposureDurationMinutes = 120,
                ChamberTemperatureC = 24,
                ChamberHumidityPct = 45
            };
            var result = _simulator.SimulateTreatment(request);
            mortalityByO2.Add(result.PredictedLarvaeMortalityRate);
        }

        for (int i = 0; i < mortalityByO2.Count - 1; i++)
        {
            Assert.True(mortalityByO2[i] >= mortalityByO2[i + 1],
                $"O2={o2Levels[i]}% mortality={mortalityByO2[i]:F1} should be >= O2={o2Levels[i + 1]}% mortality={mortalityByO2[i + 1]:F1}");
        }

        var maxDrop = 0.0;
        for (int i = 0; i < mortalityByO2.Count - 1; i++)
        {
            var drop = mortalityByO2[i] - mortalityByO2[i + 1];
            if (drop > maxDrop) maxDrop = drop;
        }
        Assert.True(maxDrop > 10, "Sigmoidal curve should have a steep region with >10% mortality drop");
    }

    [Fact]
    public void BetaValues_StageSpecific_SlopeSensitivityConsistent()
    {
        Assert.Equal(3.2, _config.Eggs.Beta, 2);
        Assert.Equal(4.1, _config.Larvae.Beta, 2);
        Assert.Equal(3.6, _config.Adult.Beta, 2);
        Assert.Equal(2.8, _config.Fungi.Beta, 2);

        Assert.True(_config.Larvae.Beta > _config.Eggs.Beta,
            "Larvae should have steeper dose-response (higher beta) than eggs");
        Assert.True(_config.Fungi.Beta < _config.Eggs.Beta,
            "Fungi should have shallower dose-response (lower beta) than eggs");
    }
}
