
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TextileMonitoring.ZigBeeSimulator
{
    public class SensorNode
    {
        public string SensorCode { get; set; } = string.Empty;
        public int SensorType { get; set; }
        public string ZigBeeAddress { get; set; } = string.Empty;
        public int TextileId { get; set; }
        public decimal BaseFrass { get; set; }
        public int BaseHoles { get; set; }
        public decimal BaseCFU { get; set; }
        public decimal CurrentFrass { get; set; }
        public int CurrentHoles { get; set; }
        public decimal CurrentCFU { get; set; }
    }

    public class ZigBeeDustPayload
    {
        [JsonProperty("sensorCode")]
        public string SensorCode { get; set; } = string.Empty;

        [JsonProperty("zigBeeAddress")]
        public string ZigBeeAddress { get; set; } = string.Empty;

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("pm2_5")]
        public decimal PM2_5 { get; set; }

        [JsonProperty("pm10")]
        public decimal PM10 { get; set; }

        [JsonProperty("frassDensity")]
        public decimal FrassDensity { get; set; }

        [JsonProperty("temperature")]
        public decimal Temperature { get; set; }

        [JsonProperty("humidity")]
        public decimal Humidity { get; set; }

        [JsonProperty("holeCount")]
        public int HoleCount { get; set; }

        [JsonProperty("signalStrength")]
        public int SignalStrength { get; set; }
    }

    public class ZigBeeFungiPayload
    {
        [JsonProperty("sensorCode")]
        public string SensorCode { get; set; } = string.Empty;

        [JsonProperty("zigBeeAddress")]
        public string ZigBeeAddress { get; set; } = string.Empty;

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("sporeCount")]
        public decimal SporeCount { get; set; }

        [JsonProperty("fungiCFU")]
        public decimal FungiCFU { get; set; }

        [JsonProperty("temperature")]
        public decimal Temperature { get; set; }

        [JsonProperty("humidity")]
        public decimal Humidity { get; set; }

        [JsonProperty("dominantFungiType")]
        public string DominantFungiType { get; set; } = string.Empty;

        [JsonProperty("signalStrength")]
        public int SignalStrength { get; set; }
    }

    public class BatchPayload
    {
        [JsonProperty("dustData")]
        public List<ZigBeeDustPayload>? DustData { get; set; }

        [JsonProperty("fungiData")]
        public List<ZigBeeFungiPayload>? FungiData { get; set; }
    }

    class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Random _random = new Random(Guid.NewGuid().GetHashCode());
        private static List<SensorNode> _dustSensors = new();
        private static List<SensorNode> _fungiSensors = new();
        private static string _apiBaseUrl = "http://localhost:5000/api";
        private static int _reportIntervalMinutes = 240;
        private static bool _enableAbnormalEvents = true;
        private static int _sentCount = 0;
        private static readonly object _lockObj = new();

        static async Task Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     古代织绣品虫蛀与霉变协同监测系统                         ║");
            Console.WriteLine("║     ZigBee 传感器数据模拟器 v1.0                              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            ParseArgs(args);
            InitializeSensors();

            Console.WriteLine($"[配置] API 地址: {_apiBaseUrl}");
            Console.WriteLine($"[配置] 上报间隔: {_reportIntervalMinutes} 分钟");
            Console.WriteLine($"[配置] 粉尘传感器: {_dustSensors.Count} 台");
            Console.WriteLine($"[配置] 真菌孢子捕捉器: {_fungiSensors.Count} 台");
            Console.WriteLine($"[配置] 异常事件模拟: {(_enableAbnormalEvents ? "启用" : "禁用")}");
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\n[信息] 正在停止模拟器...");
            };

            if (args.Contains("--onetime") || args.Contains("-o"))
            {
                Console.WriteLine("[模式] 单次上报模式");
                await SendBatchReport();
                Console.WriteLine($"\n[完成] 单次上报完成，共发送 {_sentCount} 条数据");
                return;
            }

            if (args.Contains("--backfill") || args.Contains("-b"))
            {
                Console.WriteLine("[模式] 历史数据回填模式");
                int days = 30;
                var daysArg = Array.FindIndex(args, a => a == "--days");
                if (daysArg >= 0 && daysArg + 1 < args.Length && int.TryParse(args[daysArg + 1], out var d))
                    days = d;

                await BackfillHistoricalData(days, cts.Token);
                Console.WriteLine($"\n[完成] 历史数据回填完成，共发送 {_sentCount} 条数据");
                return;
            }

            Console.WriteLine("[模式] 持续实时模拟");
            Console.WriteLine("[提示] 按 Ctrl+C 停止\n");

            var firstReport = SendBatchReport();
            await firstReport;

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_reportIntervalMinutes));

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (_enableAbnormalEvents && _random.Next(100) < 8)
                        TriggerAbnormalEvent();

                    await timer.WaitForNextTickAsync(cts.Token);
                    await SendBatchReport();

                    lock (_lockObj)
                    {
                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 上报完成 | 累计: {_sentCount} 条 | 按 Ctrl+C 停止");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[错误] {ex.Message}");
                }
            }

            Console.WriteLine($"\n[总结] 模拟器已停止，累计发送 {_sentCount} 条数据");
        }

        static void ParseArgs(string[] args)
        {
            var urlIdx = Array.FindIndex(args, a => a == "--url");
            if (urlIdx >= 0 && urlIdx + 1 < args.Length)
                _apiBaseUrl = args[urlIdx + 1].TrimEnd('/');

            var intervalIdx = Array.FindIndex(args, a => a == "--interval");
            if (intervalIdx >= 0 && intervalIdx + 1 < args.Length && int.TryParse(args[intervalIdx + 1], out var minutes))
                _reportIntervalMinutes = Math.Max(1, minutes);

            _enableAbnormalEvents = !args.Contains("--no-abnormal");
        }

        static void InitializeSensors()
        {
            string[] fungiTypes = { N_("曲霉属"), N_("青霉属"), N_("毛霉属"), N_("根霉属"), N_("木霉属") };

            for (int i = 1; i <= 30; i++)
            {
                var sensor = new SensorNode
                {
                    SensorCode = $"DUS-{i:000}",
                    SensorType = 1,
                    ZigBeeAddress = $"0x{_random.Next(0x1000, 0xFFFF):X4}",
                    TextileId = _random.Next(1, 101),
                    BaseFrass = 0.3m + (decimal)(_random.NextDouble() * 8),
                    BaseHoles = _random.Next(0, 6),
                    CurrentFrass = 0,
                    CurrentHoles = 0
                };
                sensor.CurrentFrass = sensor.BaseFrass;
                sensor.CurrentHoles = sensor.BaseHoles;
                _dustSensors.Add(sensor);
            }

            for (int i = 1; i <= 20; i++)
            {
                var sensor = new SensorNode
                {
                    SensorCode = $"FUN-{i:000}",
                    SensorType = 2,
                    ZigBeeAddress = $"0x{_random.Next(0x1000, 0xFFFF):X4}",
                    TextileId = _random.Next(1, 101),
                    BaseCFU = 30m + (decimal)(_random.NextDouble() * 250),
                    CurrentCFU = 0
                };
                sensor.CurrentCFU = sensor.BaseCFU;
                _fungiSensors.Add(sensor);
            }
        }

        static void TriggerAbnormalEvent()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ 模拟异常事件发生...");

            if (_random.Next(2) == 0)
            {
                var targetSensor = _dustSensors[_random.Next(_dustSensors.Count)];
                decimal boostFrass = 5m + (decimal)(_random.NextDouble() * 15);
                int addHoles = 3 + _random.Next(8);

                targetSensor.CurrentFrass += boostFrass;
                targetSensor.CurrentHoles += addHoles;

                Console.WriteLine($"    🐛 蛀虫爆发: {targetSensor.SensorCode} | 蛀屑+{boostFrass:N1} | 新增孔洞+{addHoles}");
            }
            else
            {
                var targetSensor = _fungiSensors[_random.Next(_fungiSensors.Count)];
                decimal boostCFU = 200m + (decimal)(_random.NextDouble() * 400);

                targetSensor.CurrentCFU += boostCFU;

                Console.WriteLine($"    🍄 霉菌暴发: {targetSensor.SensorCode} | CFU+{boostCFU:N0}");
            }
        }

        static async Task SendBatchReport()
        {
            var batchPayload = new BatchPayload
            {
                DustData = new List<ZigBeeDustPayload>(),
                FungiData = new List<ZigBeeFungiPayload>()
            };

            var timestamp = DateTime.Now;

            foreach (var sensor in _dustSensors)
            {
                var payload = GenerateDustReading(sensor, timestamp);
                batchPayload.DustData.Add(payload);

                decimal delta = (decimal)(_random.NextDouble() * 2 - 1) * 0.3m;
                sensor.CurrentFrass = Math.Max(0.1m, sensor.BaseFrass * 0.5m + sensor.CurrentFrass * 0.5m + delta);

                if (_random.Next(100) < 15)
                    sensor.CurrentHoles += 1;
            }

            string[] fungiTypes = { "曲霉属", "青霉属", "毛霉属", "根霉属", "木霉属" };
            foreach (var sensor in _fungiSensors)
            {
                var payload = GenerateFungiReading(sensor, timestamp, fungiTypes[_random.Next(fungiTypes.Length)]);
                batchPayload.FungiData.Add(payload);

                decimal delta = (decimal)(_random.NextDouble() * 40 - 20);
                sensor.CurrentCFU = Math.Max(10m, sensor.BaseCFU * 0.4m + sensor.CurrentCFU * 0.6m + delta);
            }

            try
            {
                var json = JsonConvert.SerializeObject(batchPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/sensors/zigbee/batch", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                lock (_lockObj)
                {
                    int total = batchPayload.DustData.Count + batchPayload.FungiData.Count;
                    _sentCount += total;

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[{timestamp:HH:mm:ss}] ✓ 批量上报成功 ({batchPayload.DustData.Count}粉尘 + {batchPayload.FungiData.Count}真菌) | {responseBody.Trim().Trim('{', '}')}");
                    }
                    else
                    {
                        Console.WriteLine($"[{timestamp:HH:mm:ss}] ✗ 上报失败: {response.StatusCode} | {responseBody[..Math.Min(100, responseBody.Length)]}");
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                lock (_lockObj)
                {
                    Console.WriteLine($"[{timestamp:HH:mm:ss}] ✗ 网络错误: {ex.Message}");
                }
            }
        }

        static ZigBeeDustPayload GenerateDustReading(SensorNode sensor, DateTime timestamp)
        {
            decimal temp = 19m + (decimal)(_random.NextDouble() * 10);
            decimal hum = 45m + (decimal)(_random.NextDouble() * 25);

            if (hum > 65)
                sensor.CurrentFrass *= 1.005m;

            return new ZigBeeDustPayload
            {
                SensorCode = sensor.SensorCode,
                ZigBeeAddress = sensor.ZigBeeAddress,
                Timestamp = timestamp,
                PM2_5 = Math.Round(sensor.CurrentFrass * 2.5m + (decimal)(_random.NextDouble() * 2), 4),
                PM10 = Math.Round(sensor.CurrentFrass * 4.2m + (decimal)(_random.NextDouble() * 4), 4),
                FrassDensity = Math.Round(sensor.CurrentFrass, 4),
                Temperature = Math.Round(temp, 2),
                Humidity = Math.Round(hum, 2),
                HoleCount = sensor.CurrentHoles,
                SignalStrength = -(40 + _random.Next(35))
            };
        }

        static ZigBeeFungiPayload GenerateFungiReading(SensorNode sensor, DateTime timestamp, string dominantFungi)
        {
            decimal temp = 19m + (decimal)(_random.NextDouble() * 10);
            decimal hum = 45m + (decimal)(_random.NextDouble() * 25);

            decimal cfuMultiplier = 1.0m;
            if (temp > 25 && hum > 65)
                cfuMultiplier = 1.15m;
            else if (temp > 28)
                cfuMultiplier = 1.08m;

            decimal finalCFU = sensor.CurrentCFU * cfuMultiplier;

            return new ZigBeeFungiPayload
            {
                SensorCode = sensor.SensorCode,
                ZigBeeAddress = sensor.ZigBeeAddress,
                Timestamp = timestamp,
                SporeCount = Math.Round(finalCFU * 12.5m + (decimal)(_random.NextDouble() * 50), 4),
                FungiCFU = Math.Round(finalCFU, 4),
                Temperature = Math.Round(temp, 2),
                Humidity = Math.Round(hum, 2),
                DominantFungiType = dominantFungi,
                SignalStrength = -(40 + _random.Next(35))
            };
        }

        static async Task BackfillHistoricalData(int days, CancellationToken ct)
        {
            Console.WriteLine($"[回填] 开始回填过去 {days} 天的历史数据...");
            int totalHours = days * 24;
            int intervalHours = _reportIntervalMinutes / 60;
            int totalSteps = totalHours / intervalHours;

            Console.WriteLine($"[回填] 预计上报次数: {totalSteps} | 数据条目: ~{totalSteps * 50}");
            Console.WriteLine();

            string[] fungiTypes = { "曲霉属", "青霉属", "毛霉属", "根霉属", "木霉属" };
            var startTime = DateTime.Now.AddHours(-days * 24);

            for (int step = 0; step < totalSteps; step++)
            {
                if (ct.IsCancellationRequested) break;

                var currentTime = startTime.AddHours(step * intervalHours);
                var batchPayload = new BatchPayload
                {
                    DustData = new List<ZigBeeDustPayload>(),
                    FungiData = new List<ZigBeeFungiPayload>()
                };

                foreach (var sensor in _dustSensors)
                {
                    double progress = (double)step / totalSteps;
                    decimal growthFactor = 1.0m + (decimal)(progress * 0.4 * _random.NextDouble());

                    var payload = new ZigBeeDustPayload
                    {
                        SensorCode = sensor.SensorCode,
                        ZigBeeAddress = sensor.ZigBeeAddress,
                        Timestamp = currentTime,
                        PM2_5 = Math.Round(sensor.BaseFrass * growthFactor * 2.5m, 4),
                        PM10 = Math.Round(sensor.BaseFrass * growthFactor * 4.2m, 4),
                        FrassDensity = Math.Round(sensor.BaseFrass * growthFactor, 4),
                        Temperature = Math.Round(19m + (decimal)(_random.NextDouble() * 10), 2),
                        Humidity = Math.Round(45m + (decimal)(_random.NextDouble() * 25), 2),
                        HoleCount = sensor.BaseHoles + (int)(step / 40.0 * _random.NextDouble() * 5),
                        SignalStrength = -(40 + _random.Next(35))
                    };
                    batchPayload.DustData.Add(payload);
                }

                foreach (var sensor in _fungiSensors)
                {
                    double progress = (double)step / totalSteps;
                    decimal growthFactor = 1.0m + (decimal)(progress * 0.6 * _random.NextDouble());

                    var payload = new ZigBeeFungiPayload
                    {
                        SensorCode = sensor.SensorCode,
                        ZigBeeAddress = sensor.ZigBeeAddress,
                        Timestamp = currentTime,
                        SporeCount = Math.Round(sensor.BaseCFU * growthFactor * 12.5m, 4),
                        FungiCFU = Math.Round(sensor.BaseCFU * growthFactor, 4),
                        Temperature = Math.Round(19m + (decimal)(_random.NextDouble() * 10), 2),
                        Humidity = Math.Round(45m + (decimal)(_random.NextDouble() * 25), 2),
                        DominantFungiType = fungiTypes[_random.Next(fungiTypes.Length)],
                        SignalStrength = -(40 + _random.Next(35))
                    };
                    batchPayload.FungiData.Add(payload);
                }

                try
                {
                    var json = JsonConvert.SerializeObject(batchPayload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync($"{_apiBaseUrl}/sensors/zigbee/batch", content, ct);

                    int total = batchPayload.DustData.Count + batchPayload.FungiData.Count;
                    lock (_lockObj) { _sentCount += total; }

                    if (step % 10 == 0 || step == totalSteps - 1)
                    {
                        double percent = (double)(step + 1) / totalSteps * 100;
                        Console.WriteLine($"[回填] {currentTime:MM-dd HH:mm} | 进度 {percent,5:F1}% | 已发送 {_sentCount,6} 条");
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(ct);
                        Console.WriteLine($"    ✗ 错误: {response.StatusCode} | {error[..Math.Min(80, error.Length)]}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    ✗ 异常: {ex.Message}");
                }

                if (step % 5 == 4)
                    await Task.Delay(200, ct);
            }
        }

        static string N_(string s) => s;
    }
}
