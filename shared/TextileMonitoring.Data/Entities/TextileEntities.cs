
namespace TextileMonitoring.Data.Entities;

public class Textile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Dynasty { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal AreaCm2 { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime? AcquisitionDate { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Sensor> Sensors { get; set; } = new List<Sensor>();
    public ICollection<HoleMarker> HoleMarkers { get; set; } = new List<HoleMarker>();
    public ICollection<MoldRegion> MoldRegions { get; set; } = new List<MoldRegion>();
    public ICollection<Prediction> Predictions { get; set; } = new List<Prediction>();
    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
    public ICollection<FrassImageCapture> FrassImageCaptures { get; set; } = new List<FrassImageCapture>();
    public ICollection<PestClassificationRecord> PestClassificationRecords { get; set; } = new List<PestClassificationRecord>();
    public ICollection<VocClassificationRecord> VocClassificationRecords { get; set; } = new List<VocClassificationRecord>();
    public ICollection<NitrogenTreatmentSession> NitrogenTreatmentSessions { get; set; } = new List<NitrogenTreatmentSession>();
    public ICollection<FiberStrengthTest> FiberStrengthTests { get; set; } = new List<FiberStrengthTest>();
    public ICollection<VulnerabilityAssessment> VulnerabilityAssessments { get; set; } = new List<VulnerabilityAssessment>();
}

public enum SensorType
{
    DustSensor = 1,
    FungiSensor = 2,
    VocSensor = 3,
    FrassImageSensor = 4
}

public class Sensor
{
    public int Id { get; set; }
    public int TextileId { get; set; }
    public string SensorCode { get; set; } = string.Empty;
    public SensorType SensorType { get; set; }
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public Textile? Textile { get; set; }
}

public class DustSensorData
{
    public long Id { get; set; }
    public int SensorId { get; set; }
    public int TextileId { get; set; }
    public DateTime ReadingTime { get; set; }
    public decimal PM2_5 { get; set; }
    public decimal PM10 { get; set; }
    public decimal FrassDensity { get; set; }
    public int HoleCount { get; set; }
    public decimal HoleDensity { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Humidity { get; set; }
    public short? ZigBeeSignalStrength { get; set; }
    public int SensorStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FungiSensorData
{
    public long Id { get; set; }
    public int SensorId { get; set; }
    public int TextileId { get; set; }
    public DateTime ReadingTime { get; set; }
    public decimal SporeCount { get; set; }
    public decimal FungiCFU { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Humidity { get; set; }
    public string? DominantFungiType { get; set; }
    public short? ZigBeeSignalStrength { get; set; }
    public int SensorStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HoleMarker
{
    public int Id { get; set; }
    public int TextileId { get; set; }
    public int SensorId { get; set; }
    public int? ImageId { get; set; }
    public decimal RelativeX { get; set; }
    public decimal RelativeY { get; set; }
    public decimal RadiusMm { get; set; }
    public decimal? PerimeterMm { get; set; }
    public decimal? AreaMm2 { get; set; }
    public int SeverityLevel { get; set; }
    public DateTime DetectedAt { get; set; }
    public int Status { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public class MoldRegion
{
    public int Id { get; set; }
    public int TextileId { get; set; }
    public int? SensorId { get; set; }
    public int? ImageId { get; set; }
    public decimal RelativeX { get; set; }
    public decimal RelativeY { get; set; }
    public decimal RadiusMm { get; set; }
    public decimal? AreaMm2 { get; set; }
    public string? DominantFungiType { get; set; }
    public int SeverityLevel { get; set; }
    public DateTime DetectedAt { get; set; }
    public int Status { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public enum PredictionModel
{
    Logistic = 1,
    Gompertz = 2,
    Synergy = 3,
    LotkaVolterra = 4
}

public enum RiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public class Prediction
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public PredictionModel Model { get; set; }
    public int HorizonDays { get; set; }
    public DateTime PredictionDate { get; set; }
    public decimal MaxPredictedValue { get; set; }
    public decimal? PredictedFungiCFU { get; set; }
    public decimal? SynergyRisk { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public double Confidence { get; set; }
    public string? ParametersJson { get; set; }
    public string? PredictionJson { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public class Alert
{
    public int Id { get; set; }
    public int TextileId { get; set; }
    public int AlertType { get; set; }
    public int AlertLevel { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal ActualValue { get; set; }
    public decimal Threshold { get; set; }
    public bool Resolved { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? ResolutionNotes { get; set; }

    public Textile? Textile { get; set; }
}

public class AlertConfig
{
    public int Id { get; set; }
    public string ConfigKey { get; set; } = string.Empty;
    public string ConfigValue { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class FrassImageCapture
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public int SensorId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime CaptureTime { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int PixelDepth { get; set; }
    public decimal Magnification { get; set; }
    public decimal AverageParticleArea { get; set; }
    public int ParticleCount { get; set; }
    public decimal MeanGrayscale { get; set; }
    public decimal TextureEntropy { get; set; }
    public decimal EllipticityMean { get; set; }
    public decimal AspectRatioMean { get; set; }
    public decimal SolidityMean { get; set; }
    public decimal FrassDensityCorrelated { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Humidity { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public class PestClassificationRecord
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public int SensorId { get; set; }
    public long? SourceImageId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid SourceImageCorrelationId { get; set; }
    public DateTime ClassifiedAt { get; set; }
    public int PredictedSpeciesId { get; set; }
    public string PredictedSpeciesName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? ProbabilitiesJson { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public decimal InferenceLatencyMs { get; set; }
    public int PredictedInstars { get; set; }
    public decimal EstimatedPopulationSize { get; set; }
    public int RiskSeverityScore { get; set; }
    public string? RecommendedAction { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public class VocSensorData
{
    public long Id { get; set; }
    public int SensorId { get; set; }
    public int TextileId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime ReadingTime { get; set; }
    public decimal ToluenePPB { get; set; }
    public decimal XylenePPB { get; set; }
    public decimal EthylbenzenePPB { get; set; }
    public decimal FormaldehydePPB { get; set; }
    public decimal AcetaldehydePPB { get; set; }
    public decimal _1Octen3OlPPB { get; set; }
    public decimal GeosminPPT { get; set; }
    public decimal _2MethylisoborneolPPT { get; set; }
    public decimal TotalVolatilePPB { get; set; }
    public decimal AirflowMetered { get; set; }
    public decimal? Temperature { get; set; }
    public decimal? Humidity { get; set; }
    public short? ZigBeeSignalStrength { get; set; }
    public int SensorStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class VocClassificationRecord
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public int SensorId { get; set; }
    public long? SourceVocDataId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid SourceSensorCorrelationId { get; set; }
    public DateTime ClassifiedAt { get; set; }
    public int PredictedMoldSpeciesId { get; set; }
    public string PredictedMoldSpeciesName { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? ProbabilitiesJson { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public decimal EstimatedBiomassMg { get; set; }
    public decimal EstimatedGrowthStageDays { get; set; }
    public decimal MycotoxinRiskIndex { get; set; }
    public int EarlyWarningSeverity { get; set; }
    public decimal PredictedIncubationHours { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public class NitrogenTreatmentSession
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid RequestCorrelationId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public int TargetOrganismsId { get; set; }
    public decimal TargetOxygenConcentrationPct { get; set; }
    public decimal NitrogenFlowRateLpm { get; set; }
    public int ExposureDurationMinutes { get; set; }
    public decimal ChamberPressureKpa { get; set; }
    public decimal ChamberTemperatureC { get; set; }
    public decimal ChamberHumidityPct { get; set; }
    public decimal CurrentPestDensity { get; set; }
    public decimal CurrentFungiCFU { get; set; }
    public int? PrimaryPestTargetId { get; set; }

    public decimal PredictedEggMortalityRate { get; set; }
    public decimal PredictedLarvaeMortalityRate { get; set; }
    public decimal PredictedAdultMortalityRate { get; set; }
    public decimal PredictedFungiSterilityRate { get; set; }
    public decimal CILowPct { get; set; }
    public decimal CIHighPct { get; set; }
    public int ProbitTransformValue { get; set; }
    public decimal LD99Minutes { get; set; }
    public decimal MinimumRequiredExposureMin { get; set; }
    public decimal RecommendedSafetyExposureMin { get; set; }
    public decimal FiberStrengthDegradationPct { get; set; }
    public decimal ColorChangeDeltaE { get; set; }

    public int SessionStatus { get; set; }
    public bool IsSuccessCriteriaMet { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }

    public Textile? Textile { get; set; }
}

public class FiberStrengthTest
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public DateTime TestDate { get; set; }
    public decimal OriginalBreakingLoadN { get; set; }
    public decimal CurrentBreakingLoadN { get; set; }
    public decimal TensileStrengthRemainingPct { get; set; }
    public decimal ElongationAtBreakPct { get; set; }
    public decimal YoungModulusGpa { get; set; }
    public string? TestStandard { get; set; }
    public int SampleCount { get; set; }
    public decimal SampleStandardDeviation { get; set; }
    public decimal CoefficientOfVariationPct { get; set; }
    public string? OperatorName { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

public class VulnerabilityAssessment
{
    public long Id { get; set; }
    public int TextileId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime AssessmentDate { get; set; }

    public double TopsisScore { get; set; }
    public int TopsisRank { get; set; }
    public int TopsisTotalCount { get; set; }
    public int PriorityId { get; set; }
    public string PriorityName { get; set; } = string.Empty;

    public string? CriteriaJson { get; set; }
    public double CompositePestDamageScore { get; set; }
    public double CompositeMoldAreaScore { get; set; }
    public double FiberTensileStrengthRemainingPct { get; set; }
    public double DynastyScarcityValueScore { get; set; }
    public double HistoricalSignificanceScore { get; set; }
    public double RestorationCostEstimateCny { get; set; }
    public double RelativeClosenessCC { get; set; }
    public double ConsistencyRatioCR { get; set; }
    public double TreatmentCostBenefitRatio { get; set; }

    public int ProjectedYearsIfNoAction { get; set; }
    public int ProjectedYearsWithAction { get; set; }
    public string? ActionRecommendation { get; set; }

    public DateTime CreatedAt { get; set; }

    public Textile? Textile { get; set; }
}

