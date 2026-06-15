using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;
using TextileMonitoring.AlertDispatch.Models;

namespace TextileMonitoring.AlertDispatch.Services;

public class DingTalkNotifier : IDingTalkNotifier
{
    private readonly DingTalkConfig _config;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    public DingTalkNotifier(IOptions<NotificationConfig> config, ILogger logger, HttpClient httpClient)
    {
        _config = config.Value.DingTalk;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<bool> SendAsync(string title, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.Webhook))
        {
            _logger.Warning("DingTalk webhook not configured, skipping notification");
            return false;
        }

        try
        {
            var url = BuildSignedUrl();
            var payload = new
            {
                msgtype = "markdown",
                markdown = new
                {
                    title = title,
                    text = content
                },
                at = new
                {
                    isAtAll = _config.EnableAtAll,
                    atMobiles = _config.AtMobiles
                }
            };

            var json = JsonConvert.SerializeObject(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, httpContent, ct);
            var responseContent = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<DingTalkResponse>(responseContent);
                if (result?.Errcode == 0)
                {
                    _logger.Information("DingTalk notification sent successfully: {Title}", title);
                    return true;
                }
                _logger.Error("DingTalk notification failed: {Errcode} - {Errmsg}", result?.Errcode, result?.Errmsg);
                return false;
            }

            _logger.Error("DingTalk notification HTTP error: {StatusCode} - {Content}", response.StatusCode, responseContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send DingTalk notification: {Title}", title);
            return false;
        }
    }

    private string BuildSignedUrl()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var stringToSign = $"{timestamp}\n{_config.Secret}";
        var sign = ComputeSignature(stringToSign, _config.Secret);
        return $"{_config.Webhook}&timestamp={timestamp}&sign={sign}";
    }

    private static string ComputeSignature(string stringToSign, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hash);
    }

    private class DingTalkResponse
    {
        public int Errcode { get; set; }
        public string Errmsg { get; set; } = string.Empty;
    }
}
