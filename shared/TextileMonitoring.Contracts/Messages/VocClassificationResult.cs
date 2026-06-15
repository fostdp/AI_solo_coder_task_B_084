
namespace TextileMonitoring.Contracts.Messages;

public record VocClassificationResult
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid SourceSensorCorrelationId { get; init; }
    public int TextileId { get; init; }
    public string SensorCode { get; init; } = string.Empty;

    public MoldSpeciesFromVoc PredictedMoldSpecies { get; init; }
    public double Confidence { get; init; }
    public Dictionary<MoldSpeciesFromVoc, double> SpeciesProbabilities { get; init; } = new();
    public string ModelVersion { get; init; } = "rf-voc-v1.0";
    public double EstimatedBiomassMg { get; init; }
    public double EstimatedGrowthStageDays { get; init; }
    public double MycotoxinRiskIndex { get; init; }
    public double SynergisticPestFungiIndex { get; init; }
    public int DecisionTreeVotes { get; init; }
    public int OobErrorRateBps { get; init; }
    public double FeatureImportanceGiniTop3 { get; init; }
    public int EarlyWarningSeverity { get; init; }
    public double PredictedIncubationHours { get; init; }
}
