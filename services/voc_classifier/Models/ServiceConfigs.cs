namespace VocClassifier.Service.Models;

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

public class RandomForestConfig
{
    public int NumberOfTrees { get; set; } = 200;
    public int OobErrorMinBps { get; set; } = 65;
    public int OobErrorMaxBps { get; set; } = 180;
    public string ModelVersion { get; set; } = "rf-voc-v1.0";
}
