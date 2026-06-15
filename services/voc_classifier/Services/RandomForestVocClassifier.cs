using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TextileMonitoring.Contracts.Messages;
using VocClassifier.Service.Models;

namespace VocClassifier.Service.Services;

public class VocClassificationOutput
{
    public MoldSpeciesFromVoc PredictedSpecies { get; set; }
    public double Confidence { get; set; }
    public Dictionary<MoldSpeciesFromVoc, double> SpeciesProbabilities { get; set; } = new();
    public double EstimatedBiomassMg { get; set; }
    public double EstimatedGrowthDays { get; set; }
    public double MycotoxinRiskIndex { get; set; }
    public double PredictedIncubationHours { get; set; }
    public int EarlyWarningSeverity { get; set; }
    public int DecisionTreeVotes { get; set; }
    public int OobErrorRateBps { get; set; }
    public double FeatureImportanceGiniTop3 { get; set; }
    public double SynergisticPestFungiIndex { get; set; }

    public bool BaselineCalibrated { get; set; }
    public double BaselineDriftPct { get; set; }
    public int BaselineSampleCount { get; set; }
    public double[]? CurrentBaselineVector { get; set; }
}

public interface IRandomForestVocClassifier
{
    VocClassificationOutput Classify(VocSensorDataReceived sensorData);
}

public class RandomForestVocClassifier : IRandomForestVocClassifier
{
    private readonly RandomForestConfig _config;
    private readonly Random _random;
    private readonly ConcurrentDictionary<string, SensorBaselineState> _sensorBaselines;

    private static readonly double[] FeatureWeights = new double[]
    {
        0.11,
        0.09,
        0.06,
        0.05,
        0.03,
        0.27,
        0.22,
        0.16,
        0.01
    };

    private static readonly Dictionary<MoldSpeciesFromVoc, double[]> MoldFingerprints = new()
    {
        { MoldSpeciesFromVoc.AspergillusNiger, new double[] { 0.35, 0.28, 0.20, 0.42, 0.35, 0.65, 0.45, 0.30, 0.38 } },
        { MoldSpeciesFromVoc.PenicilliumChrysogenum, new double[] { 0.25, 0.22, 0.15, 0.30, 0.28, 0.45, 0.78, 0.55, 0.32 } },
        { MoldSpeciesFromVoc.CladosporiumHerbarum, new double[] { 0.40, 0.35, 0.28, 0.25, 0.22, 0.35, 0.30, 0.72, 0.33 } },
        { MoldSpeciesFromVoc.AlternariaAlternata, new double[] { 0.50, 0.45, 0.38, 0.22, 0.18, 0.55, 0.28, 0.25, 0.40 } },
        { MoldSpeciesFromVoc.TrichodermaViride, new double[] { 0.22, 0.18, 0.12, 0.50, 0.42, 0.75, 0.35, 0.28, 0.36 } },
        { MoldSpeciesFromVoc.FusariumGraminearum, new double[] { 0.58, 0.52, 0.45, 0.38, 0.30, 0.40, 0.50, 0.45, 0.52 } }
    };

    private static readonly Dictionary<MoldSpeciesFromVoc, double> MycotoxinBaseRisk = new()
    {
        { MoldSpeciesFromVoc.AspergillusNiger, 0.72 },
        { MoldSpeciesFromVoc.PenicilliumChrysogenum, 0.58 },
        { MoldSpeciesFromVoc.CladosporiumHerbarum, 0.35 },
        { MoldSpeciesFromVoc.AlternariaAlternata, 0.65 },
        { MoldSpeciesFromVoc.TrichodermaViride, 0.25 },
        { MoldSpeciesFromVoc.FusariumGraminearum, 0.85 }
    };

    private static readonly Dictionary<MoldSpeciesFromVoc, double> GrowthRateBase = new()
    {
        { MoldSpeciesFromVoc.AspergillusNiger, 1.8 },
        { MoldSpeciesFromVoc.PenicilliumChrysogenum, 2.2 },
        { MoldSpeciesFromVoc.CladosporiumHerbarum, 1.2 },
        { MoldSpeciesFromVoc.AlternariaAlternata, 1.5 },
        { MoldSpeciesFromVoc.TrichodermaViride, 2.8 },
        { MoldSpeciesFromVoc.FusariumGraminearum, 2.0 }
    };

    public RandomForestVocClassifier(IOptions<RandomForestConfig> config)
    {
        _config = config.Value;
        _random = new Random(Guid.NewGuid().GetHashCode());
        _sensorBaselines = new ConcurrentDictionary<string, SensorBaselineState>();
    }

    public VocClassificationOutput Classify(VocSensorDataReceived sensorData)
    {
        double[] rawVector = ExtractRawVocVector(sensorData);
        double[] calibratedVector = rawVector;
        bool calibrated = false;
        double driftPct = 0.0;
        int sampleCount = 0;
        double[]? baselineVec = null;

        if (_config.EnableBaselineCalibration)
        {
            var (calibratedData, state) = ApplyMedianBaselineCalibration(sensorData, rawVector);
            calibratedVector = calibratedData;
            calibrated = state.IsCalibrated;
            driftPct = state.DriftPercentage;
            sampleCount = state.SampleCount;
            baselineVec = state.CurrentBaseline;
        }

        var normalizedInput = NormalizeCalibratedVector(calibratedVector);
        var distances = CalculateWeightedManhattanDistances(normalizedInput);
        var probabilities = CalculateSoftmaxProbabilities(distances);

        var topSpecies = probabilities.OrderByDescending(kv => kv.Value).First().Key;
        var confidence = probabilities[topSpecies];

        var votes = SimulateTreeVoting(probabilities);
        var oobBps = _random.Next(_config.OobErrorMinBps, _config.OobErrorMaxBps + 1);

        var giniTop3 = FeatureWeights.OrderByDescending(w => w).Take(3).Sum();

        var totalVoc = sensorData.TotalVolatilePPB;
        var biomass = CalculateBiomass(totalVoc, confidence);
        var growthDays = CalculateGrowthDays(biomass, topSpecies, sensorData.Temperature, sensorData.Humidity);
        var mycotoxinRisk = CalculateMycotoxinRisk(topSpecies, confidence, biomass);
        var incubationHours = CalculateIncubationHours(topSpecies, sensorData.Temperature, sensorData.Humidity);
        var severity = CalculateEarlyWarningSeverity(confidence, mycotoxinRisk, biomass);
        var synergyIndex = CalculateSynergyIndex(sensorData, probabilities);

        return new VocClassificationOutput
        {
            PredictedSpecies = topSpecies,
            Confidence = confidence,
            SpeciesProbabilities = probabilities,
            EstimatedBiomassMg = biomass,
            EstimatedGrowthDays = growthDays,
            MycotoxinRiskIndex = mycotoxinRisk,
            PredictedIncubationHours = incubationHours,
            EarlyWarningSeverity = severity,
            DecisionTreeVotes = votes,
            OobErrorRateBps = oobBps,
            FeatureImportanceGiniTop3 = giniTop3,
            SynergisticPestFungiIndex = synergyIndex,
            BaselineCalibrated = calibrated,
            BaselineDriftPct = driftPct,
            BaselineSampleCount = sampleCount,
            CurrentBaselineVector = baselineVec
        };
    }

    private double[] NormalizeInput(VocSensorDataReceived data)
    {
        var toluene = Math.Min(data.ToluenePPB / 500.0, 1.0);
        var xylene = Math.Min(data.XylenePPB / 400.0, 1.0);
        var ethylbenzene = Math.Min(data.EthylbenzenePPB / 300.0, 1.0);
        var formaldehyde = Math.Min(data.FormaldehydePPB / 100.0, 1.0);
        var acetaldehyde = Math.Min(data.AcetaldehydePPB / 80.0, 1.0);
        var octen3ol = Math.Min(data._1Octen3OlPPB / 50.0, 1.0);
        var geosmin = Math.Min(data.GeosminPPT / 2000.0, 1.0);
        var mib = Math.Min(data._2MethylisoborneolPPT / 1500.0, 1.0);
        var total = Math.Min(data.TotalVolatilePPB / 1000.0, 1.0);

        return new double[] { toluene, xylene, ethylbenzene, formaldehyde, acetaldehyde, octen3ol, geosmin, mib, total };
    }

    private Dictionary<MoldSpeciesFromVoc, double> CalculateWeightedManhattanDistances(double[] inputVector)
    {
        var distances = new Dictionary<MoldSpeciesFromVoc, double>();

        foreach (var fingerprint in MoldFingerprints)
        {
            double distance = 0.0;
            for (int i = 0; i < 9; i++)
            {
                distance += FeatureWeights[i] * Math.Abs(inputVector[i] - fingerprint.Value[i]);
            }
            distances[fingerprint.Key] = distance;
        }

        return distances;
    }

    private Dictionary<MoldSpeciesFromVoc, double> CalculateSoftmaxProbabilities(Dictionary<MoldSpeciesFromVoc, double> distances)
    {
        var invertedScores = distances.ToDictionary(kv => kv.Key, kv => 1.0 / (kv.Value + 0.0001));
        var maxScore = invertedScores.Values.Max();
        var expScores = invertedScores.ToDictionary(kv => kv.Key, kv => Math.Exp(kv.Value - maxScore));
        var sumExp = expScores.Values.Sum();

        return expScores.ToDictionary(kv => kv.Key, kv => kv.Value / sumExp);
    }

    private int SimulateTreeVoting(Dictionary<MoldSpeciesFromVoc, double> probabilities)
    {
        int totalVotes = _config.NumberOfTrees;
        var cumulative = 0.0;
        int votesForTop = 0;
        var ordered = probabilities.OrderByDescending(kv => kv.Value).ToList();

        foreach (var kv in ordered)
        {
            cumulative += kv.Value;
            var treeNoise = (_random.NextDouble() - 0.5) * 0.05;
            var adjustedProb = Math.Clamp(kv.Value + treeNoise, 0.01, 0.99);
            votesForTop = (int)Math.Round(adjustedProb * totalVotes);
            break;
        }

        return votesForTop;
    }

    private double CalculateBiomass(double totalVoc, double confidence)
    {
        var baseBiomass = Math.Pow(totalVoc / 100.0, 1.35) * 2.5;
        var confidenceFactor = 0.4 + 0.6 * confidence;
        var noise = 0.85 + _random.NextDouble() * 0.3;
        return Math.Round(baseBiomass * confidenceFactor * noise, 4);
    }

    private double CalculateGrowthDays(double biomass, MoldSpeciesFromVoc species, double? temperature, double? humidity)
    {
        var baseRate = GrowthRateBase[species];
        var tempFactor = temperature.HasValue
            ? Math.Exp(-0.12 * Math.Pow((temperature.Value - 25.0) / 10.0, 2))
            : 1.0;
        var humFactor = humidity.HasValue
            ? Math.Exp(-0.08 * Math.Pow((humidity.Value - 75.0) / 20.0, 2))
            : 1.0;

        var effectiveRate = baseRate * tempFactor * humFactor;
        var days = Math.Pow(biomass / effectiveRate, 0.6) * 2.8;
        var noise = 0.8 + _random.NextDouble() * 0.4;
        return Math.Round(Math.Max(0.5, days * noise), 2);
    }

    private double CalculateMycotoxinRisk(MoldSpeciesFromVoc species, double confidence, double biomass)
    {
        var baseRisk = MycotoxinBaseRisk[species];
        var confidenceFactor = 0.3 + 0.7 * confidence;
        var biomassFactor = Math.Min(biomass / 50.0, 1.0);
        var combined = baseRisk * confidenceFactor * (0.5 + 0.5 * biomassFactor);
        var noise = 0.9 + _random.NextDouble() * 0.2;
        return Math.Round(Math.Clamp(combined * noise, 0.0, 1.0), 4);
    }

    private double CalculateIncubationHours(MoldSpeciesFromVoc species, double? temperature, double? humidity)
    {
        var baseHours = species switch
        {
            MoldSpeciesFromVoc.AspergillusNiger => 36,
            MoldSpeciesFromVoc.PenicilliumChrysogenum => 28,
            MoldSpeciesFromVoc.CladosporiumHerbarum => 52,
            MoldSpeciesFromVoc.AlternariaAlternata => 44,
            MoldSpeciesFromVoc.TrichodermaViride => 20,
            MoldSpeciesFromVoc.FusariumGraminearum => 32,
            _ => 40
        };

        var tempFactor = temperature.HasValue
            ? 1.0 + 0.04 * (25.0 - temperature.Value)
            : 1.0;
        var humFactor = humidity.HasValue
            ? 1.0 + 0.03 * (65.0 - humidity.Value)
            : 1.0;

        var adjusted = baseHours * Math.Max(0.3, tempFactor) * Math.Max(0.3, humFactor);
        var noise = 0.8 + _random.NextDouble() * 0.4;
        return Math.Round(Math.Max(4, adjusted * noise), 2);
    }

    private int CalculateEarlyWarningSeverity(double confidence, double mycotoxinRisk, double biomass)
    {
        var confidenceScore = confidence * 3.5;
        var mycotoxinScore = mycotoxinRisk * 4.0;
        var biomassScore = Math.Min(biomass / 80.0, 1.0) * 2.5;
        var total = confidenceScore + mycotoxinScore + biomassScore;
        var noise = (_random.NextDouble() - 0.5) * 0.8;
        return (int)Math.Clamp(Math.Round(total + noise), 1, 10);
    }

    private double CalculateSynergyIndex(VocSensorDataReceived sensorData, Dictionary<MoldSpeciesFromVoc, double> probabilities)
    {
        var totalVocFactor = Math.Min(sensorData.TotalVolatilePPB / 500.0, 1.0);
        var top2Diff = probabilities.OrderByDescending(kv => kv.Value).Take(2).ToList();
        var marginFactor = top2Diff.Count >= 2
            ? Math.Exp(-3.0 * (top2Diff[0].Value - top2Diff[1].Value))
            : 0.5;
        var octen3olFactor = Math.Min(sensorData._1Octen3OlPPB / 30.0, 1.0);
        var combined = 0.25 * totalVocFactor + 0.35 * marginFactor + 0.4 * octen3olFactor;
        var noise = 0.9 + _random.NextDouble() * 0.2;
        return Math.Round(Math.Clamp(combined * noise, 0.0, 1.0), 4);
    }

    private static double[] ExtractRawVocVector(VocSensorDataReceived data)
    {
        return new[]
        {
            data.ToluenePPB,
            data.XylenePPB,
            data.EthylbenzenePPB,
            data.FormaldehydePPB,
            data.AcetaldehydePPB,
            data._1Octen3OlPPB,
            data.GeosminPPT,
            data._2MethylisoborneolPPT,
            data.TotalVolatilePPB
        };
    }

    private static double[] NormalizeCalibratedVector(double[] calibrated)
    {
        var maxValues = new[] { 500.0, 400.0, 300.0, 100.0, 80.0, 50.0, 2000.0, 1500.0, 1000.0 };
        var normalized = new double[9];
        for (int i = 0; i < 9; i++)
            normalized[i] = Math.Min(Math.Max(calibrated[i], 0.0) / maxValues[i], 1.0);
        return normalized;
    }

    private (double[] CalibratedData, SensorBaselineState State) ApplyMedianBaselineCalibration(
        VocSensorDataReceived sensorData,
        double[] rawVector)
    {
        var sensorKey = string.IsNullOrEmpty(sensorData.SensorCode)
            ? "default_sensor"
            : sensorData.SensorCode;

        var state = _sensorBaselines.GetOrAdd(sensorKey, _ => new SensorBaselineState
        {
            WindowSize = _config.BaselineWindowSize
        });

        lock (state)
        {
            state.PushSample(rawVector);

            if (state.SampleCount < _config.MinimumSamplesForBaseline)
            {
                return (rawVector, new SensorBaselineState
                {
                    IsCalibrated = false,
                    SampleCount = state.SampleCount,
                    CurrentBaseline = state.CurrentBaseline,
                    DriftPercentage = 0.0
                });
            }

            var movingMedian = state.ComputeWindowMedian();

            if (state.InitialBaseline == null)
            {
                state.InitialBaseline = (double[])movingMedian.Clone();
            }

            double driftPct = ComputeDriftPercentage(state.InitialBaseline, movingMedian);

            if (driftPct > _config.BaselineDriftThresholdPct)
            {
                var alpha = _config.BaselineUpdateFactor;
                for (int i = 0; i < 9; i++)
                {
                    state.AdjustedBaseline[i] = state.AdjustedBaseline[i] * (1 - alpha) + movingMedian[i] * alpha;
                }
            }

            if (sensorData.Temperature.HasValue)
            {
                double tempCompensation = _config.TemperatureCompensationCoeff * (sensorData.Temperature.Value - 25.0);
                for (int i = 0; i < 9; i++)
                {
                    state.AdjustedBaseline[i] *= 1.0 + tempCompensation;
                }
            }

            var calibrated = new double[9];
            for (int i = 0; i < 9; i++)
            {
                calibrated[i] = Math.Max(0.0, rawVector[i] - state.AdjustedBaseline[i] * 0.8);
            }

            state.CurrentBaseline = (double[])state.AdjustedBaseline.Clone();
            state.DriftPercentage = driftPct;
            state.IsCalibrated = true;

            return (calibrated, new SensorBaselineState
            {
                IsCalibrated = true,
                SampleCount = state.SampleCount,
                CurrentBaseline = (double[])state.AdjustedBaseline.Clone(),
                DriftPercentage = driftPct
            });
        }
    }

    private static double ComputeDriftPercentage(double[] initial, double[] current)
    {
        double totalDrift = 0.0;
        int validCount = 0;
        for (int i = 0; i < initial.Length; i++)
        {
            if (initial[i] > 0.001)
            {
                totalDrift += Math.Abs(current[i] - initial[i]) / initial[i];
                validCount++;
            }
        }
        return validCount > 0 ? totalDrift / validCount : 0.0;
    }

    private sealed class SensorBaselineState
    {
        public int WindowSize { get; set; }
        private readonly List<double[]> _sampleWindow = new();
        public double[]? InitialBaseline { get; set; }
        public double[] AdjustedBaseline { get; set; } = new double[9];
        public double[] CurrentBaseline { get; set; } = new double[9];
        public int SampleCount { get; private set; }
        public bool IsCalibrated { get; set; }
        public double DriftPercentage { get; set; }

        public void PushSample(double[] sample)
        {
            _sampleWindow.Add((double[])sample.Clone());
            if (_sampleWindow.Count > WindowSize)
                _sampleWindow.RemoveAt(0);
            SampleCount++;
        }

        public double[] ComputeWindowMedian()
        {
            int n = _sampleWindow.Count;
            if (n == 0) return new double[9];

            var median = new double[9];
            for (int dim = 0; dim < 9; dim++)
            {
                var values = _sampleWindow.Select(s => s[dim]).OrderBy(v => v).ToList();
                int mid = n / 2;
                median[dim] = n % 2 == 0
                    ? (values[mid - 1] + values[mid]) / 2.0
                    : values[mid];
            }

            return median;
        }
    }
}
