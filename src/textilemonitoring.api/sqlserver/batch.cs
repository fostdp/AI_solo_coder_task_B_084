
using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.DTOs;
using TextileMonitoring.API.Models;

namespace TextileMonitoring.API.SqlServer
{
    public interface ISqlServerBatchWriter
    {
        Task<int> BulkInsertDustSensorDataAsync(List<DustSensorData> dataList, CancellationToken ct = default);
        Task<int> BulkInsertFungiSensorDataAsync(List<FungiSensorData> dataList, CancellationToken ct = default);
        Task<int> BatchInsertHoleMarkersAsync(List<HoleMarker> markers, CancellationToken ct = default);
    }

    public class SqlServerBatchWriter : ISqlServerBatchWriter
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SqlServerBatchWriter> _logger;
        private readonly string _connectionString;

        public SqlServerBatchWriter(ApplicationDbContext context, ILogger<SqlServerBatchWriter> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        public async Task<int> BulkInsertDustSensorDataAsync(List<DustSensorData> dataList, CancellationToken ct = default)
        {
            if (dataList == null || dataList.Count == 0) return 0;

            int totalInserted = 0;
            const int efBatchThreshold = 500;

            if (dataList.Count < efBatchThreshold)
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                _context.DustSensorData.AddRange(dataList);
                totalInserted = await _context.SaveChangesAsync(ct);
                _context.ChangeTracker.AutoDetectChangesEnabled = true;
                _logger.LogInformation("EF Core批量写入粉尘数据: {Count} 条", totalInserted);
                return totalInserted;
            }

            const int chunkSize = 3000;
            for (int chunkIdx = 0; chunkIdx < dataList.Count; chunkIdx += chunkSize)
            {
                var chunk = dataList.GetRange(chunkIdx, Math.Min(chunkSize, dataList.Count - chunkIdx));

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                using var bulkCopy = new SqlBulkCopy(connection)
                {
                    DestinationTableName = "[dbo].[DustSensorData]",
                    BatchSize = chunk.Count,
                    BulkCopyTimeout = 120,
                    EnableStreaming = true
                };

                var columnMappings = new Dictionary<string, string>
                {
                    ["SensorId"] = "SensorId",
                    ["TextileId"] = "TextileId",
                    ["ReadingTime"] = "ReadingTime",
                    ["PM2_5"] = "PM2_5",
                    ["PM10"] = "PM10",
                    ["FrassDensity"] = "FrassDensity",
                    ["HoleCount"] = "HoleCount",
                    ["HoleDensity"] = "HoleDensity",
                    ["Temperature"] = "Temperature",
                    ["Humidity"] = "Humidity",
                    ["SignalStrength"] = "SignalStrength",
                    ["SensorStatus"] = "SensorStatus"
                };

                foreach (var map in columnMappings)
                    bulkCopy.ColumnMappings.Add(map.Key, map.Value);

                using var reader = new EntityListDataReader<DustSensorData>(chunk, columnMappings.Keys.ToList());

                await bulkCopy.WriteToServerAsync(reader, ct);
                totalInserted += chunk.Count;

                _logger.LogDebug("SqlBulkCopy粉尘批次 [{Start}-{End}] 写入 {Count} 条",
                    chunkIdx, chunkIdx + chunk.Count - 1, chunk.Count);
            }

            _logger.LogInformation("SqlBulkCopy共写入粉尘数据: {Count} 条", totalInserted);
            return totalInserted;
        }

        public async Task<int> BulkInsertFungiSensorDataAsync(List<FungiSensorData> dataList, CancellationToken ct = default)
        {
            if (dataList == null || dataList.Count == 0) return 0;

            int totalInserted = 0;

            if (dataList.Count < 500)
            {
                _context.ChangeTracker.AutoDetectChangesEnabled = false;
                _context.FungiSensorData.AddRange(dataList);
                totalInserted = await _context.SaveChangesAsync(ct);
                _context.ChangeTracker.AutoDetectChangesEnabled = true;
                return totalInserted;
            }

            const int chunkSize = 3000;
            for (int chunkIdx = 0; chunkIdx < dataList.Count; chunkIdx += chunkSize)
            {
                var chunk = dataList.GetRange(chunkIdx, Math.Min(chunkSize, dataList.Count - chunkIdx));

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                using var bulkCopy = new SqlBulkCopy(connection)
                {
                    DestinationTableName = "[dbo].[FungiSensorData]",
                    BatchSize = chunk.Count,
                    BulkCopyTimeout = 120,
                    EnableStreaming = true
                };

                var columnMappings = new Dictionary<string, string>
                {
                    ["SensorId"] = "SensorId",
                    ["TextileId"] = "TextileId",
                    ["ReadingTime"] = "ReadingTime",
                    ["SporeCount"] = "SporeCount",
                    ["FungiCFU"] = "FungiCFU",
                    ["Temperature"] = "Temperature",
                    ["Humidity"] = "Humidity",
                    ["DominantFungiType"] = "DominantFungiType",
                    ["SignalStrength"] = "SignalStrength",
                    ["SensorStatus"] = "SensorStatus"
                };

                foreach (var map in columnMappings)
                    bulkCopy.ColumnMappings.Add(map.Key, map.Value);

                using var reader = new EntityListDataReader<FungiSensorData>(chunk, columnMappings.Keys.ToList());
                await bulkCopy.WriteToServerAsync(reader, ct);
                totalInserted += chunk.Count;
            }

            return totalInserted;
        }

        public async Task<int> BatchInsertHoleMarkersAsync(List<HoleMarker> markers, CancellationToken ct = default)
        {
            if (markers == null || markers.Count == 0) return 0;

            int totalInserted = 0;
            const int batchSize = 200;

            for (int i = 0; i < markers.Count; i += batchSize)
            {
                var batch = markers.GetRange(i, Math.Min(batchSize, markers.Count - i));
                var parameterList = new List<object>();
                var queryParts = new List<string>();

                for (int j = 0; j < batch.Count; j++)
                {
                    var m = batch[j];
                    var pTextileId = m.TextileId;
                    var pSensorId = m.SensorId;
                    var pImgId = m.ImageId ?? (object)DBNull.Value;
                    var pRelX = m.RelativeX;
                    var pRelY = m.RelativeY;
                    var pRadius = m.RadiusMm;
                    var pPerim = m.PerimeterMm ?? (object)DBNull.Value;
                    var pArea = m.AreaMm2 ?? (object)DBNull.Value;
                    var pSev = m.SeverityLevel;
                    var pDetected = m.DetectedAt;
                    var pStatus = m.Status;
                    var pNotes = m.Notes ?? (object)DBNull.Value;

                    queryParts.Add(
                        "(@tid" + j + ", @sid" + j + ", @img" + j + ", @rx" + j + ", @ry" + j +
                        ", @r" + j + ", @p" + j + ", @a" + j + ", @s" + j +
                        ", @d" + j + ", @st" + j + ", @n" + j + ")");

                    parameterList.Add(pTextileId);
                    parameterList.Add(pSensorId);
                    parameterList.Add(pImgId);
                    parameterList.Add(pRelX);
                    parameterList.Add(pRelY);
                    parameterList.Add(pRadius);
                    parameterList.Add(pPerim);
                    parameterList.Add(pArea);
                    parameterList.Add(pSev);
                    parameterList.Add(pDetected);
                    parameterList.Add(pStatus);
                    parameterList.Add(pNotes);
                }

                var sql =
@"INSERT INTO [dbo].[HoleMarkers]
    ([TextileId], [SensorId], [ImageId], [RelativeX], [RelativeY],
     [RadiusMm], [PerimeterMm], [AreaMm2], [SeverityLevel],
     [DetectedAt], [Status], [Notes])
VALUES " + string.Join(", ", queryParts) + ";";

                totalInserted += await _context.Database.ExecuteSqlRawAsync(sql, parameterList, ct);
            }

            _logger.LogInformation("HoleMarkers批量插入完成，共 {Count} 条", totalInserted);
            return totalInserted;
        }
    }

    public class EntityListDataReader<T> : IDataReader
    {
        private readonly IEnumerator<T> _enumerator;
        private readonly List<PropertyInfo> _props;
        private bool _disposed;
        private readonly int _rowCount;
        private int _currentRow = -1;

        public EntityListDataReader(List<T> list, List<string> propertyNames)
        {
            _enumerator = list.GetEnumerator();
            _rowCount = list.Count;
            var propsAll = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            _props = propertyNames.Select(n => propsAll.FirstOrDefault(p => p.Name == n)!)
                                   .Where(p => p != null)
                                   .ToList();
        }

        public object this[int i] => GetValue(i);
        public object this[string name] => GetValue(GetOrdinal(name));
        public int Depth => 0;
        public bool IsClosed => _disposed;
        public int RecordsAffected => _rowCount;
        public int FieldCount => _props.Count;

        public void Close() => Dispose();
        public void Dispose() { _disposed = true; _enumerator.Dispose(); }

        public bool Read() { _currentRow++; return _enumerator.MoveNext(); }
        public bool NextResult() => false;
        public DataTable GetSchemaTable() => throw new NotSupportedException();

        public bool GetBoolean(int i) => Convert.ToBoolean(GetValue(i));
        public byte GetByte(int i) => Convert.ToByte(GetValue(i));
        public char GetChar(int i) => Convert.ToChar(GetValue(i));
        public DateTime GetDateTime(int i) => Convert.ToDateTime(GetValue(i));
        public decimal GetDecimal(int i) => Convert.ToDecimal(GetValue(i));
        public double GetDouble(int i) => Convert.ToDouble(GetValue(i));
        public float GetFloat(int i) => Convert.ToSingle(GetValue(i));
        public Guid GetGuid(int i) => (Guid)GetValue(i);
        public short GetInt16(int i) => Convert.ToInt16(GetValue(i));
        public int GetInt32(int i) => Convert.ToInt32(GetValue(i));
        public long GetInt64(int i) => Convert.ToInt64(GetValue(i));
        public string GetString(int i) => Convert.ToString(GetValue(i)) ?? "";
        public int GetValues(object[] values) { for (int i = 0; i < _props.Count; i++) values[i] = GetValue(i); return _props.Count; }

        public long GetBytes(int i, long fi, byte[]? buf, int bi, int len) => throw new NotSupportedException();
        public long GetChars(int i, long fi, char[]? buf, int bi, int len) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public string GetDataTypeName(int i) => _props[i].PropertyType.Name;
        public Type GetFieldType(int i) => _props[i].PropertyType;
        public string GetName(int i) => _props[i].Name;
        public int GetOrdinal(string name) => _props.FindIndex(p => p.Name == name);
        public object GetValue(int i) { var v = _props[i].GetValue(_enumerator.Current); return v ?? DBNull.Value; }
        public bool IsDBNull(int i) => GetValue(i) == DBNull.Value;
    }
}
