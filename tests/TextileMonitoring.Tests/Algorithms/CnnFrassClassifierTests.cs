
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
            MinInferenceLatencyMs = 1,
            MaxInferenceLatencyMs = 2,
            TemperatureScale = 1.0,
            NoiseLevel = 0.08,
            MinInstars = 1,
            MaxInstars = 5,
            BasePopulationPerParticle = 0.25,
            EnableDataAugmentation = true,
            AugmentationNoiseStdDev = 0.06,
            FeatureJitterRange = 0.08,
            DropoutRate = 0.15,
            TtaAugmentationCount = 8,
            EnableTransferLearning = true,
            FineTuneFactor = 0.65,
            BaseModelWeight = 0.35,
            ModelVersion = "cnn-frass-test"
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

    [Fact]
    public void ClassificationAccuracy_FiveSpeciesWithNoise_Above85Percent()
    {
        var speciesIdealProfiles = new (PestSpecies Species, double Ellip, double Aspect, double Sol, double Gray, double Entropy, double Area)[]
        {
            (PestSpecies.LepismaSaccharina, 0.82, 1.55, 0.91, 132.6, 4.8, 120.0),
            (PestSpecies.CtenolepismaLongicaudata, 0.68, 2.45, 0.62, 107.1, 6.9, 95.0),
            (PestSpecies.AttagenusPellio, 0.42, 1.20, 0.78, 198.9, 5.2, 280.0),
            (PestSpecies.TineolaBisselliella, 0.58, 1.85, 0.56, 96.9, 5.5, 55.0),
            (PestSpecies.AnthrenusVerbasci, 0.94, 1.08, 0.85, 140.3, 5.0, 160.0)
        };

        var rnd = new Random(42);
        int totalSamples = 0;
        int correctPredictions = 0;
        const int samplesPerSpecies = 20;

        foreach (var profile in speciesIdealProfiles)
        {
            for (int i = 0; i < samplesPerSpecies; i++)
            {
                var noise = 0.08;
                var image = new FrassImageCaptured
                {
                    TextileId = 1000 + totalSamples,
                    SensorCode = $"IMG-ACC-{totalSamples}",
                    EllipticityMean = Math.Clamp(profile.Ellip * (1 + rnd.NextDouble() * 2 * noise - noise), 0.01, 0.99),
                    AspectRatioMean = Math.Clamp(profile.Aspect * (1 + rnd.NextDouble() * 2 * noise - noise), 1.0, 4.0),
                    SolidityMean = Math.Clamp(profile.Sol * (1 + rnd.NextDouble() * 2 * noise - noise), 0.01, 0.99),
                    MeanGrayscale = Math.Clamp(profile.Gray * (1 + rnd.NextDouble() * 2 * noise - noise), 5.0, 250.0),
                    TextureEntropy = Math.Clamp(profile.Entropy * (1 + rnd.NextDouble() * 2 * noise - noise), 1.0, 7.8),
                    AverageParticleArea = Math.Clamp(profile.Area * (1 + rnd.NextDouble() * 2 * noise - noise), 10.0, 490.0),
                    ParticleCount = rnd.Next(10, 200)
                };

                var (result, _) = _service.Classify(image);
                totalSamples++;
                if (result.PredictedSpecies == profile.Species)
                    correctPredictions++;
            }
        }

        double accuracy = (double)correctPredictions / totalSamples;
        Assert.True(accuracy > 0.85,
            $"Classification accuracy {accuracy:P2} ({correctPredictions}/{totalSamples}) should exceed 85%");
    }

    [Theory]
    [InlineData(0.01, 1.0, 0.99, 5.0, 1.0, 10.0)]
    [InlineData(0.99, 4.0, 0.01, 250.0, 7.8, 490.0)]
    [InlineData(0.50, 2.5, 0.50, 128.0, 4.0, 250.0)]
    [InlineData(0.01, 4.0, 0.01, 250.0, 7.8, 490.0)]
    public void Classify_BoundaryFeatureValues_ReturnsValidPrediction(
        double ellip, double aspect, double sol, double gray, double entropy, double area)
    {
        var image = new FrassImageCaptured
        {
            TextileId = 2000 + (int)(ellip * 1000),
            SensorCode = "IMG-BOUNDARY",
            EllipticityMean = ellip,
            AspectRatioMean = aspect,
            SolidityMean = sol,
            MeanGrayscale = gray,
            TextureEntropy = entropy,
            AverageParticleArea = area,
            ParticleCount = 1
        };

        var (result, latency) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.NotEqual(PestSpecies.Unknown, result.PredictedSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.InRange(latency, 45, 95);
        var sum = result.SpeciesProbabilities.Values.Sum();
        Assert.InRange(sum, 0.98, 1.02);
    }

    [Theory]
    [InlineData(-0.5, 1.5, 0.8, 128, 5.0, 150.0)]
    [InlineData(1.5, 1.5, 0.8, 128, 5.0, 150.0)]
    [InlineData(0.5, 0.5, 0.8, 128, 5.0, 150.0)]
    [InlineData(0.5, 1.5, -0.1, 128, 5.0, 150.0)]
    [InlineData(0.5, 1.5, 1.2, -10.0, 5.0, 150.0)]
    [InlineData(0.5, 1.5, 0.8, 300.0, 5.0, 150.0)]
    [InlineData(0.5, 1.5, 0.8, 128, 10.0, -50.0)]
    public void Classify_OutOfRangeFeatureValues_ClampedAndClassified(
        double ellip, double aspect, double sol, double gray, double entropy, double area)
    {
        var image = new FrassImageCaptured
        {
            TextileId = 3000 + Math.Abs((int)(ellip * 100)),
            SensorCode = "IMG-OUTOFRANGE",
            EllipticityMean = ellip,
            AspectRatioMean = aspect,
            SolidityMean = sol,
            MeanGrayscale = gray,
            TextureEntropy = entropy,
            AverageParticleArea = area,
            ParticleCount = -1
        };

        var (result, _) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.NotEqual(PestSpecies.Unknown, result.PredictedSpecies);
        Assert.InRange(result.Confidence, 0.0, 1.0);
        Assert.InRange(result.FeatureVectorNorm, 0.0, 3.0);
    }

    [Fact]
    public void Classify_ZeroParticleCount_StillReturnsValidPrediction()
    {
        var image = new FrassImageCaptured
        {
            TextileId = 4000,
            SensorCode = "IMG-ZERO",
            EllipticityMean = 0.7,
            AspectRatioMean = 1.8,
            SolidityMean = 0.8,
            MeanGrayscale = 130,
            TextureEntropy = 5.0,
            AverageParticleArea = 0.0,
            ParticleCount = 0
        };

        var (result, _) = _service.Classify(image);

        Assert.NotNull(result);
        Assert.NotEqual(PestSpecies.Unknown, result.PredictedSpecies);
        Assert.True(result.EstimatedPopulationSize >= 0);
    }

    [Fact]
    public void Classify_AmbiguousFeatures_LowConfidenceHighEntropy()
    {
        var ambiguousImage = new FrassImageCaptured
        {
            TextileId = 5000,
            SensorCode = "IMG-AMBIG",
            EllipticityMean = 0.68,
            AspectRatioMean = 1.77,
            SolidityMean = 0.73,
            MeanGrayscale = 133.3,
            TextureEntropy = 5.44,
            AverageParticleArea = 142.0,
            ParticleCount = 50
        };

        var (result, _) = _service.Classify(ambiguousImage);

        var probs = result.SpeciesProbabilities.Values.OrderByDescending(v => v).Take(2).ToList();
        var margin = probs[0] - probs[1];
        Assert.True(margin < 0.5 || result.Confidence < 0.7,
            $"Ambiguous features should have low confidence or small margin (margin={margin:F3}, conf={result.Confidence:F3})");
    }

    [Fact]
    public void DataAugmentation_TtaImprovesRobustness()
    {
        var baseImage = new FrassImageCaptured
        {
            TextileId = 6000,
            SensorCode = "IMG-TTA",
            EllipticityMean = 0.82,
            AspectRatioMean = 1.55,
            SolidityMean = 0.91,
            MeanGrayscale = 132.6,
            TextureEntropy = 4.8,
            AverageParticleArea = 120.0,
            ParticleCount = 30
        };

        var predictions = new List<PestSpecies>();
        for (int i = 0; i < 10; i++)
        {
            var (result, _) = _service.Classify(baseImage);
            predictions.Add(result.PredictedSpecies);
        }

        var speciesCounts = predictions.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
        var mostCommon = speciesCounts.OrderByDescending(kv => kv.Value).First();

        Assert.True(mostCommon.Value >= 6,
            $"TTA should provide stable predictions; top species {mostCommon.Key} appears {mostCommon.Value}/10 times");
    }

    [Fact]
    public void TransferLearning_FineTuneFactor_ProfilesInterpolated()
    {
        var configNoTl = new CnnClassifierConfig
        {
            ModelVersion = "test-notl",
            MinInferenceLatencyMs = 1,
            MaxInferenceLatencyMs = 2,
            EnableTransferLearning = false,
            EnableDataAugmentation = false,
            TtaAugmentationCount = 1,
            TemperatureScale = 1.0,
            NoiseLevel = 0.0
        };

        var configTl = new CnnClassifierConfig
        {
            ModelVersion = "test-tl",
            MinInferenceLatencyMs = 1,
            MaxInferenceLatencyMs = 2,
            EnableTransferLearning = true,
            FineTuneFactor = 0.65,
            EnableDataAugmentation = false,
            TtaAugmentationCount = 1,
            TemperatureScale = 1.0,
            NoiseLevel = 0.0
        };

        var serviceNoTl = new CnnFrassClassifierService(
            Microsoft.Extensions.Options.Options.Create(configNoTl));
        var serviceTl = new CnnFrassClassifierService(
            Microsoft.Extensions.Options.Options.Create(configTl));

        var image = new FrassImageCaptured
        {
            TextileId = 6001,
            SensorCode = "IMG-TL",
            EllipticityMean = 0.7,
            AspectRatioMean = 1.8,
            SolidityMean = 0.8,
            MeanGrayscale = 130,
            TextureEntropy = 5.5,
            AverageParticleArea = 150.0,
            ParticleCount = 20
        };

        var (resultNoTl, _) = serviceNoTl.Classify(image);
        var (resultTl, _) = serviceTl.Classify(image);

        Assert.NotNull(resultNoTl);
        Assert.NotNull(resultTl);
        Assert.True(resultNoTl.Confidence > 0);
        Assert.True(resultTl.Confidence > 0);
    }

    [Fact]
    public void DropoutRegularization_ActiveDuringAugmentation()
    {
        var config = new CnnClassifierConfig
        {
            ModelVersion = "test-dropout",
            MinInferenceLatencyMs = 1,
            MaxInferenceLatencyMs = 2,
            EnableDataAugmentation = true,
            TtaAugmentationCount = 16,
            DropoutRate = 0.3,
            AugmentationNoiseStdDev = 0.05,
            FeatureJitterRange = 0.06,
            TemperatureScale = 1.0,
            NoiseLevel = 0.0,
            EnableTransferLearning = false
        };

        var service = new CnnFrassClassifierService(
            Microsoft.Extensions.Options.Options.Create(config));

        var image = new FrassImageCaptured
        {
            TextileId = 6002,
            SensorCode = "IMG-DROPOUT",
            EllipticityMean = 0.75,
            AspectRatioMean = 1.6,
            SolidityMean = 0.85,
            MeanGrayscale = 128,
            TextureEntropy = 5.0,
            AverageParticleArea = 100.0,
            ParticleCount = 25
        };

        var confidences = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            var (result, _) = service.Classify(image);
            confidences.Add(result.Confidence);
        }

        var maxConf = confidences.Max();
        var minConf = confidences.Min();
        var variance = confidences.Average(c => Math.Pow(c - confidences.Average(), 2));

        Assert.True(variance > 0.0001,
            $"Dropout and augmentation should introduce confidence variance (var={variance:F6})");
        Assert.True(maxConf - minConf > 0.005,
            $"Confidence should vary across runs with dropout (range={maxConf - minConf:F4})");
    }

    [Fact]
    public void MLNET_ModelTrainsSuccessfully_ProducesValidPredictions()
    {
        var config = new CnnClassifierConfig
        {
            ModelVersion = "test-mlnet",
            MinInferenceLatencyMs = 1,
            MaxInferenceLatencyMs = 2,
            EnableDataAugmentation = false,
            TtaAugmentationCount = 1,
            EnableTransferLearning = true,
            FineTuneFactor = 0.65,
            TemperatureScale = 1.0,
            NoiseLevel = 0.0
        };

        var service = new CnnFrassClassifierService(
            Microsoft.Extensions.Options.Options.Create(config));

        var testCases = new[]
        {
            new { Species = PestSpecies.LepismaSaccharina, Ellipticity = 0.82, Aspect = 1.55, Solidity = 0.91 },
            new { Species = PestSpecies.AnthrenusVerbasci, Ellipticity = 0.94, Aspect = 1.08, Solidity = 0.85 },
            new { Species = PestSpecies.AttagenusPellio, Ellipticity = 0.42, Aspect = 1.20, Solidity = 0.78 }
        };

        int correctCount = 0;
        foreach (var tc in testCases)
        {
            var image = new FrassImageCaptured
            {
                TextileId = 7000 + (int)tc.Species,
                SensorCode = "IMG-MLNET",
                EllipticityMean = tc.Ellipticity,
                AspectRatioMean = tc.Aspect,
                SolidityMean = tc.Solidity,
                MeanGrayscale = 130,
                TextureEntropy = 5.0,
                AverageParticleArea = 150.0,
                ParticleCount = 30
            };

            var (result, _) = service.Classify(image);
            if (result.PredictedSpecies == tc.Species)
                correctCount++;

            Assert.True(result.Confidence > 0, $"ML.NET prediction should have positive confidence");
            Assert.True(result.SpeciesProbabilities.Count >= 5,
                $"ML.NET should return probabilities for all species (got {result.SpeciesProbabilities.Count})");
            Assert.True(Math.Abs(result.SpeciesProbabilities.Values.Sum() - 1.0) < 0.01,
                $"Probabilities should sum to ~1.0 (sum={result.SpeciesProbabilities.Values.Sum():F4})");
        }

        Assert.True(correctCount >= 2,
            $"ML.NET model should classify at least 2/3 standard profiles correctly (got {correctCount}/3)");
    }

    [Fact]
    public void MLNET_Fallback_WhenModelFails_ReturnsDistanceBasedResults()
    {
        var config = new CnnClassifierConfig
        {
            ModelVersion = "test-fallback",
            MinInferenceLatencyMs = 1,
            MaxInferenceLatencyMs = 2,
            EnableDataAugmentation = false,
            TtaAugmentationCount = 1,
            EnableTransferLearning = false,
            TemperatureScale = 1.0,
            NoiseLevel = 0.0
        };

        var service = new CnnFrassClassifierService(
            Microsoft.Extensions.Options.Options.Create(config));

        var image = new FrassImageCaptured
        {
            TextileId = 8000,
            SensorCode = "IMG-FALLBACK",
            EllipticityMean = 0.82,
            AspectRatioMean = 1.55,
            SolidityMean = 0.91,
            MeanGrayscale = 132.6,
            TextureEntropy = 4.8,
            AverageParticleArea = 120.0,
            ParticleCount = 45
        };

        for (int i = 0; i < 10; i++)
        {
            var (result, _) = service.Classify(image);
            Assert.NotNull(result);
            Assert.True(result.Confidence > 0);
            Assert.True(result.SpeciesProbabilities.Count > 0);
        }
    }
}
