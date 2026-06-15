namespace PestClassifier.Service.Models;

public class RabbitMqConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public ushort PrefetchCount { get; set; } = 32;
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

public class CnnClassifierConfig
{
    public string ModelVersion { get; set; } = "cnn-frass-v1.0";
    public int MinInferenceLatencyMs { get; set; } = 45;
    public int MaxInferenceLatencyMs { get; set; } = 95;
    public double TemperatureScale { get; set; } = 1.0;
    public double NoiseLevel { get; set; } = 0.08;
    public int MinInstars { get; set; } = 1;
    public int MaxInstars { get; set; } = 5;
    public double BasePopulationPerParticle { get; set; } = 0.25;
}
