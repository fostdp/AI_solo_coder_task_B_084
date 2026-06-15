
namespace TextileMonitoring.Contracts.Messages;

public enum TreatmentTarget
{
    AllStages = 0,
    EggsOnly = 1,
    LarvaeOnly = 2,
    AdultOnly = 3,
    FungiSterilization = 4
}

public record NitrogenTreatmentRequest
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public int TextileId { get; init; }
    public string RequestedBy { get; init; } = string.Empty;
    public TreatmentTarget TargetOrganisms { get; init; }

    public double TargetOxygenConcentrationPct { get; init; } = 0.5;
    public double NitrogenFlowRateLpm { get; init; } = 12.0;
    public int ExposureDurationMinutes { get; init; } = 480;
    public double ChamberPressureKpa { get; init; } = 102.5;
    public double ChamberTemperatureC { get; init; } = 24.0;
    public double ChamberHumidityPct { get; init; } = 45.0;

    public double CurrentPestDensity { get; init; }
    public double CurrentFungiCFU { get; init; }
    public PestSpecies? PrimaryPestTarget { get; init; }
    public bool FiberStrengthPreTestRequired { get; init; } = true;
    public string? Notes { get; init; }
}

public record NitrogenTreatmentResult
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Guid RequestCorrelationId { get; init; }
    public int TextileId { get; init; }

    public double PredictedEggMortalityRate { get; init; }
    public double PredictedLarvaeMortalityRate { get; init; }
    public double PredictedAdultMortalityRate { get; init; }
    public double PredictedFungiSterilityRate { get; init; }
    public double ConfidenceIntervalLowPct { get; init; }
    public double ConfidenceIntervalHighPct { get; init; }

    public int ProbitTransformValue { get; init; }
    public double CalculatedLethalDoseLD99Min { get; init; }
    public double MinimumRequiredExposureMin { get; init; }
    public double RecommendedSafetyExposureMin { get; init; }
    public double FiberStrengthDegradationEstimatedPct { get; init; }
    public double ColorChangeDeltaE { get; init; }

    public bool IsSuccessCriteriaMet { get; init; }
    public string? TreatmentRiskNotes { get; init; }
    public int PostTreatmentMonitoringDaysRecommended { get; init; }
}
