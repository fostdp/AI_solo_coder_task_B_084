
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
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, result.PredictedMoldSpecies);
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
        Assert.NotEqual(MoldSpeciesFromVoc.Unknown, result.PredictedMoldSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.True(result.EstimatedBiomassMg > 0);
        Assert.InRange(result.EstimatedGrowthStageDays, 0, 120);
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
}
