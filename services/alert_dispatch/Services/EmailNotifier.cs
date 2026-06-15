using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Options;
using Serilog;
using TextileMonitoring.AlertDispatch.Models;

namespace TextileMonitoring.AlertDispatch.Services;

public class EmailNotifier : IEmailNotifier
{
    private readonly SmtpConfig _config;
    private readonly ILogger _logger;

    public EmailNotifier(IOptions<NotificationConfig> config, ILogger logger)
    {
        _config = config.Value.Smtp;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string subject, string body, IEnumerable<string> recipients, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Host))
        {
            _logger.Warning("SMTP host not configured, skipping email notification");
            return false;
        }

        var recipientList = recipients?.ToList() ?? new List<string>();
        if (!recipientList.Any())
        {
            _logger.Warning("No email recipients configured, skipping notification");
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_config.FromDisplayName, _config.FromAddress));
            foreach (var recipient in recipientList)
            {
                message.To.Add(new MailboxAddress(string.Empty, recipient));
            }
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = body
            };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_config.Host, _config.Port, _config.EnableSsl, ct);
            if (!string.IsNullOrWhiteSpace(_config.User))
            {
                await client.AuthenticateAsync(_config.User, _config.Password, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            _logger.Information("Email notification sent successfully to {RecipientCount} recipients: {Subject}",
                recipientList.Count, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send email notification: {Subject}", subject);
            return false;
        }
    }
}
