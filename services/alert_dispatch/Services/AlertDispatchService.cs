using MassTransit;
using Microsoft.Extensions.Options;
using Serilog;
using TextileMonitoring.AlertDispatch.Models;
using TextileMonitoring.Contracts.Messages;

namespace TextileMonitoring.AlertDispatch.Services;

public class AlertDispatchService : IAlertDispatchService
{
    private readonly IAlertRepository _alertRepository;
    private readonly IDingTalkNotifier _dingTalkNotifier;
    private readonly IEmailNotifier _emailNotifier;
    private readonly IBus _bus;
    private readonly NotificationConfig _notificationConfig;
    private readonly AlertTemplateConfig _templateConfig;
    private readonly ILogger _logger;

    public AlertDispatchService(
        IAlertRepository alertRepository,
        IDingTalkNotifier dingTalkNotifier,
        IEmailNotifier emailNotifier,
        IBus bus,
        IOptions<NotificationConfig> notificationConfig,
        IOptions<AlertDispatchOptions> dispatchOptions,
        ILogger logger)
    {
        _alertRepository = alertRepository;
        _dingTalkNotifier = dingTalkNotifier;
        _emailNotifier = emailNotifier;
        _bus = bus;
        _notificationConfig = notificationConfig.Value;
        _templateConfig = dispatchOptions.Value.Templates;
        _logger = logger;
    }

    public async Task DispatchAsync(AlertTriggered alert, CancellationToken ct = default)
    {
        _logger.Information("Processing alert: {CorrelationId}, Level: {AlertLevel}, Type: {AlertType}",
            alert.CorrelationId, alert.AlertLevel, alert.AlertType);

        var savedAlert = await _alertRepository.CreateAlertAsync(alert, ct);
        _logger.Information("Alert persisted to database with ID: {AlertId}", savedAlert.Id);

        var channels = GetChannelsForLevel(alert.AlertLevel);

        if (channels.Count == 0)
        {
            _logger.Information("Alert level {AlertLevel} does not require notification, skipping dispatch", alert.AlertLevel);
            await PublishDispatchedEvent(alert.CorrelationId, savedAlert.Id, "None", string.Empty, true, null, ct);
            return;
        }

        foreach (var channel in channels)
        {
            try
            {
                bool success;
                string recipient;

                switch (channel)
                {
                    case NotificationChannel.DingTalk:
                        success = await SendDingTalkNotification(alert, ct);
                        recipient = _notificationConfig.DingTalk.Webhook;
                        break;

                    case NotificationChannel.Email:
                        success = await SendEmailNotification(alert, ct);
                        recipient = string.Join(";", _notificationConfig.EmailRecipients);
                        break;

                    default:
                        continue;
                }

                await PublishDispatchedEvent(
                    alert.CorrelationId,
                    savedAlert.Id,
                    channel.ToString(),
                    recipient,
                    success,
                    success ? null : "Notification send failed",
                    ct);

                if (!success)
                {
                    _logger.Warning("Failed to send alert via {Channel} for alert {CorrelationId}",
                        channel, alert.CorrelationId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error dispatching alert via {Channel} for alert {CorrelationId}",
                    channel, alert.CorrelationId);

                await PublishDispatchedEvent(
                    alert.CorrelationId,
                    savedAlert.Id,
                    channel.ToString(),
                    string.Empty,
                    false,
                    ex.Message,
                    ct);
            }
        }

        _logger.Information("Alert dispatch completed for {CorrelationId}", alert.CorrelationId);
    }

    private async Task<bool> SendDingTalkNotification(AlertTriggered alert, CancellationToken ct)
    {
        var title = RenderTemplate(_templateConfig.DingTalkTitleTemplate, alert);
        var body = RenderTemplate(_templateConfig.DingTalkBodyTemplate, alert);
        return await _dingTalkNotifier.SendAsync(title, body, ct);
    }

    private async Task<bool> SendEmailNotification(AlertTriggered alert, CancellationToken ct)
    {
        var subject = RenderTemplate(_templateConfig.EmailSubjectTemplate, alert);
        var body = RenderTemplate(_templateConfig.EmailBodyTemplate, alert);
        return await _emailNotifier.SendAsync(subject, body, _notificationConfig.EmailRecipients, ct);
    }

    private async Task PublishDispatchedEvent(
        Guid correlationId,
        int alertId,
        string channel,
        string recipient,
        bool success,
        string? errorMessage,
        CancellationToken ct)
    {
        var dispatchedEvent = new AlertDispatched
        {
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow,
            AlertId = alertId,
            Channel = channel,
            Recipient = recipient,
            Success = success,
            ErrorMessage = errorMessage
        };

        await _bus.Publish(dispatchedEvent, ct);
        _logger.Verbose("Published AlertDispatched event: {CorrelationId}, Channel: {Channel}, Success: {Success}",
            correlationId, channel, success);
    }

    private static List<NotificationChannel> GetChannelsForLevel(string alertLevel)
    {
        return alertLevel.ToLower() switch
        {
            "critical" => new List<NotificationChannel> { NotificationChannel.DingTalk, NotificationChannel.Email },
            "high" => new List<NotificationChannel> { NotificationChannel.DingTalk },
            "medium" => new List<NotificationChannel> { NotificationChannel.Email },
            "low" => new List<NotificationChannel>(),
            _ => new List<NotificationChannel>()
        };
    }

    private static string RenderTemplate(string template, AlertTriggered alert)
    {
        var result = template
            .Replace("{CorrelationId}", alert.CorrelationId.ToString())
            .Replace("{Timestamp}", alert.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{TextileId}", alert.TextileId.ToString())
            .Replace("{TextileName}", alert.TextileName)
            .Replace("{AlertType}", alert.AlertType)
            .Replace("{AlertLevel}", alert.AlertLevel)
            .Replace("{AlertLevelClass}", alert.AlertLevel.ToLower())
            .Replace("{Title}", alert.Title)
            .Replace("{Description}", alert.Description)
            .Replace("{ActualValue}", alert.ActualValue.ToString("F2"))
            .Replace("{Threshold}", alert.Threshold.ToString("F2"))
            .Replace("{Recommendation}", alert.Recommendation ?? string.Empty)
            .Replace("{SourcePredictionId}", alert.SourcePredictionId ?? string.Empty);

        return result;
    }

    private enum NotificationChannel
    {
        DingTalk,
        Email
    }
}
