namespace TextileMonitoring.AlertDispatch.Models;

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

public class NotificationConfig
{
    public DingTalkConfig DingTalk { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();
    public List<string> EmailRecipients { get; set; } = new();
    public int CooldownMinutes { get; set; } = 60;
}

public class DingTalkConfig
{
    public string Webhook { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public bool EnableAtAll { get; set; } = false;
    public List<string> AtMobiles { get; set; } = new();
}

public class SmtpConfig
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = "织绣品监测系统";
}
