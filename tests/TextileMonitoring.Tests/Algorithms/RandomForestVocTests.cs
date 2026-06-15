
using Microsoft.Extensions.Options;
using TextileMonitoring.Contracts.Messages;
using VocClassifier.Service.Models;
using VocClassifier.Service.Services;

namespace TextileMonitoring.Tests.Algorithms;

public class RandomForestVocTests
{
    private readonly RandomForestVocClassifier _classifier;

    public RandomForestVocTests()
    {
        var config = new RandomForestConfig
        {
            NumberOfTrees = 200,
            MinOobErrorBps = 65,
            MaxOobErrorBps = 180,
            MoldThresholdLow = 100,
            MoldThresholdMedium = 200,
            MoldThresholdHigh = 350,
            MycotoxinRiskMultiplier = 1.5
        };
        var options = Options.Create(config);
        _classifier = new RandomForestVocClassifier(options);
    }

    [Fact]
    public void Classify_AspergillusProfile_HighOctenol()
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 1,
            SensorCode = "VOC-001",
            ToluenePPB = 35,
            XylenePPB = 28,
            EthylbenzenePPB = 20,
            FormaldehydePPB = 42,
            AcetaldehydePPB = 35,
            _1Octen3OlPPB = 6.5,
            GeosminPPT = 45,
            _2MethylisoborneolPPT = 30,
            TotalVolatilePPB = 245,
            Temperature = 25,
            Humidity = 65
        };

        var result = _classifier.Classify(voc);

        Assert.NotNull(result);
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, result.PredictedSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.InRange(result.EarlyWarningSeverity, 1, 10);
        Assert.True(result.DecisionTreeVotes > 100 && result.DecisionTreeVotes <= 200);
    }

    [Theory]
    [InlineData(35, 28, 20, 42, 35, 6.5, 45, 30, 245)]
    [InlineData(25, 22, 15, 30, 28, 4.5, 78, 55, 205)]
    [InlineData(40, 35, 28, 25, 22, 3.5, 30, 72, 200)]
    [InlineData(50, 45, 38, 22, 18, 5.5, 28, 25, 235)]
    [InlineData(22, 18, 12, 50, 42, 7.5, 35, 28, 210)]
    [InlineData(58, 52, 45, 38, 30, 4.0, 50, 45, 300)]
    public void Classify_SixMoldSpeciesProfiles_AllValid(
        double tol, double xyl, double ethb, double form, double acet,
        double octen, double geos, double mib, double total)
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 50,
            SensorCode = "VOC-TEST",
            ToluenePPB = tol,
            XylenePPB = xyl,
            EthylbenzenePPB = ethb,
            FormaldehydePPB = form,
            AcetaldehydePPB = acet,
            _1Octen3OlPPB = octen,
            GeosminPPT = geos,
            _2MethylisoborneolPPT = mib,
            TotalVolatilePPB = total,
            Temperature = 24,
            Humidity = 60
        };

        var result = _classifier.Classify(voc);

        Assert.NotNull(result);
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, result.PredictedSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.True(result.EstimatedBiomassMg > 0);
        Assert.InRange(result.EstimatedGrowthDays, 0, 120);
        Assert.InRange(result.OobErrorRateBps, 65, 180);
        Assert.True(result.DecisionTreeVotes > 0);
    }

    [Fact]
    public void SpeciesProbabilities_SixMolds_SumToOne()
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 200,
            SensorCode = "VOC-SUM",
            ToluenePPB = 30,
            XylenePPB = 25,
            EthylbenzenePPB = 18,
            FormaldehydePPB = 35,
            AcetaldehydePPB = 28,
            _1Octen3OlPPB = 5.0,
            GeosminPPT = 40,
            _2MethylisoborneolPPT = 35,
            TotalVolatilePPB = 220
        };

        var result = _classifier.Classify(voc);

        var sum = result.SpeciesProbabilities.Values.Sum();
        Assert.InRange(sum, 0.99, 1.01);
        Assert.Equal(6, result.SpeciesProbabilities.Count);
    }

    [Fact]
    public void Classify_ExtremelyHighVOCs_HighBiomassHighSeverity()
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 777,
            SensorCode = "VOC-EXTREME",
            ToluenePPB = 350,
            XylenePPB = 280,
            EthylbenzenePPB = 180,
            FormaldehydePPB = 420,
            AcetaldehydePPB = 310,
            _1Octen3OlPPB = 48,
            GeosminPPT = 550,
            _2MethylisoborneolPPT = 480,
            TotalVolatilePPB = 2618
        };

        var result = _classifier.Classify(voc);

        Assert.True(result.EstimatedBiomassMg > 50);
        Assert.True(result.EarlyWarningSeverity >= 8);
        Assert.True(result.MycotoxinRiskIndex > 0.6);
        Assert.True(result.PredictedIncubationHours < 72);
    }

    [Fact]
    public void Classify_LowVOCLevels_LowSeverityLongIncubation()
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 101,
            SensorCode = "VOC-LOW",
            ToluenePPB = 3,
            XylenePPB = 2,
            EthylbenzenePPB = 1,
            FormaldehydePPB = 4,
            AcetaldehydePPB = 3,
            _1Octen3OlPPB = 0.1,
            GeosminPPT = 1.5,
            _2MethylisoborneolPPT = 1.0,
            TotalVolatilePPB = 14.6
        };

        var result = _classifier.Classify(voc);

        Assert.True(result.EstimatedBiomassMg < 5);
        Assert.True(result.EarlyWarningSeverity <= 4);
        Assert.True(result.PredictedIncubationHours > 168);
    }

    [Fact]
    public void Classify_ModelVersion_IsCorrect()
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 555,
            SensorCode = "VOC-VERSION",
            ToluenePPB = 15, XylenePPB = 12, EthylbenzenePPB = 8,
            FormaldehydePPB = 18, AcetaldehydePPB = 14,
            _1Octen3OlPPB = 2.5, GeosminPPT = 18, _2MethylisoborneolPPT = 15,
            TotalVolatilePPB = 92.5
        };

        var result = _classifier.Classify(voc);

        Assert.Equal("rf-voc-v1.0", result.ModelVersion);
    }

    [Fact]
    public void Classify_SourceCorrelationId_Preserved()
    {
        var sourceId = Guid.NewGuid();
        var voc = new VocSensorDataReceived
        {
            CorrelationId = sourceId,
            TextileId = 999,
            SensorCode = "VOC-SRC",
            ToluenePPB = 22, XylenePPB = 18, EthylbenzenePPB = 13,
            FormaldehydePPB = 26, AcetaldehydePPB = 20,
            _1Octen3OlPPB = 3.6, GeosminPPT = 26, _2MethylisoborneolPPT = 22,
            TotalVolatilePPB = 130.6
        };

        var result = _classifier.Classify(voc);

        Assert.Equal(sourceId, result.SourceSensorCorrelationId);
    }

    [Fact]
    public void Classify_FeatureImportanceGiniTop3_Positive()
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 314,
            SensorCode = "VOC-GINI",
            ToluenePPB = 20, XylenePPB = 16, EthylbenzenePPB = 11,
            FormaldehydePPB = 23, AcetaldehydePPB = 18,
            _1Octen3OlPPB = 3.2, GeosminPPT = 24, _2MethylisoborneolPPT = 20,
            TotalVolatilePPB = 115.2
        };

        var result = _classifier.Classify(voc);

        Assert.True(result.FeatureImportanceGiniTop3 > 0);
    }

    [Fact]
    public void MacroF1Score_SixSpeciesWithNoise_Above0Point8()
    {
        var speciesProfiles = new (MoldSpeciesFromVoc Species, double[] Profile)[]
        {
            (MoldSpeciesFromVoc.AspergillusNiger, new double[] { 35, 28, 20, 42, 35, 6.5, 45, 30, 245 }),
            (MoldSpeciesFromVoc.PenicilliumChrysogenum, new double[] { 25, 22, 15, 30, 28, 4.5, 78, 55, 205 }),
            (MoldSpeciesFromVoc.CladosporiumHerbarum, new double[] { 40, 35, 28, 25, 22, 3.5, 30, 72, 200 }),
            (MoldSpeciesFromVoc.AlternariaAlternata, new double[] { 50, 45, 38, 22, 18, 5.5, 28, 25, 235 }),
            (MoldSpeciesFromVoc.TrichodermaViride, new double[] { 22, 18, 12, 50, 42, 7.5, 35, 28, 210 }),
            (MoldSpeciesFromVoc.FusariumGraminearum, new double[] { 58, 52, 45, 38, 30, 4.0, 50, 45, 300 })
        };

        var speciesList = speciesProfiles.Select(p => p.Species).ToList();
        var numSpecies = speciesList.Count;
        var confusionMatrix = new int[numSpecies, numSpecies];
        var rnd = new Random(12345);
        const int samplesPerSpecies = 15;
        const double noiseLevel = 0.10;

        for (int s = 0; s < numSpecies; s++)
        {
            var trueSpecies = speciesList[s];
            var profile = speciesProfiles[s].Profile;

            for (int i = 0; i < samplesPerSpecies; i++)
            {
                var voc = new VocSensorDataReceived
                {
                    TextileId = 10000 + s * 100 + i,
                    SensorCode = $"VOC-F1-{s}-{i}",
                    ToluenePPB = Math.Max(0, profile[0] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    XylenePPB = Math.Max(0, profile[1] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    EthylbenzenePPB = Math.Max(0, profile[2] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    FormaldehydePPB = Math.Max(0, profile[3] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    AcetaldehydePPB = Math.Max(0, profile[4] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    _1Octen3OlPPB = Math.Max(0, profile[5] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    GeosminPPT = Math.Max(0, profile[6] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    _2MethylisoborneolPPT = Math.Max(0, profile[7] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    TotalVolatilePPB = Math.Max(10, profile[8] * (1 + rnd.NextDouble() * 2 * noiseLevel - noiseLevel)),
                    Temperature = 24,
                    Humidity = 60
                };

                var result = _classifier.Classify(voc);
                var predictedIdx = speciesList.IndexOf(result.PredictedSpecies);
                if (predictedIdx >= 0)
                {
                    confusionMatrix[s, predictedIdx]++;
                }
            }
        }

        double macroF1 = 0;
        for (int s = 0; s < numSpecies; s++)
        {
            int tp = confusionMatrix[s, s];
            int fp = Enumerable.Range(0, numSpecies).Sum(i => i != s ? confusionMatrix[i, s] : 0);
            int fn = Enumerable.Range(0, numSpecies).Sum(i => i != s ? confusionMatrix[s, i] : 0);

            double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0;
            double recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0;
            double f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;
            macroF1 += f1;
        }
        macroF1 /= numSpecies;

        Assert.True(macroF1 > 0.8,
            $"Macro-F1 score {macroF1:F4} should exceed 0.8 across {numSpecies} species, {samplesPerSpecies} samples each");
    }

    [Fact]
    public void ConfusionMatrix_SixSpecies_DiagonallyDominant()
    {
        var speciesProfiles = new (MoldSpeciesFromVoc Species, double[] Profile)[]
        {
            (MoldSpeciesFromVoc.AspergillusNiger, new double[] { 35, 28, 20, 42, 35, 6.5, 45, 30, 245 }),
            (MoldSpeciesFromVoc.PenicilliumChrysogenum, new double[] { 25, 22, 15, 30, 28, 4.5, 78, 55, 205 }),
            (MoldSpeciesFromVoc.CladosporiumHerbarum, new double[] { 40, 35, 28, 25, 22, 3.5, 30, 72, 200 }),
            (MoldSpeciesFromVoc.AlternariaAlternata, new double[] { 50, 45, 38, 22, 18, 5.5, 28, 25, 235 }),
            (MoldSpeciesFromVoc.TrichodermaViride, new double[] { 22, 18, 12, 50, 42, 7.5, 35, 28, 210 }),
            (MoldSpeciesFromVoc.FusariumGraminearum, new double[] { 58, 52, 45, 38, 30, 4.0, 50, 45, 300 })
        };

        var speciesList = speciesProfiles.Select(p => p.Species).ToList();
        var numSpecies = speciesList.Count;
        var confusionMatrix = new int[numSpecies, numSpecies];
        var rnd = new Random(999);

        for (int s = 0; s < numSpecies; s++)
        {
            var profile = speciesProfiles[s].Profile;
            for (int i = 0; i < 10; i++)
            {
                var noise = 0.05;
                var voc = new VocSensorDataReceived
                {
                    TextileId = 20000 + s * 100 + i,
                    SensorCode = "VOC-CM",
                    ToluenePPB = Math.Max(0, profile[0] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    XylenePPB = Math.Max(0, profile[1] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    EthylbenzenePPB = Math.Max(0, profile[2] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    FormaldehydePPB = Math.Max(0, profile[3] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    AcetaldehydePPB = Math.Max(0, profile[4] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    _1Octen3OlPPB = Math.Max(0, profile[5] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    GeosminPPT = Math.Max(0, profile[6] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    _2MethylisoborneolPPT = Math.Max(0, profile[7] * (1 + rnd.NextDouble() * 2 * noise - noise)),
                    TotalVolatilePPB = Math.Max(10, profile[8] * (1 + rnd.NextDouble() * 2 * noise - noise))
                };

                var result = _classifier.Classify(voc);
                var predictedIdx = speciesList.IndexOf(result.PredictedSpecies);
                if (predictedIdx >= 0)
                    confusionMatrix[s, predictedIdx]++;
            }
        }

        for (int s = 0; s < numSpecies; s++)
        {
            int diag = confusionMatrix[s, s];
            int maxOffDiag = 0;
            for (int j = 0; j < numSpecies; j++)
            {
                if (j != s && confusionMatrix[s, j] > maxOffDiag)
                    maxOffDiag = confusionMatrix[s, j];
            }
            Assert.True(diag >= maxOffDiag,
                $"Species {speciesList[s]}: diagonal={diag} should be >= max off-diagonal={maxOffDiag}");
        }
    }

    [Theory]
    [InlineData(1, 1, 1, 1, 1, 0.05, 1, 1, 5)]
    [InlineData(500, 400, 300, 100, 80, 50, 2000, 1500, 3000)]
    public void Classify_BoundaryVocLevels_ReturnsValidPrediction(
        double tol, double xyl, double ethb, double form, double acet,
        double octen, double geos, double mib, double total)
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 30000 + (int)tol,
            SensorCode = "VOC-BOUNDARY",
            ToluenePPB = tol,
            XylenePPB = xyl,
            EthylbenzenePPB = ethb,
            FormaldehydePPB = form,
            AcetaldehydePPB = acet,
            _1Octen3OlPPB = octen,
            GeosminPPT = geos,
            _2MethylisoborneolPPT = mib,
            TotalVolatilePPB = total
        };

        var result = _classifier.Classify(voc);

        Assert.NotNull(result);
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, result.PredictedSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        var sum = result.SpeciesProbabilities.Values.Sum();
        Assert.InRange(sum, 0.99, 1.01);
    }

    [Theory]
    [InlineData(-10, 25, 18, 35, 28, 5.0, 40, 35, 220)]
    [InlineData(30, -5, 18, 35, 28, 5.0, 40, 35, 220)]
    [InlineData(30, 25, 18, 35, 28, -2.5, 40, 35, 220)]
    [InlineData(30, 25, 18, 35, 28, 5.0, 40, 35, -100)]
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0)]
    public void Classify_NegativeOrZeroVOC_ClampedAndClassified(
        double tol, double xyl, double ethb, double form, double acet,
        double octen, double geos, double mib, double total)
    {
        var voc = new VocSensorDataReceived
        {
            TextileId = 40000 + Math.Abs((int)tol),
            SensorCode = "VOC-NEGATIVE",
            ToluenePPB = tol,
            XylenePPB = xyl,
            EthylbenzenePPB = ethb,
            FormaldehydePPB = form,
            AcetaldehydePPB = acet,
            _1Octen3OlPPB = octen,
            GeosminPPT = geos,
            _2MethylisoborneolPPT = mib,
            TotalVolatilePPB = total
        };

        var result = _classifier.Classify(voc);

        Assert.NotNull(result);
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, result.PredictedSpecies);
        Assert.True(result.EstimatedBiomassMg >= 0);
        Assert.True(result.OobErrorRateBps >= 65 && result.OobErrorRateBps <= 180);
    }

    [Fact]
    public void Classify_ExtremeTemperatureHumidity_StillClassifies()
    {
        var vocExtreme = new VocSensorDataReceived
        {
            TextileId = 50000,
            SensorCode = "VOC-EXTREME-TH",
            ToluenePPB = 30, XylenePPB = 25, EthylbenzenePPB = 18,
            FormaldehydePPB = 35, AcetaldehydePPB = 28,
            _1Octen3OlPPB = 5.0, GeosminPPT = 40, _2MethylisoborneolPPT = 35,
            TotalVolatilePPB = 220,
            Temperature = -10,
            Humidity = 5
        };

        var vocNormal = vocExtreme with { Temperature = 25, Humidity = 65 };

        var resultExtreme = _classifier.Classify(vocExtreme);
        var resultNormal = _classifier.Classify(vocNormal);

        Assert.NotNull(resultExtreme);
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, resultExtreme.PredictedSpecies);
        Assert.True(resultExtreme.PredictedIncubationHours > 0);
        Assert.True(resultNormal.PredictedIncubationHours < resultExtreme.PredictedIncubationHours ||
                    resultNormal.EstimatedGrowthDays < resultExtreme.EstimatedGrowthDays,
            "Normal temp/humidity should promote faster mold growth than extreme conditions");
    }
}
