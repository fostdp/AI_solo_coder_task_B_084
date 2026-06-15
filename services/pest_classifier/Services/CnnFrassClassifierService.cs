using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Data;
using PestClassifier.Service.Models;
using TextileMonitoring.Contracts.Enums;
using TextileMonitoring.Contracts.Messages;

namespace PestClassifier.Service.Services;

public interface ICnnFrassClassifierService
{
    (PestClassificationResult Result, double InferenceLatencyMs) Classify(FrassImageCaptured image);
}

public class CnnFrassClassifierService : ICnnFrassClassifierService
{
    private readonly CnnClassifierConfig _config;
    private readonly Random _random;
    private readonly MLContext _mlContext;
    private readonly ITransformer _trainedModel;
    private readonly PredictionEngine<FrassFeatureData, PestPrediction> _predictionEngine;

    public class FrassFeatureData
    {
        [LoadColumn(0)] public float Ellipticity { get; set; }
        [LoadColumn(1)] public float AspectRatio { get; set; }
        [LoadColumn(2)] public float Solidity { get; set; }
        [LoadColumn(3)] public float MeanGrayscale { get; set; }
        [LoadColumn(4)] public float TextureEntropy { get; set; }
        [LoadColumn(5)] public float AverageParticleArea { get; set; }
        [LoadColumn(6)] public string? Label { get; set; }
    }

    public class PestPrediction
    {
        [ColumnName("PredictedLabel")] public string PredictedSpecies { get; set; } = string.Empty;
        [ColumnName("Score")] public float[] SpeciesScores { get; set; } = Array.Empty<float>();
    }

    private static readonly Dictionary<PestSpecies, PestFeatureProfile> SpeciesProfiles = new()
    {
        {
            PestSpecies.LepismaSaccharina,
            new PestFeatureProfile
            {
                Species = PestSpecies.LepismaSaccharina,
                EllipticityIdeal = 0.82,
                EllipticityWeight = 0.22,
                AspectRatioIdeal = 1.55,
                AspectRatioWeight = 0.10,
                SolidityIdeal = 0.91,
                SolidityWeight = 0.22,
                MeanGrayscaleIdeal = 0.52,
                MeanGrayscaleWeight = 0.18,
                TextureEntropyIdeal = 4.8,
                TextureEntropyWeight = 0.12,
                AverageParticleAreaIdeal = 120.0,
                AverageParticleAreaWeight = 0.16
            }
        },
        {
            PestSpecies.CtenolepismaLongicaudata,
            new PestFeatureProfile
            {
                Species = PestSpecies.CtenolepismaLongicaudata,
                EllipticityIdeal = 0.68,
                EllipticityWeight = 0.12,
                AspectRatioIdeal = 2.45,
                AspectRatioWeight = 0.25,
                SolidityIdeal = 0.62,
                SolidityWeight = 0.22,
                MeanGrayscaleIdeal = 0.42,
                MeanGrayscaleWeight = 0.10,
                TextureEntropyIdeal = 6.9,
                TextureEntropyWeight = 0.24,
                AverageParticleAreaIdeal = 95.0,
                AverageParticleAreaWeight = 0.07
            }
        },
        {
            PestSpecies.AttagenusPellio,
            new PestFeatureProfile
            {
                Species = PestSpecies.AttagenusPellio,
                EllipticityIdeal = 0.42,
                EllipticityWeight = 0.22,
                AspectRatioIdeal = 1.20,
                AspectRatioWeight = 0.08,
                SolidityIdeal = 0.78,
                SolidityWeight = 0.10,
                MeanGrayscaleIdeal = 0.78,
                MeanGrayscaleWeight = 0.24,
                TextureEntropyIdeal = 5.2,
                TextureEntropyWeight = 0.10,
                AverageParticleAreaIdeal = 280.0,
                AverageParticleAreaWeight = 0.26
            }
        },
        {
            PestSpecies.TineolaBisselliella,
            new PestFeatureProfile
            {
                Species = PestSpecies.TineolaBisselliella,
                EllipticityIdeal = 0.58,
                EllipticityWeight = 0.18,
                AspectRatioIdeal = 1.85,
                AspectRatioWeight = 0.12,
                SolidityIdeal = 0.56,
                SolidityWeight = 0.24,
                MeanGrayscaleIdeal = 0.38,
                MeanGrayscaleWeight = 0.14,
                TextureEntropyIdeal = 5.5,
                TextureEntropyWeight = 0.12,
                AverageParticleAreaIdeal = 55.0,
                AverageParticleAreaWeight = 0.20
            }
        },
        {
            PestSpecies.AnthrenusVerbasci,
            new PestFeatureProfile
            {
                Species = PestSpecies.AnthrenusVerbasci,
                EllipticityIdeal = 0.94,
                EllipticityWeight = 0.26,
                AspectRatioIdeal = 1.08,
                AspectRatioWeight = 0.20,
                SolidityIdeal = 0.85,
                SolidityWeight = 0.12,
                MeanGrayscaleIdeal = 0.55,
                MeanGrayscaleWeight = 0.16,
                TextureEntropyIdeal = 5.0,
                TextureEntropyWeight = 0.10,
                AverageParticleAreaIdeal = 160.0,
                AverageParticleAreaWeight = 0.16
            }
        }
    };

    private static readonly Dictionary<PestSpecies, PestFeatureProfile> BaseModelProfiles = new()
    {
        {
            PestSpecies.LepismaSaccharina,
            new PestFeatureProfile
            {
                Species = PestSpecies.LepismaSaccharina,
                EllipticityIdeal = 0.75,
                EllipticityWeight = 0.18,
                AspectRatioIdeal = 1.70,
                AspectRatioWeight = 0.14,
                SolidityIdeal = 0.85,
                SolidityWeight = 0.18,
                MeanGrayscaleIdeal = 0.50,
                MeanGrayscaleWeight = 0.16,
                TextureEntropyIdeal = 5.2,
                TextureEntropyWeight = 0.16,
                AverageParticleAreaIdeal = 110.0,
                AverageParticleAreaWeight = 0.18
            }
        },
        {
            PestSpecies.CtenolepismaLongicaudata,
            new PestFeatureProfile
            {
                Species = PestSpecies.CtenolepismaLongicaudata,
                EllipticityIdeal = 0.65,
                EllipticityWeight = 0.15,
                AspectRatioIdeal = 2.20,
                AspectRatioWeight = 0.20,
                SolidityIdeal = 0.68,
                SolidityWeight = 0.18,
                MeanGrayscaleIdeal = 0.45,
                MeanGrayscaleWeight = 0.13,
                TextureEntropyIdeal = 6.2,
                TextureEntropyWeight = 0.20,
                AverageParticleAreaIdeal = 90.0,
                AverageParticleAreaWeight = 0.14
            }
        },
        {
            PestSpecies.AttagenusPellio,
            new PestFeatureProfile
            {
                Species = PestSpecies.AttagenusPellio,
                EllipticityIdeal = 0.48,
                EllipticityWeight = 0.20,
                AspectRatioIdeal = 1.30,
                AspectRatioWeight = 0.10,
                SolidityIdeal = 0.75,
                SolidityWeight = 0.14,
                MeanGrayscaleIdeal = 0.72,
                MeanGrayscaleWeight = 0.20,
                TextureEntropyIdeal = 5.5,
                TextureEntropyWeight = 0.12,
                AverageParticleAreaIdeal = 250.0,
                AverageParticleAreaWeight = 0.24
            }
        },
        {
            PestSpecies.TineolaBisselliella,
            new PestFeatureProfile
            {
                Species = PestSpecies.TineolaBisselliella,
                EllipticityIdeal = 0.62,
                EllipticityWeight = 0.16,
                AspectRatioIdeal = 1.75,
                AspectRatioWeight = 0.14,
                SolidityIdeal = 0.60,
                SolidityWeight = 0.20,
                MeanGrayscaleIdeal = 0.42,
                MeanGrayscaleWeight = 0.16,
                TextureEntropyIdeal = 5.8,
                TextureEntropyWeight = 0.14,
                AverageParticleAreaIdeal = 60.0,
                AverageParticleAreaWeight = 0.20
            }
        },
        {
            PestSpecies.AnthrenusVerbasci,
            new PestFeatureProfile
            {
                Species = PestSpecies.AnthrenusVerbasci,
                EllipticityIdeal = 0.90,
                EllipticityWeight = 0.22,
                AspectRatioIdeal = 1.15,
                AspectRatioWeight = 0.18,
                SolidityIdeal = 0.82,
                SolidityWeight = 0.15,
                MeanGrayscaleIdeal = 0.52,
                MeanGrayscaleWeight = 0.18,
                TextureEntropyIdeal = 5.3,
                TextureEntropyWeight = 0.12,
                AverageParticleAreaIdeal = 150.0,
                AverageParticleAreaWeight = 0.15
            }
        }
    };

    private readonly Dictionary<PestSpecies, PestFeatureProfile> _fineTunedProfiles;

    public CnnFrassClassifierService(IOptions<CnnClassifierConfig> config)
    {
        _config = config.Value;
        _random = new Random(Guid.NewGuid().GetHashCode());
        _fineTunedProfiles = BuildFineTunedProfiles();
        _mlContext = new MLContext(seed: 42);

        var trainingData = GenerateTrainingData();
        _trainedModel = TrainMulticlassClassifier(trainingData);
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<FrassFeatureData, PestPrediction>(_trainedModel);
    }

    private List<FrassFeatureData> GenerateTrainingData()
    {
        var trainingData = new List<FrassFeatureData>();
        var profiles = _config.EnableTransferLearning ? _fineTunedProfiles : SpeciesProfiles;
        int samplesPerSpecies = 120;

        foreach (var profile in profiles.Values)
        {
            for (int i = 0; i < samplesPerSpecies; i++)
            {
                var noise = _random.NextDouble() * _config.AugmentationNoiseStdDev * 2 - _config.AugmentationNoiseStdDev;
                var jitter = (_random.NextDouble() - 0.5) * 2.0 * _config.FeatureJitterRange;
                double dropout = _random.NextDouble() < _config.DropoutRate ? 0.5 : 1.0;

                double ApplyNoise(double value, double range)
                {
                    return Math.Clamp(value + noise + jitter, value - range, value + range) * dropout;
                }

                var sample = new FrassFeatureData
                {
                    Ellipticity = (float)ApplyNoise(profile.EllipticityIdeal, 0.15),
                    AspectRatio = (float)ApplyNoise(profile.AspectRatioIdeal, 0.4),
                    Solidity = (float)ApplyNoise(profile.SolidityIdeal, 0.15),
                    MeanGrayscale = (float)ApplyNoise(profile.MeanGrayscaleIdeal, 0.15),
                    TextureEntropy = (float)ApplyNoise(profile.TextureEntropyIdeal, 0.8),
                    AverageParticleArea = (float)ApplyNoise(profile.AverageParticleAreaIdeal / 300.0, 0.2),
                    Label = profile.Species.ToString()
                };

                trainingData.Add(sample);
            }
        }

        return trainingData;
    }

    private List<FrassFeatureData> GenerateBaseModelTrainingData()
    {
        var trainingData = new List<FrassFeatureData>();
        int samplesPerSpecies = 80;

        foreach (var profile in BaseModelProfiles.Values)
        {
            for (int i = 0; i < samplesPerSpecies; i++)
            {
                var noise = _random.NextDouble() * 0.1 - 0.05;
                var sample = new FrassFeatureData
                {
                    Ellipticity = (float)Math.Clamp(profile.EllipticityIdeal + noise, 0.01, 0.99),
                    AspectRatio = (float)Math.Clamp(profile.AspectRatioIdeal + noise * 2, 1.0, 4.0),
                    Solidity = (float)Math.Clamp(profile.SolidityIdeal + noise, 0.01, 0.99),
                    MeanGrayscale = (float)Math.Clamp(profile.MeanGrayscaleIdeal + noise, 0.0, 1.0),
                    TextureEntropy = (float)Math.Clamp(profile.TextureEntropyIdeal + noise * 4, 0.0, 10.0),
                    AverageParticleArea = (float)Math.Clamp(profile.AverageParticleAreaIdeal / 300.0 + noise, 0.0, 1.0),
                    Label = profile.Species.ToString()
                };
                trainingData.Add(sample);
            }
        }

        return trainingData;
    }

    private ITransformer TrainMulticlassClassifier(List<FrassFeatureData> trainingData)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label")
            .Append(_mlContext.Transforms.NormalizeMinMax("Features",
                nameof(FrassFeatureData.Ellipticity),
                nameof(FrassFeatureData.AspectRatio),
                nameof(FrassFeatureData.Solidity),
                nameof(FrassFeatureData.MeanGrayscale),
                nameof(FrassFeatureData.TextureEntropy),
                nameof(FrassFeatureData.AverageParticleArea)))
            .Append(_mlContext.Transforms.Concatenate("Features",
                nameof(FrassFeatureData.Ellipticity),
                nameof(FrassFeatureData.AspectRatio),
                nameof(FrassFeatureData.Solidity),
                nameof(FrassFeatureData.MeanGrayscale),
                nameof(FrassFeatureData.TextureEntropy),
                nameof(FrassFeatureData.AverageParticleArea)))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                labelColumnName: "Label",
                featureColumnName: "Features",
                maximumNumberOfIterations: 500,
                l1Regularization: 0.01f,
                l2Regularization: 0.02f))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        var model = pipeline.Fit(dataView);

        return model;
    }

    private Dictionary<PestSpecies, PestFeatureProfile> BuildFineTunedProfiles()
    {
        if (!_config.EnableTransferLearning)
            return new Dictionary<PestSpecies, PestFeatureProfile>(SpeciesProfiles);

        var result = new Dictionary<PestSpecies, PestFeatureProfile>();
        var alpha = _config.FineTuneFactor;

        foreach (var species in SpeciesProfiles.Keys)
        {
            var domain = SpeciesProfiles[species];
            var baseline = BaseModelProfiles[species];

            result[species] = new PestFeatureProfile
            {
                Species = species,
                EllipticityIdeal = Lerp(baseline.EllipticityIdeal, domain.EllipticityIdeal, alpha),
                EllipticityWeight = Lerp(baseline.EllipticityWeight, domain.EllipticityWeight, alpha),
                AspectRatioIdeal = Lerp(baseline.AspectRatioIdeal, domain.AspectRatioIdeal, alpha),
                AspectRatioWeight = Lerp(baseline.AspectRatioWeight, domain.AspectRatioWeight, alpha),
                SolidityIdeal = Lerp(baseline.SolidityIdeal, domain.SolidityIdeal, alpha),
                SolidityWeight = Lerp(baseline.SolidityWeight, domain.SolidityWeight, alpha),
                MeanGrayscaleIdeal = Lerp(baseline.MeanGrayscaleIdeal, domain.MeanGrayscaleIdeal, alpha),
                MeanGrayscaleWeight = Lerp(baseline.MeanGrayscaleWeight, domain.MeanGrayscaleWeight, alpha),
                TextureEntropyIdeal = Lerp(baseline.TextureEntropyIdeal, domain.TextureEntropyIdeal, alpha),
                TextureEntropyWeight = Lerp(baseline.TextureEntropyWeight, domain.TextureEntropyWeight, alpha),
                AverageParticleAreaIdeal = Lerp(baseline.AverageParticleAreaIdeal, domain.AverageParticleAreaIdeal, alpha),
                AverageParticleAreaWeight = Lerp(baseline.AverageParticleAreaWeight, domain.AverageParticleAreaWeight, alpha)
            };
        }

        return result;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public virtual (PestClassificationResult Result, double InferenceLatencyMs) Classify(FrassImageCaptured image)
    {
        var latency = _random.Next(_config.MinInferenceLatencyMs, _config.MaxInferenceLatencyMs + 1);
        Thread.Sleep(latency);

        Dictionary<PestSpecies, double> probabilities;

        if (_config.EnableDataAugmentation && _config.TtaAugmentationCount > 1)
        {
            probabilities = ClassifyWithTta(image);
        }
        else
        {
            var normalizedFeatures = NormalizeFeatures(image);
            probabilities = ClassifyWithMLNET(normalizedFeatures);
        }

        probabilities = ApplyDecisionTreeNoise(probabilities);

        var ordered = probabilities.OrderByDescending(kv => kv.Value).ToList();
        var topSpecies = ordered.First().Key;
        var confidence = ordered.First().Value;
        var secondConfidence = ordered.Count > 1 ? ordered[1].Value : 0.0;
        var margin = confidence - secondConfidence;

        var instars = _random.Next(_config.MinInstars, _config.MaxInstars + 1);
        var estimatedPopulation = (int)Math.Round(
            (image.ParticleCount + 1) * _config.BasePopulationPerParticle * instars);

        var riskScore = CalculateRiskScore(confidence, image, instars);

        return (new PestClassificationResult
        {
            PredictedSpecies = topSpecies,
            Confidence = confidence,
            SpeciesProbabilities = probabilities,
            EstimatedLarvalInstars = instars,
            EstimatedPopulation = estimatedPopulation,
            RiskSeverityScore = riskScore,
            ProbabilityMargin = margin,
            InferenceLatencyMs = latency,
            ModelVersion = _config.ModelVersion
        }, latency);
    }

    private Dictionary<PestSpecies, double> ClassifyWithTta(FrassImageCaptured image)
    {
        var aggregated = new Dictionary<PestSpecies, double>();
        foreach (var species in _fineTunedProfiles.Keys)
            aggregated[species] = 0.0;

        var baseFeatures = NormalizeFeatures(image);

        for (int i = 0; i < _config.TtaAugmentationCount; i++)
        {
            var augmented = AugmentFeatures(baseFeatures);
            var probs = ClassifyWithMLNET(augmented);

            foreach (var kvp in probs)
                aggregated[kvp.Key] += kvp.Value;
        }

        var sum = aggregated.Values.Sum();
        return aggregated.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum);
    }

    private Dictionary<PestSpecies, double> ClassifyWithMLNET(NormalizedFeatureVector features)
    {
        var input = new FrassFeatureData
        {
            Ellipticity = (float)features.Ellipticity,
            AspectRatio = (float)features.AspectRatio,
            Solidity = (float)features.Solidity,
            MeanGrayscale = (float)features.MeanGrayscale,
            TextureEntropy = (float)features.TextureEntropy,
            AverageParticleArea = (float)features.AverageParticleArea
        };

        var prediction = _predictionEngine.Predict(input);

        var probabilities = new Dictionary<PestSpecies, double>();
        var speciesNames = Enum.GetNames<PestSpecies>();

        for (int i = 0; i < speciesNames.Length; i++)
        {
            if (Enum.TryParse<PestSpecies>(speciesNames[i], out var species)
                && i < prediction.SpeciesScores.Length)
            {
                probabilities[species] = Softmax(prediction.SpeciesScores)[i];
            }
        }

        if (probabilities.Count == 0)
        {
            var fallback = CalculateWeightedDistances(features);
            probabilities = SoftmaxDistances(fallback);
        }

        return probabilities;
    }

    private static double[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exp = logits.Select(x => Math.Exp(x - max)).ToArray();
        var sum = exp.Sum();
        return exp.Select(x => x / sum).ToArray();
    }

    private NormalizedFeatureVector AugmentFeatures(NormalizedFeatureVector original)
    {
        double jitter = _config.FeatureJitterRange;
        double noiseStd = _config.AugmentationNoiseStdDev;
        double dropoutRate = _config.DropoutRate;

        double GaussianNoise()
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2) * noiseStd;
        }

        double Dropout(double value)
        {
            if (_random.NextDouble() < dropoutRate)
                return 0.5;
            return value;
        }

        double Jitter(double value)
        {
            var shift = (_random.NextDouble() - 0.5) * 2.0 * jitter;
            return Math.Clamp(value + shift + GaussianNoise(), 0.0, 1.0);
        }

        return new NormalizedFeatureVector(
            Ellipticity: Dropout(Jitter(original.Ellipticity)),
            AspectRatio: Dropout(Jitter(original.AspectRatio)),
            Solidity: Dropout(Jitter(original.Solidity)),
            MeanGrayscale: Dropout(Jitter(original.MeanGrayscale)),
            TextureEntropy: Dropout(Jitter(original.TextureEntropy)),
            AverageParticleArea: Dropout(Jitter(original.AverageParticleArea)));
    }

    private NormalizedFeatureVector NormalizeFeatures(FrassImageCaptured image)
    {
        double ClampNorm(double value, double min, double max)
        {
            var clamped = Math.Clamp(value, min, max);
            return (clamped - min) / (max - min);
        }

        var ellipticity = ClampNorm(image.EllipticityMean, 0.0, 1.0);
        var aspectRatio = ClampNorm(image.AspectRatioMean, 1.0, 4.0);
        var solidity = ClampNorm(image.SolidityMean, 0.0, 1.0);
        var meanGrayscale = ClampNorm(image.MeanGrayscale, 0.0, 255.0);
        var textureEntropy = ClampNorm(image.TextureEntropy, 0.0, 10.0);
        var particleArea = ClampNorm(image.AverageParticleArea, 0.0, 500.0);

        return new NormalizedFeatureVector(
            ellipticity,
            aspectRatio,
            solidity,
            meanGrayscale,
            textureEntropy,
            particleArea);
    }

    private Dictionary<PestSpecies, double> CalculateWeightedDistances(NormalizedFeatureVector features)
    {
        var distances = new Dictionary<PestSpecies, double>();
        var profiles = _config.EnableTransferLearning ? _fineTunedProfiles : SpeciesProfiles;

        foreach (var profile in profiles.Values)
        {
            var dEllipticity = Math.Pow(features.Ellipticity - profile.EllipticityIdeal, 2) * profile.EllipticityWeight;
            var dAspect = Math.Pow(features.AspectRatio - (profile.AspectRatioIdeal - 1.0) / 3.0, 2) * profile.AspectRatioWeight;
            var dSolidity = Math.Pow(features.Solidity - profile.SolidityIdeal, 2) * profile.SolidityWeight;
            var dGrayscale = Math.Pow(features.MeanGrayscale - profile.MeanGrayscaleIdeal, 2) * profile.MeanGrayscaleWeight;
            var dEntropy = Math.Pow(features.TextureEntropy - profile.TextureEntropyIdeal / 10.0, 2) * profile.TextureEntropyWeight;
            var dArea = Math.Pow(features.AverageParticleArea - profile.AverageParticleAreaIdeal / 500.0, 2) * profile.AverageParticleAreaWeight;

            distances[profile.Species] = Math.Sqrt(dEllipticity + dAspect + dSolidity + dGrayscale + dEntropy + dArea);
        }

        return distances;
    }

    private Dictionary<PestSpecies, double> SoftmaxDistances(Dictionary<PestSpecies, double> distances)
    {
        var tau = 0.1 / _config.TemperatureScale;
        var maxDist = distances.Values.Max();
        var expNegDist = distances.ToDictionary(
            kvp => kvp.Key,
            kvp => Math.Exp(-(kvp.Value - maxDist) * tau));

        var sum = expNegDist.Values.Sum();
        return expNegDist.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum);
    }

    private Dictionary<PestSpecies, double> ApplyDecisionTreeNoise(Dictionary<PestSpecies, double> probabilities)
    {
        var noiseLevel = _config.NoiseLevel;
        var result = new Dictionary<PestSpecies, double>();

        foreach (var kvp in probabilities)
        {
            var noise = (_random.NextDouble() - 0.5) * 2.0 * noiseLevel;
            result[kvp.Key] = Math.Clamp(kvp.Value + noise, 0.0, 1.0);
        }

        var sum = result.Values.Sum();
        if (sum > 0)
        {
            return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum);
        }

        return probabilities;
    }

    private int CalculateRiskScore(double confidence, FrassImageCaptured image, int instars)
    {
        var particleScore = Math.Min(image.ParticleCount / 100.0, 1.0) * 40;
        var confidenceScore = confidence * 30;
        var instarScore = (instars / 5.0) * 20;
        var areaScore = Math.Min(image.AverageParticleArea / 300.0, 1.0) * 10;

        var total = particleScore + confidenceScore + instarScore + areaScore;
        return (int)Math.Clamp(Math.Round(total), 1, 100);
    }
}

public record NormalizedFeatureVector(
    double Ellipticity,
    double AspectRatio,
    double Solidity,
    double MeanGrayscale,
    double TextureEntropy,
    double AverageParticleArea);
