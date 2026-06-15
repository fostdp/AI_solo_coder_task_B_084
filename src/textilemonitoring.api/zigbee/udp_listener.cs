
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Models;
using TextileMonitoring.API.Services;

namespace TextileMonitoring.API.ZigBee
{
    public class ZigBeeUdpListener : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ZigBeeUdpListener> _logger;
        private readonly UdpClient _udpClient;
        private readonly int _listenPort = 8684;
        private readonly int _batchSize = 50;
        private readonly Queue<object> _pendingPayloads = new();

        public ZigBeeUdpListener(IServiceProvider serviceProvider, ILogger<ZigBeeUdpListener> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _udpClient = new UdpClient(_listenPort);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ZigBee UDP监听器启动，监听端口 {Port}", _listenPort);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
var result = await _udpClient.ReceiveAsync(stoppingToken);
                    var payloadBytes = result.Buffer;

                    if (payloadBytes.Length < 8)
                    {
                        _logger.LogWarning("收到无效ZigBee数据包，长度不足: {Length}", payloadBytes.Length);
                        continue;
                    }

                    var sensorCode = System.Text.Encoding.ASCII.GetString(payloadBytes, 0, 7).Trim();
                    var payloadType = payloadBytes[7];

                    object payload;
                    switch (payloadType)
                    {
                        case 0x01:
                            payload = ParseDustPayload(sensorCode, payloadBytes);
                            break;
                        case 0x02:
                            payload = ParseFungiPayload(sensorCode, payloadBytes);
                            break;
                        default:
                            _logger.LogWarning("未知ZigBee负载类型: {Type:X2}", payloadType);
                            continue;
                    }

                    _pendingPayloads.Enqueue(payload);

                    if (_pendingPayloads.Count >= _batchSize)
                    {
                        await FlushBatchAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("ZigBee监听器正在停止...");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ZigBee数据包处理失败");
                }
            }

            await FlushBatchAsync();
            _udpClient.Close();
        }

        private ZigBeeDustPayloadDto ParseDustPayload(string sensorCode, byte[] data)
        {
            if (data.Length < 32)
                throw new ArgumentException("粉尘数据包长度不足");

            return new ZigBeeDustPayloadDto
            {
                SensorCode = sensorCode,
                ZigBeeAddress = $"0x{BitConverter.ToUInt16(data, 8):X4}",
                Timestamp = DateTime.UnixEpoch.AddSeconds(BitConverter.ToUInt32(data, 10)).ToLocalTime(),
                PM2_5 = Math.Round(BitConverter.ToSingle(data, 14), 2),
                PM10 = Math.Round(BitConverter.ToSingle(data, 18), 2),
                FrassDensity = Math.Round(BitConverter.ToSingle(data, 22), 4),
                Temperature = Math.Round(BitConverter.ToSingle(data, 26), 1),
                Humidity = Math.Round(BitConverter.ToSingle(data, 30), 1),
                HoleCount = BitConverter.ToInt32(data, 34),
                SignalStrength = BitConverter.ToInt16(data, 38)
            };
        }

        private ZigBeeFungiPayloadDto ParseFungiPayload(string sensorCode, byte[] data)
        {
            if (data.Length < 36)
                throw new ArgumentException("真菌数据包长度不足");

            var fungiTypeBytes = new byte[32];
            Array.Copy(data, 34, fungiTypeBytes, 0, 32);
            var dominantType = System.Text.Encoding.UTF8.GetString(fungiTypeBytes).TrimEnd('\0');

            return new ZigBeeFungiPayloadDto
            {
                SensorCode = sensorCode,
                ZigBeeAddress = $"0x{BitConverter.ToUInt16(data, 8):X4}",
                Timestamp = DateTime.UnixEpoch.AddSeconds(BitConverter.ToUInt32(data, 10)).ToLocalTime(),
                SporeCount = Math.Round(BitConverter.ToDouble(data, 14), 2),
                FungiCFU = Math.Round(BitConverter.ToDouble(data, 22), 2),
                Temperature = Math.Round(BitConverter.ToSingle(data, 30), 1),
                Humidity = Math.Round(BitConverter.ToSingle(data, 34), 1),
                DominantFungiType = dominantType,
                SignalStrength = BitConverter.ToInt16(data, 66)
            };
        }

        private async Task FlushBatchAsync()
        {
            if (_pendingPayloads.Count == 0) return;

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sensorDataService = scope.ServiceProvider.GetRequiredService<ISensorDataService>();

            var dustBatch = new List<ZigBeeDustPayloadDto>();
            var fungiBatch = new List<ZigBeeFungiPayloadDto>();

            while (_pendingPayloads.TryDequeue(out var payload))
            {
                if (payload is ZigBeeDustPayloadDto d) dustBatch.Add(d);
                else if (payload is ZigBeeFungiPayloadDto f) fungiBatch.Add(f);
            }

            _logger.LogInformation("ZigBee批量入库: 粉尘 {DustCount} 条, 真菌 {FungiCount} 条", dustBatch.Count, fungiBatch.Count);

            await sensorDataService.BatchImportDustDataAsync(dustBatch);
            await sensorDataService.BatchImportFungiDataAsync(fungiBatch);
        }

        public override void Dispose()
        {
            _udpClient?.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
