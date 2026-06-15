namespace TextileMonitoring.MildewGompertz.Models;

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

public class MildewModelConfig
{
    public string DefaultModel { get; set; } = "Gompertz";
    public double DefaultGrowthRateRho { get; set; } = 0.015;
    public double DefaultCarryingCapacityKf { get; set; } = 500.0;
    public double DefaultInitialCFU { get; set; } = 100.0;
    public double TemperatureSensitivity { get; set; } = 0.06;
    public double HumiditySensitivity { get; set; } = 0.025;
    public double SynergyInteractionPhi { get; set; } = 1.35;
    public double PestInteractionCoeff { get; set; } = 0.0005;
    public int DefaultHorizonDays { get; set; } = 30;
    public double MoldDetectionThreshold { get; set; } = 200.0;
    public double RadiusPerCFU { get; set; } = 0.125;
    public double MinRadiusMm { get; set; } = 15.0;
    public double MaxRadiusMm { get; set; } = 75.0;
}
