using MathNet.Numerics.Distributions;
using Microsoft.Extensions.Options;
using PestClassifier.Service.Models;
using TextileMonitoring.Contracts.Messages;

namespace PestClassifier.Service.Services;

public class CnnFrassClassifierService
{
    private readonly CnnClassifierConfig _config;
    private readonly Random _random;

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

    public CnnFrassClassifierService(IOptions<CnnClassifierConfig> config)
    {
        _config = config.Value;
        _random = new Random(Guid.NewGuid().GetHashCode());
    }

    public virtual (PestClassificationResult Result, double InferenceLatencyMs) Classify(FrassImageCaptured image)
    {
        var latency = _random.Next(_config.MinInferenceLatencyMs, _config.MaxInferenceLatencyMs + 1);
        Thread.Sleep(latency);

        var normalizedFeatures = NormalizeFeatures(image);
        var distances = CalculateWeightedDistances(normalizedFeatures);
        var probabilities = SoftmaxDistances(distances);

        probabilities = ApplyDecisionTreeNoise(probabilities);

        var topSpecies = probabilities
            .OrderByDescending(kvp => kvp.Value)
            .First();

        var featureNorm = CalculateFeatureVectorNorm(image);
        var predictedInstars = EstimateInstars(image, topSpecies.Key);
        var estimatedPopulation = EstimatePopulation(image, probabilities);
        var riskScore = CalculateRiskSeverity(probabilities, estimatedPopulation);
        var recommendedAction = GetRecommendedAction(riskScore, topSpecies.Key);

        var result = new PestClassificationResult
        {
            CorrelationId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            SourceImageCorrelationId = image.CorrelationId,
            TextileId = image.TextileId,
            SensorCode = image.SensorCode,
            PredictedSpecies = topSpecies.Key,
            Confidence = Math.Round(topSpecies.Value, 6),
            SpeciesProbabilities = probabilities.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value, 6)),
            ModelVersion = _config.ModelVersion,
            InferenceLatencyMs = latency,
            FeatureVectorNorm = Math.Round(featureNorm, 6),
            PredictedInstars = predictedInstars,
            EstimatedPopulationSize = Math.Round(estimatedPopulation, 2),
            RiskSeverityScore = riskScore,
            RecommendedAction = recommendedAction
        };

        return (result, latency);
    }

    private static NormalizedFeatureVector NormalizeFeatures(FrassImageCaptured img)
    {
        var ellipticity = Math.Clamp(img.EllipticityMean, 0.0, 1.0);
        var aspectRatio = Math.Clamp((img.AspectRatioMean - 1.0) / 3.0, 0.0, 1.0);
        var solidity = Math.Clamp(img.SolidityMean, 0.0, 1.0);
        var meanGrayscale = Math.Clamp(img.MeanGrayscale / 255.0, 0.0, 1.0);
        var textureEntropy = Math.Clamp(img.TextureEntropy / 8.0, 0.0, 1.0);
        var avgParticleArea = Math.Clamp((img.AverageParticleArea - 10.0) / 490.0, 0.0, 1.0);

        return new NormalizedFeatureVector(
            Ellipticity: ellipticity,
            AspectRatio: aspectRatio,
            Solidity: solidity,
            MeanGrayscale: meanGrayscale,
            TextureEntropy: textureEntropy,
            AverageParticleArea: avgParticleArea);
    }

    private Dictionary<PestSpecies, double> CalculateWeightedDistances(NormalizedFeatureVector features)
    {
        var distances = new Dictionary<PestSpecies, double>();

        foreach (var profile in SpeciesProfiles.Values)
        {
            var normEllipticity = Math.Clamp((profile.EllipticityIdeal - 0.1) / 0.9, 0.0, 1.0);
            var normAspectRatio = Math.Clamp((profile.AspectRatioIdeal - 1.0) / 3.0, 0.0, 1.0);
            var normSolidity = Math.Clamp((profile.SolidityIdeal - 0.1) / 0.9, 0.0, 1.0);
            var normGrayscale = Math.Clamp((profile.MeanGrayscaleIdeal - 0.1) / 0.9, 0.0, 1.0);
            var normEntropy = Math.Clamp((profile.TextureEntropyIdeal - 2.0) / 6.0, 0.0, 1.0);
            var normArea = Math.Clamp((profile.AverageParticleAreaIdeal - 10.0) / 490.0, 0.0, 1.0);

            double dEllipticity = Math.Pow(features.Ellipticity - normEllipticity, 2) * profile.EllipticityWeight;
            double dAspectRatio = Math.Pow(features.AspectRatio - normAspectRatio, 2) * profile.AspectRatioWeight;
            double dSolidity = Math.Pow(features.Solidity - normSolidity, 2) * profile.SolidityWeight;
            double dGrayscale = Math.Pow(features.MeanGrayscale - normGrayscale, 2) * profile.MeanGrayscaleWeight;
            double dEntropy = Math.Pow(features.TextureEntropy - normEntropy, 2) * profile.TextureEntropyWeight;
            double dArea = Math.Pow(features.AverageParticleArea - normArea, 2) * profile.AverageParticleAreaWeight;

            distances[profile.Species] = Math.Sqrt(dEllipticity + dAspectRatio + dSolidity + dGrayscale + dEntropy + dArea);
        }

        return distances;
    }

    private Dictionary<PestSpecies, double> SoftmaxDistances(Dictionary<PestSpecies, double> distances)
    {
        var maxDist = distances.Values.Max();
        var scaledScores = distances.ToDictionary(
            kvp => kvp.Key,
            kvp => -(kvp.Value - maxDist) / _config.TemperatureScale);

        var maxScore = scaledScores.Values.Max();
        var expScores = scaledScores.ToDictionary(
            kvp => kvp.Key,
            kvp => Math.Exp(kvp.Value - maxScore));

        var sumExp = expScores.Values.Sum();

        return expScores.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value / sumExp);
    }

    private Dictionary<PestSpecies, double> ApplyDecisionTreeNoise(Dictionary<PestSpecies, double> probabilities)
    {
        var result = new Dictionary<PestSpecies, double>();
        var noiseVector = new double[probabilities.Count];

        for (int i = 0; i < noiseVector.Length; i++)
        {
            noiseVector[i] = Normal.Sample(0, _config.NoiseLevel);
        }

        var idx = 0;
        foreach (var kvp in probabilities)
        {
            result[kvp.Key] = Math.Max(0, kvp.Value + noiseVector[idx]);
            idx++;
        }

        var sum = result.Values.Sum();
        if (sum > 0)
        {
            result = result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / sum);
        }

        return result;
    }

    private static double CalculateFeatureVectorNorm(FrassImageCaptured image)
    {
        double sumSq =
            Math.Pow(image.EllipticityMean, 2) +
            Math.Pow(image.AspectRatioMean / 3.0, 2) +
            Math.Pow(image.SolidityMean, 2) +
            Math.Pow(image.MeanGrayscale / 255.0, 2) +
            Math.Pow(image.TextureEntropy / 8.0, 2) +
            Math.Pow(image.AverageParticleArea / 500.0, 2);

        return Math.Sqrt(sumSq);
    }

    private int EstimateInstars(FrassImageCaptured image, PestSpecies topSpecies)
    {
        double areaScore = Math.Clamp(image.AverageParticleArea / 300.0, 0.0, 1.0);
        double densityScore = Math.Clamp(image.FrassDensityCorrelated / 5.0, 0.0, 1.0);
        double grayscaleShift = 0.0;

        switch (topSpecies)
        {
            case PestSpecies.AttagenusPellio:
                grayscaleShift = image.MeanGrayscale / 255.0 * 0.3;
                break;
            case PestSpecies.TineolaBisselliella:
                grayscaleShift = (1.0 - image.SolidityMean) * 0.25;
                break;
            case PestSpecies.LepismaSaccharina:
                grayscaleShift = image.EllipticityMean * 0.2;
                break;
            case PestSpecies.CtenolepismaLongicaudata:
                grayscaleShift = image.TextureEntropy / 8.0 * 0.25;
                break;
            case PestSpecies.AnthrenusVerbasci:
                grayscaleShift = (1.0 - image.AspectRatioMean / 3.0) * 0.2;
                break;
        }

        double combined = 0.5 * areaScore + 0.3 * densityScore + 0.2 * grayscaleShift;
        double instarFloat = _config.MinInstars + combined * (_config.MaxInstars - _config.MinInstars);

        var jitter = Normal.Sample(0, 0.4);
        int instar = (int)Math.Round(instarFloat + jitter);

        return Math.Clamp(instar, _config.MinInstars, _config.MaxInstars);
    }

    private double EstimatePopulation(FrassImageCaptured image, Dictionary<PestSpecies, double> probabilities)
    {
        double topProb = probabilities.Values.Max();
        double particleFactor = image.ParticleCount * _config.BasePopulationPerParticle;
        double densityFactor = Math.Pow(Math.Clamp(image.FrassDensityCorrelated, 0.1, 10.0), 1.15);
        double areaFactor = Math.Pow(Math.Clamp(image.AverageParticleArea / 100.0, 0.1, 5.0), 0.7);
        double confidenceFactor = 0.4 + 0.6 * topProb;

        double basePopulation = particleFactor * densityFactor * areaFactor * confidenceFactor;

        double jitter = Normal.Sample(0, basePopulation * 0.12);
        double finalPopulation = Math.Max(1.0, basePopulation + jitter);

        return Math.Round(finalPopulation, 2);
    }

    private static int CalculateRiskSeverity(Dictionary<PestSpecies, double> probabilities, double estimatedPopulation)
    {
        var top = probabilities.OrderByDescending(kvp => kvp.Value).First();
        double speciesRisk = top.Key switch
        {
            PestSpecies.TineolaBisselliella => 1.35,
            PestSpecies.AttagenusPellio => 1.25,
            PestSpecies.AnthrenusVerbasci => 1.15,
            PestSpecies.LepismaSaccharina => 1.00,
            PestSpecies.CtenolepismaLongicaudata => 0.95,
            _ => 1.0
        };

        double populationScore = estimatedPopulation switch
        {
            < 5 => 1,
            < 20 => 2,
            < 50 => 3,
            < 100 => 4,
            _ => 5
        };

        double confidenceScore = top.Value switch
        {
            < 0.4 => 1,
            < 0.6 => 2,
            < 0.75 => 3,
            < 0.9 => 4,
            _ => 5
        };

        double combined = (populationScore * 0.5 + confidenceScore * 0.2 + 3 * 0.3) * speciesRisk;
        return (int)Math.Clamp(Math.Round(combined), 1, 5);
    }

    private static string GetRecommendedAction(int riskScore, PestSpecies species)
    {
        var baseAction = riskScore switch
        {
            1 => "常规监测，无需额外处理",
            2 => "加强监测频率，建议2周内复检",
            3 => "建议局部低温处理或靶向氮气熏蒸",
            4 => "立即启动全面氮气熏蒸处理程序",
            5 => "紧急隔离文物并启动全面除虫方案",
            _ => "持续监测"
        };

        string? speciesNote = species switch
        {
            PestSpecies.TineolaBisselliella => "；注意检查羊绒丝绸类织物",
            PestSpecies.AttagenusPellio => "；检查动物毛皮及羽毛制品",
            PestSpecies.AnthrenusVerbasci => "；检查标本及混合纤维区域",
            PestSpecies.LepismaSaccharina => "；重点关注潮湿区域纤维素织物",
            PestSpecies.CtenolepismaLongicaudata => "；关注高温区域及淀粉类上浆织物",
            _ => null
        };

        return baseAction + speciesNote;
    }

    private sealed class PestFeatureProfile
    {
        public PestSpecies Species { get; init; }
        public double EllipticityIdeal { get; init; }
        public double EllipticityWeight { get; init; }
        public double AspectRatioIdeal { get; init; }
        public double AspectRatioWeight { get; init; }
        public double SolidityIdeal { get; init; }
        public double SolidityWeight { get; init; }
        public double MeanGrayscaleIdeal { get; init; }
        public double MeanGrayscaleWeight { get; init; }
        public double TextureEntropyIdeal { get; init; }
        public double TextureEntropyWeight { get; init; }
        public double AverageParticleAreaIdeal { get; init; }
        public double AverageParticleAreaWeight { get; init; }
    }

    private sealed record NormalizedFeatureVector(
        double Ellipticity,
        double AspectRatio,
        double Solidity,
        double MeanGrayscale,
        double TextureEntropy,
        double AverageParticleArea);
}
