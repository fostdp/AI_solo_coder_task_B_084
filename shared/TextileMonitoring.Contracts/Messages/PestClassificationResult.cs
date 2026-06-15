
namespace TextileMonitoring.Contracts.Messages;

public record PestClassificationResult
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid SourceImageCorrelationId { get; init; }
    public int TextileId { get; init; }
    public string SensorCode { get; init; } = string.Empty;
    public PestSpecies PredictedSpecies { get; init; }
    public double Confidence { get; init; }
    public Dictionary<PestSpecies, double> SpeciesProbabilities { get; init; } = new();
    public string ModelVersion { get; init; } = "cnn-frass-v1.0";
    public double InferenceLatencyMs { get; init; }
    public double FeatureVectorNorm { get; init; }
    public int PredictedInstars { get; init; }
    public double EstimatedPopulationSize { get; init; }
    public int RiskSeverityScore { get; init; }
    public string? RecommendedAction { get; init; }
}
