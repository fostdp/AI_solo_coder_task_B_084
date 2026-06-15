namespace TreatmentSimulator.Service.Models;

public class RabbitMqConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public ushort PrefetchCount { get; set; } = 16;
    public int RetryCount { get; set; } = 3;
    public int RetryIntervalMs { get; set; } = 1000;
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeout { get; set; } = 120;
    public int MaxRetryCount { get; set; } = 5;
    public int MaxRetryDelaySec { get; set; } = 10;
    public bool EnableDetailedErrors { get; set; } = false;
    public bool EnableSensitiveDataLogging { get; set; } = false;
}

public class ProbitStageParameters
{
    public double[] K { get; set; } = new double[3];
    public double LD50Minutes { get; set; }
    public double Beta { get; set; }
}

public class PestSpeciesTolerance
{
    public double LD50Multiplier { get; set; } = 1.0;
    public double BetaMultiplier { get; set; } = 1.0;
    public double DoseEfficiencyMultiplier { get; set; } = 1.0;
}

public class ProbitModelConfig
{
    public ProbitStageParameters Eggs { get; set; } = new()
    {
        K = new[] { 0.0075, 0.0025, 0.0003 },
        LD50Minutes = 185.0,
        Beta = 3.2
    };

    public ProbitStageParameters Larvae { get; set; } = new()
    {
        K = new[] { 0.012, 0.0035, 0.00045 },
        LD50Minutes = 98.0,
        Beta = 4.1
    };

    public ProbitStageParameters Adult { get; set; } = new()
    {
        K = new[] { 0.009, 0.0028, 0.00035 },
        LD50Minutes = 132.0,
        Beta = 3.6
    };

    public ProbitStageParameters Fungi { get; set; } = new()
    {
        K = new[] { 0.006, 0.002, 0.00025 },
        LD50Minutes = 340.0,
        Beta = 2.8
    };

    public Dictionary<string, PestSpeciesTolerance> PestTolerance { get; set; } = new()
    {
        { "LepismaSaccharina", new PestSpeciesTolerance { LD50Multiplier = 1.00, BetaMultiplier = 1.00, DoseEfficiencyMultiplier = 1.00 } },
        { "CtenolepismaLongicaudata", new PestSpeciesTolerance { LD50Multiplier = 1.05, BetaMultiplier = 0.95, DoseEfficiencyMultiplier = 0.98 } },
        { "AttagenusPellio", new PestSpeciesTolerance { LD50Multiplier = 1.18, BetaMultiplier = 0.90, DoseEfficiencyMultiplier = 0.92 } },
        { "TineolaBisselliella", new PestSpeciesTolerance { LD50Multiplier = 0.88, BetaMultiplier = 1.08, DoseEfficiencyMultiplier = 1.08 } },
        { "AnthrenusVerbasci", new PestSpeciesTolerance { LD50Multiplier = 1.28, BetaMultiplier = 0.85, DoseEfficiencyMultiplier = 0.88 } }
    };

    public bool EnablePestSpeciesCorrection { get; set; } = true;

    public double StandardOxygenPct { get; set; } = 0.5;
    public double ReferenceTempC { get; set; } = 24.0;
    public double DeltaMethodBaseSE { get; set; } = 0.035;
    public double DeltaMethodExposureCoeff { get; set; } = 0.0001;
    public double ConfidenceZScore { get; set; } = 1.96;
    public double SafetyFactor { get; set; } = 1.3;
    public double TargetMortalityPct { get; set; } = 99.0;
    public double MaxAllowedDeltaE { get; set; } = 3.0;
    public double MaxAllowedStrengthLossPct { get; set; } = 2.5;
    public double ProbitLD99 { get; set; } = 7.33;
}
