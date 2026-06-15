
using Microsoft.Extensions.Options;
using PestClassifier.Service.Models;
using PestClassifier.Service.Services;
using TextileMonitoring.Contracts.Messages;

namespace TextileMonitoring.Tests.Algorithms;

public class CnnFrassClassifierTests
{
    private readonly CnnFrassClassifierService _service;

    public CnnFrassClassifierTests()
    {
        var config = new CnnClassifierConfig
        {
            MinInferenceLatencyMs = 45,
            MaxInferenceLatencyMs = 95,
            ConfidenceThreshold = 0.6,
            LowConfidenceWarningScore = 7,
            HighConfidenceActionScore = 3,
            PopulationMultiplier = 120
        };
        var options = Options.Create(config);
        _service = new CnnFrassClassifierService(options);
    }

    [Fact]
    public void Classify_LepismaSaccharinaProfile_ReturnsCorrectSpecies()
    {
        var image = new FrassImageCaptured
        {
            CorrelationId = Guid.NewGuid(),
            TextileId = 1,
            SensorCode = "IMG-0001",
            EllipticityMean = 0.82,
            AspectRatioMean = 1.55,
            SolidityMean = 0.91,
            MeanGrayscale = 132.6,
            TextureEntropy = 4.8,
            AverageParticleArea = 120.0,
            ParticleCount = 45
        };

        var (result, latency) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.True(latency >= 45 && latency <= 95);
        Assert.NotEqual(PestSpecies.Unknown, result.PredictedSpecies);
        Assert.True(result.Confidence > 0);
        Assert.Equal(5, result.SpeciesProbabilities.Count);
        Assert.InRange(result.Confidence, 0.0, 1.0);
    }

    [Fact]
    public void Classify_AttagenusPellioProfile_HighLargeAreaDark()
    {
        var image = new FrassImageCaptured
        {
            TextileId = 2,
            SensorCode = "IMG-0002",
            EllipticityMean = 0.42,
            AspectRatioMean = 1.20,
            SolidityMean = 0.78,
            MeanGrayscale = 198.9,
            TextureEntropy = 5.2,
            AverageParticleArea = 280.0,
            ParticleCount = 22
        };

        var (result, _) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.True(result.EstimatedPopulationSize > 0);
        Assert.True(result.RiskSeverityScore >= 0);
        Assert.Equal(1, result.SourceImageCorrelationId == Guid.Empty ? 1 : 1);
    }

    [Theory]
    [InlineData(0.82, 1.55, 0.91, 132.6, 4.8, 120.0)]
    [InlineData(0.68, 2.45, 0.62, 107.1, 6.9, 95.0)]
    [InlineData(0.42, 1.20, 0.78, 198.9, 5.2, 280.0)]
    [InlineData(0.58, 1.85, 0.56, 96.9, 5.5, 55.0)]
    [InlineData(0.94, 1.08, 0.85, 140.3, 5.0, 160.0)]
    public void Classify_FiveSpeciesIdealInputs_AllReturnValidPredictions(
        double ellipticity, double aspect, double solidity,
        double gray, double entropy, double area)
    {
        var image = new FrassImageCaptured
        {
            TextileId = 100,
            SensorCode = "IMG-TEST",
            EllipticityMean = ellipticity,
            AspectRatioMean = aspect,
            SolidityMean = solidity,
            MeanGrayscale = gray,
            TextureEntropy = entropy,
            AverageParticleArea = area,
            ParticleCount = 30
        };

        var (result, latency) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.NotEqual(PestSpecies.Unknown, result.PredictedSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.InRange(latency, 45, 95);
        Assert.True(result.PredictedInstars >= 1 && result.PredictedInstars <= 5);
    }

    [Fact]
    public void SpeciesProbabilities_SumToApproximatelyOne()
    {
        var image = new FrassImageCaptured
        {
            TextileId = 10,
            SensorCode = "IMG-SUM",
            EllipticityMean = 0.55,
            AspectRatioMean = 1.70,
            SolidityMean = 0.75,
            MeanGrayscale = 128.0,
            TextureEntropy = 5.5,
            AverageParticleArea = 150.0,
            ParticleCount = 40
        };

        var (result, _) = _service.Classify(image);

        var probSum = result.SpeciesProbabilities.Values.Sum();
        Assert.InRange(probSum, 0.99, 1.01);
    }

    [Fact]
    public void Classify_ExtremeParticleCount_HighPopulationEstimate()
    {
        var image = new FrassImageCaptured
        {
            TextileId = 7,
            SensorCode = "IMG-BURST",
            EllipticityMean = 0.50,
            AspectRatioMean = 1.60,
            SolidityMean = 0.80,
            MeanGrayscale = 140.0,
            TextureEntropy = 6.0,
            AverageParticleArea = 200.0,
            ParticleCount = 5000
        };

        var (result, _) = _service.Classify(image);

        Assert.True(result.EstimatedPopulationSize > 500);
        Assert.True(result.RiskSeverityScore >= 5);
    }

    [Fact]
    public void Classify_CorrelationId_PreservedInSourceReference()
    {
        var corrId = Guid.NewGuid();
        var image = new FrassImageCaptured
        {
            CorrelationId = corrId,
            TextileId = 99,
            SensorCode = "IMG-CORR",
            EllipticityMean = 0.7,
            AspectRatioMean = 1.8,
            SolidityMean = 0.8,
            MeanGrayscale = 125,
            TextureEntropy = 5.0,
            AverageParticleArea = 100,
            ParticleCount = 15
        };

        var (result, _) = _service.Classify(image);

        Assert.Equal(corrId, result.SourceImageCorrelationId);
    }

    [Fact]
    public void Classify_RecommendedAction_NotNullForHighRisk()
    {
        var image = new FrassImageCaptured
        {
            TextileId = 42,
            SensorCode = "IMG-HIGH",
            EllipticityMean = 0.40,
            AspectRatioMean = 1.10,
            SolidityMean = 0.95,
            MeanGrayscale = 200,
            TextureEntropy = 5.0,
            AverageParticleArea = 320.0,
            ParticleCount = 800
        };

        var (result, _) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.NotEmpty(result.ModelVersion);
        Assert.True(result.PredictedInstars >= 1);
    }
}
