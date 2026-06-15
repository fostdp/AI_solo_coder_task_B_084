
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using TextileMonitoring.Contracts.Messages;
using ILogger = Serilog.ILogger;

namespace ZigBeeIngest.Worker;

public class ZigBeeUdpListenerWorker : BackgroundService
{
    private readonly UdpSettings _udpSettings;
    private readonly BatchingSettings _batchingSettings;
    private readonly IBus _bus;
    private readonly ILogger _logger;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentQueue<SensorDataReceived> _sensorQueue = new();
    private readonly ConcurrentQueue<VocSensorDataReceived> _vocQueue = new();
    private readonly ConcurrentQueue<FrassImageCaptured> _frassImageQueue = new();
    private readonly PeriodicTimer _flushTimer;
    private int _totalReceived;
    private int _totalPublished;
    private int _totalParseErrors;
    private int _totalVocPublished;
    private int _totalFrassImagePublished;
    private int _consecutiveTimeouts;

    public ZigBeeUdpListenerWorker(
        IOptions<UdpSettings> udpSettings,
        IOptions<BatchingSettings> batchingSettings,
        IBus bus,
        ILogger logger)
    {
        _udpSettings = udpSettings.Value;
        _batchingSettings = batchingSettings.Value;
        _bus = bus;
        _logger = logger;

        _udpClient = new UdpClient(_udpSettings.ListenPort);
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ReceiveTimeout, _udpSettings.ReceiveTimeoutMs);
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ExclusiveAddressUse, true);

        if (_udpSettings.EnableMulticast && !string.IsNullOrWhiteSpace(_udpSettings.MulticastGroup))
        {
            try
            {
                _udpClient.JoinMulticastGroup(IPAddress.Parse(_udpSettings.MulticastGroup));
                _logger.Information("Joined multicast group: {MulticastGroup}", _udpSettings.MulticastGroup);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to join multicast group, continuing with unicast only");
            }
        }

        _flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_batchingSettings.FlushIntervalMs));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("ZigBee UDP Listener starting - port: {Port}, batch: {BatchSize}, timeout: {Timeout}ms",
            _udpSettings.ListenPort, _batchingSettings.BatchSize, _udpSettings.ReceiveTimeoutMs);

        var listenTask = ReceiveLoopAsync(stoppingToken);
        var flushTask = FlushLoopAsync(stoppingToken);
        var metricsTask = MetricsLoopAsync(stoppingToken);

        await Task.WhenAll(listenTask, flushTask, metricsTask);

        _logger.Information("ZigBee UDP Listener stopped. Total: {Received} received, {Published} published, " +
            "Voc: {Voc}, FrassImage: {FrassImage}, Errors: {Errors}",
            _totalReceived, _totalPublished, _totalVocPublished, _totalFrassImagePublished, _totalParseErrors);
    }

    private async Task ReceiveLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                var result = await _udpClient.ReceiveAsync(stoppingToken);

                _consecutiveTimeouts = 0;
                Interlocked.Increment(ref _totalReceived);

                var payloadBytes = result.Buffer;
                if (!TryDispatchPayload(payloadBytes, result.RemoteEndPoint))
                {
                    Interlocked.Increment(ref _totalParseErrors);
                }

                if (_sensorQueue.Count >= _batchingSettings.BatchSize ||
                    _vocQueue.Count >= _batchingSettings.BatchSize ||
                    _frassImageQueue.Count >= _batchingSettings.BatchSize)
                {
                    await FlushBatchAsync(stoppingToken);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                Interlocked.Increment(ref _consecutiveTimeouts);
                if (_consecutiveTimeouts % 10 == 0)
                {
                    _logger.Verbose("UDP receive timeout ({Count} consecutive), continuing...", _consecutiveTimeouts);
                }
                continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in UDP receive loop");
                await Task.Delay(100, stoppingToken);
            }
        }
    }

    private bool TryDispatchPayload(byte[] payloadBytes, IPEndPoint remoteEndpoint)
    {
        try
        {
            if (payloadBytes.Length < 8)
            {
                _logger.Verbose("Discarding short packet ({Length} bytes) from {Remote}",
                    payloadBytes.Length, remoteEndpoint);
                return false;
            }

            var payloadType = payloadBytes[7];

            switch (payloadType)
            {
                case 0x01:
                case 0x02:
                {
                    var sensor = ParseBinarySensorPayload(payloadBytes, payloadType, remoteEndpoint);
                    if (sensor != null) { _sensorQueue.Enqueue(sensor); return true; }
                    return false;
                }
                case 0x03:
                {
                    var voc = ParseBinaryVocPayload(payloadBytes, remoteEndpoint);
                    if (voc != null) { _vocQueue.Enqueue(voc); return true; }
                    return false;
                }
                case 0x04:
                {
                    var frass = ParseBinaryFrassImagePayload(payloadBytes, remoteEndpoint);
                    if (frass != null) { _frassImageQueue.Enqueue(frass); return true; }
                    return false;
                }
                default:
                {
                    var textPayload = Encoding.UTF8.GetString(payloadBytes).Trim();
                    if (textPayload.StartsWith('{') && textPayload.EndsWith('}'))
                    {
                        return TryDispatchJson(textPayload, remoteEndpoint);
                    }
                    _logger.Verbose("Unknown payload type {Type:X2} from {Remote}", payloadType, remoteEndpoint);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to dispatch payload from {Remote}", remoteEndpoint);
            return false;
        }
    }

    private bool TryDispatchJson(string jsonPayload, IPEndPoint remoteEndpoint)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };

            var envelope = JsonSerializer.Deserialize<JsonEnvelope>(jsonPayload, options);
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.SensorCode))
                return false;

            switch (envelope.Kind?.ToLowerInvariant())
            {
                case "voc":
                    _vocQueue.Enqueue(envelope.ToVocMessage());
                    return true;
                case "frass_image":
                    _frassImageQueue.Enqueue(envelope.ToFrassImageMessage());
                    return true;
                default:
                    var sensor = envelope.ToSensorMessage();
                    if (sensor != null) { _sensorQueue.Enqueue(sensor); return true; }
                    return false;
            }
        }
        catch (JsonException ex)
        {
            _logger.Warning(ex, "Failed to parse JSON payload from {Remote}", remoteEndpoint);
            return false;
        }
    }

    private SensorDataReceived? ParseBinarySensorPayload(byte[] payloadBytes, byte payloadType, IPEndPoint remoteEndpoint)
    {
        try
        {
            if (payloadBytes.Length < 18) return null;
            var sensorCode = $"ZIG-{BitConverter.ToUInt16(payloadBytes, 0):X4}";
            var textileId = BitConverter.ToInt32(payloadBytes, 2);
            var temperature = BitConverter.ToSingle(payloadBytes, 8);
            var humidity = BitConverter.ToSingle(payloadBytes, 12);
            var signalStrength = payloadBytes.Length > 16 ? (short)(sbyte)payloadBytes[16] : (short)-50;

            var msg = new SensorDataReceived
            {
                CorrelationId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                SensorCode = sensorCode,
                SensorType = payloadType == 0x01 ? "dust" : "fungi",
                TextileId = textileId,
                Temperature = Math.Round(temperature, 2),
                Humidity = Math.Round(humidity, 2),
                SignalStrength = signalStrength,
                RawPayload = Convert.ToBase64String(payloadBytes)
            };

            if (payloadType == 0x01 && payloadBytes.Length >= 34)
            {
                msg.FrassDensity = Math.Round(BitConverter.ToSingle(payloadBytes, 18), 4);
                msg.HoleCount = BitConverter.ToInt32(payloadBytes, 22);
                msg.PM2_5 = Math.Round(BitConverter.ToSingle(payloadBytes, 26), 2);
                msg.PM10 = Math.Round(BitConverter.ToSingle(payloadBytes, 30), 2);
            }
            else if (payloadType == 0x02 && payloadBytes.Length >= 70)
            {
                msg.SporeCount = Math.Round(BitConverter.ToSingle(payloadBytes, 18), 2);
                msg.FungiCFU = Math.Round(BitConverter.ToSingle(payloadBytes, 22), 2);
                var fungiTypeBytes = new byte[16];
                Array.Copy(payloadBytes, 26, fungiTypeBytes, 0, Math.Min(16, payloadBytes.Length - 26));
                msg.DominantFungiType = Encoding.ASCII.GetString(fungiTypeBytes).TrimEnd('\0');
            }

            return msg;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to parse sensor binary payload (type {Type:X2}) from {Remote}",
                payloadType, remoteEndpoint);
            return null;
        }
    }

    private VocSensorDataReceived? ParseBinaryVocPayload(byte[] payloadBytes, IPEndPoint remoteEndpoint)
    {
        try
        {
            if (payloadBytes.Length < 64)
            {
                _logger.Verbose("Short VOC packet ({Length} bytes) from {Remote}", payloadBytes.Length, remoteEndpoint);
                return null;
            }

            var sensorCode = $"VOC-{BitConverter.ToUInt16(payloadBytes, 0):X4}";
            var textileId = BitConverter.ToInt32(payloadBytes, 2);
            var signalStrength = payloadBytes.Length >= 64 ? BitConverter.ToInt16(payloadBytes, 62) : (short)-60;

            return new VocSensorDataReceived
            {
                CorrelationId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                SensorCode = sensorCode,
                TextileId = textileId,
                ToluenePPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 14)), 3),
                XylenePPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 18)), 3),
                EthylbenzenePPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 22)), 3),
                FormaldehydePPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 26)), 3),
                AcetaldehydePPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 30)), 3),
                _1Octen3OlPPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 34)), 4),
                GeosminPPT = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 38)), 2),
                _2MethylisoborneolPPT = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 42)), 2),
                TotalVolatilePPB = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 46)), 2),
                Temperature = Math.Round(BitConverter.ToSingle(payloadBytes, 50), 2),
                Humidity = Math.Round(BitConverter.ToSingle(payloadBytes, 54), 2),
                AirflowMetered = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 58)), 3),
                SignalStrength = signalStrength,
                SensorStatus = 1,
                RawPayload = Convert.ToBase64String(payloadBytes)
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to parse VOC binary payload from {Remote}", remoteEndpoint);
            return null;
        }
    }

    private FrassImageCaptured? ParseBinaryFrassImagePayload(byte[] payloadBytes, IPEndPoint remoteEndpoint)
    {
        try
        {
            if (payloadBytes.Length < 65)
            {
                _logger.Verbose("Short FrassImage packet ({Length} bytes) from {Remote}", payloadBytes.Length, remoteEndpoint);
                return null;
            }

            var sensorCode = $"IMG-{BitConverter.ToUInt16(payloadBytes, 0):X4}";
            var textileId = BitConverter.ToInt32(payloadBytes, 2);
            var signalStrength = payloadBytes.Length >= 65 ? BitConverter.ToInt16(payloadBytes, 63) : (short)-60;

            return new FrassImageCaptured
            {
                CorrelationId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                SensorCode = sensorCode,
                TextileId = textileId,
                ImageWidth = BitConverter.ToUInt16(payloadBytes, 14),
                ImageHeight = BitConverter.ToUInt16(payloadBytes, 16),
                PixelDepth = payloadBytes[18],
                Magnification = Math.Round(BitConverter.ToSingle(payloadBytes, 19), 1),
                AverageParticleArea = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 23)), 4),
                ParticleCount = Math.Max(0, BitConverter.ToUInt32(payloadBytes, 27)),
                MeanGrayscale = Math.Round(BitConverter.ToSingle(payloadBytes, 31), 3),
                TextureEntropy = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 35)), 4),
                EllipticityMean = Math.Round(Math.Clamp(BitConverter.ToSingle(payloadBytes, 39), 0, 1), 4),
                AspectRatioMean = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 43)), 4),
                SolidityMean = Math.Round(Math.Clamp(BitConverter.ToSingle(payloadBytes, 47), 0, 1), 4),
                FrassDensityCorrelated = Math.Round(Math.Max(0, BitConverter.ToSingle(payloadBytes, 51)), 4),
                Temperature = Math.Round(BitConverter.ToSingle(payloadBytes, 55), 2),
                Humidity = Math.Round(BitConverter.ToSingle(payloadBytes, 59), 2),
                SignalStrength = signalStrength
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to parse FrassImage binary payload from {Remote}", remoteEndpoint);
            return null;
        }
    }

    private async Task FlushLoopAsync(CancellationToken stoppingToken)
    {
        while (await _flushTimer.WaitForNextTickAsync(stoppingToken))
        {
            if (_sensorQueue.Count > 0 || _vocQueue.Count > 0 || _frassImageQueue.Count > 0)
            {
                _logger.Verbose("Flush timer - queues: sensor={S}, voc={V}, frass={F}",
                    _sensorQueue.Count, _vocQueue.Count, _frassImageQueue.Count);
                await FlushBatchAsync(stoppingToken);
            }
        }
    }

    private async Task MetricsLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.Information("ZigBee UDP metrics - RX:{RX} TX:{S}+{V}+{F}={TOT} ParseErrors:{E} Timeouts:{TO}",
                _totalReceived, _totalPublished, _totalVocPublished, _totalFrassImagePublished,
                _totalPublished + _totalVocPublished + _totalFrassImagePublished,
                _totalParseErrors, _consecutiveTimeouts);
        }
    }

    private async Task FlushBatchAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            FlushQueueAsync(_sensorQueue, _batchingSettings.BatchSize,
                async item => await _bus.Publish(item, ctx => {
                    var rk = (item.SensorType?.ToLowerInvariant()) switch
                    {
                        "dust" => Contracts.RabbitMQ.QueueNames.RoutingKeys.DustSensor,
                        "fungi" => Contracts.RabbitMQ.QueueNames.RoutingKeys.FungiSensor,
                        _ => Contracts.RabbitMQ.QueueNames.RoutingKeys.DustSensor
                    };
                    ctx.SetRoutingKey(rk);
                }, stoppingToken),
                ref _totalPublished,
                "sensor"),
            FlushQueueAsync(_vocQueue, _batchingSettings.BatchSize,
                async item => await _bus.Publish(item, ctx => {
                    ctx.SetRoutingKey(Contracts.RabbitMQ.QueueNames.RoutingKeys.VocSensor);
                }, stoppingToken),
                ref _totalVocPublished,
                "voc"),
            FlushQueueAsync(_frassImageQueue, _batchingSettings.BatchSize,
                async item => await _bus.Publish(item, ctx => {
                    ctx.SetRoutingKey(Contracts.RabbitMQ.QueueNames.RoutingKeys.FrassImage);
                }, stoppingToken),
                ref _totalFrassImagePublished,
                "frass_image")
        );
    }

    private async Task FlushQueueAsync<T>(
        ConcurrentQueue<T> queue,
        int batchSize,
        Func<T, Task> publisher,
        CancellationToken stoppingToken,
        ref int publishCounter,
        string label)
    {
        var batch = new List<T>(batchSize);
        while (queue.TryDequeue(out var item) && batch.Count < batchSize)
        {
            batch.Add(item);
        }
        if (batch.Count == 0) return;

        try
        {
            var tasks = batch.Select(publisher);
            await Task.WhenAll(tasks);
            Interlocked.Add(ref publishCounter, batch.Count);
            _logger.Verbose("Published {Count} {Label} events", batch.Count, label);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to publish batch of {Count} {Label} events. Re-queuing...", batch.Count, label);
            foreach (var item in Enumerable.Reverse(batch))
            {
                queue.Enqueue(item);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override void Dispose()
    {
        _flushTimer.Dispose();
        try
        {
            if (_udpSettings.EnableMulticast && !string.IsNullOrWhiteSpace(_udpSettings.MulticastGroup))
            {
                _udpClient.DropMulticastGroup(IPAddress.Parse(_udpSettings.MulticastGroup));
            }
        }
        catch
        {
        }
        _udpClient?.Close();
        _udpClient?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private class JsonEnvelope
    {
        public string? Kind { get; set; }
        public string? SensorCode { get; set; }
        public int TextileId { get; set; }
        public DateTime? Timestamp { get; set; }
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public short SignalStrength { get; set; }

        public string? SensorType { get; set; }
        public double? FrassDensity { get; set; }
        public int? HoleCount { get; set; }
        public double? PM2_5 { get; set; }
        public double? PM10 { get; set; }
        public double? SporeCount { get; set; }
        public double? FungiCFU { get; set; }
        public string? DominantFungiType { get; set; }

        public double ToluenePPB { get; set; }
        public double XylenePPB { get; set; }
        public double EthylbenzenePPB { get; set; }
        public double FormaldehydePPB { get; set; }
        public double AcetaldehydePPB { get; set; }
        public double _1Octen3OlPPB { get; set; }
        public double GeosminPPT { get; set; }
        public double _2MethylisoborneolPPT { get; set; }
        public double TotalVolatilePPB { get; set; }
        public double AirflowMetered { get; set; }

        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public int PixelDepth { get; set; } = 8;
        public double Magnification { get; set; } = 40.0;
        public double AverageParticleArea { get; set; }
        public double ParticleCount { get; set; }
        public double MeanGrayscale { get; set; }
        public double TextureEntropy { get; set; }
        public double EllipticityMean { get; set; }
        public double AspectRatioMean { get; set; }
        public double SolidityMean { get; set; }
        public double FrassDensityCorrelated { get; set; }

        public SensorDataReceived ToSensorMessage() => new()
        {
            CorrelationId = Guid.NewGuid(),
            Timestamp = Timestamp ?? DateTime.UtcNow,
            SensorCode = SensorCode ?? "",
            SensorType = SensorType?.ToLowerInvariant() ?? "unknown",
            TextileId = TextileId,
            Temperature = Math.Round(Temperature, 2),
            Humidity = Math.Round(Humidity, 2),
            FrassDensity = FrassDensity.HasValue ? Math.Round(FrassDensity.Value, 4) : null,
            HoleCount = HoleCount,
            PM2_5 = PM2_5.HasValue ? Math.Round(PM2_5.Value, 2) : null,
            PM10 = PM10.HasValue ? Math.Round(PM10.Value, 2) : null,
            SporeCount = SporeCount.HasValue ? Math.Round(SporeCount.Value, 2) : null,
            FungiCFU = FungiCFU.HasValue ? Math.Round(FungiCFU.Value, 2) : null,
            DominantFungiType = DominantFungiType,
            SignalStrength = SignalStrength
        };

        public VocSensorDataReceived ToVocMessage() => new()
        {
            CorrelationId = Guid.NewGuid(),
            Timestamp = Timestamp ?? DateTime.UtcNow,
            SensorCode = SensorCode ?? "",
            TextileId = TextileId,
            ToluenePPB = Math.Round(ToluenePPB, 3),
            XylenePPB = Math.Round(XylenePPB, 3),
            EthylbenzenePPB = Math.Round(EthylbenzenePPB, 3),
            FormaldehydePPB = Math.Round(FormaldehydePPB, 3),
            AcetaldehydePPB = Math.Round(AcetaldehydePPB, 3),
            _1Octen3OlPPB = Math.Round(_1Octen3OlPPB, 4),
            GeosminPPT = Math.Round(GeosminPPT, 2),
            _2MethylisoborneolPPT = Math.Round(_2MethylisoborneolPPT, 2),
            TotalVolatilePPB = Math.Round(TotalVolatilePPB, 2),
            Temperature = Math.Round(Temperature, 2),
            Humidity = Math.Round(Humidity, 2),
            AirflowMetered = Math.Round(AirflowMetered, 3),
            SignalStrength = SignalStrength,
            SensorStatus = 1
        };

        public FrassImageCaptured ToFrassImageMessage() => new()
        {
            CorrelationId = Guid.NewGuid(),
            Timestamp = Timestamp ?? DateTime.UtcNow,
            SensorCode = SensorCode ?? "",
            TextileId = TextileId,
            ImageWidth = ImageWidth,
            ImageHeight = ImageHeight,
            PixelDepth = PixelDepth,
            Magnification = Math.Round(Magnification, 1),
            AverageParticleArea = Math.Round(AverageParticleArea, 4),
            ParticleCount = Math.Max(0, ParticleCount),
            MeanGrayscale = Math.Round(MeanGrayscale, 3),
            TextureEntropy = Math.Round(TextureEntropy, 4),
            EllipticityMean = Math.Round(Math.Clamp(EllipticityMean, 0, 1), 4),
            AspectRatioMean = Math.Round(Math.Max(0, AspectRatioMean), 4),
            SolidityMean = Math.Round(Math.Clamp(SolidityMean, 0, 1), 4),
            FrassDensityCorrelated = Math.Round(Math.Max(0, FrassDensityCorrelated), 4),
            Temperature = Math.Round(Temperature, 2),
            Humidity = Math.Round(Humidity, 2),
            SignalStrength = SignalStrength
        };
    }
}
