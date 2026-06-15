
-- ============================================================================
-- SQL Server 分区表配置 - 按月分区
-- 适用于 SensorData、AlertLog、PredictionLog 等时间序列表
-- ============================================================================

USE [TextileMonitoringDB];
GO

-- ============================================================================
-- 1. 分区函数 - 按月分区，支持 2024-01 至 2026-12 共 36 个月
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.partition_functions WHERE name = 'PF_TextileMonthly')
BEGIN
    CREATE PARTITION FUNCTION [PF_TextileMonthly] (DATETIME2)
    AS RANGE RIGHT FOR VALUES (
        '2024-02-01T00:00:00.000', '2024-03-01T00:00:00.000', '2024-04-01T00:00:00.000',
        '2024-05-01T00:00:00.000', '2024-06-01T00:00:00.000', '2024-07-01T00:00:00.000',
        '2024-08-01T00:00:00.000', '2024-09-01T00:00:00.000', '2024-10-01T00:00:00.000',
        '2024-11-01T00:00:00.000', '2024-12-01T00:00:00.000',
        '2025-01-01T00:00:00.000', '2025-02-01T00:00:00.000', '2025-03-01T00:00:00.000',
        '2025-04-01T00:00:00.000', '2025-05-01T00:00:00.000', '2025-06-01T00:00:00.000',
        '2025-07-01T00:00:00.000', '2025-08-01T00:00:00.000', '2025-09-01T00:00:00.000',
        '2025-10-01T00:00:00.000', '2025-11-01T00:00:00.000', '2025-12-01T00:00:00.000',
        '2026-01-01T00:00:00.000', '2026-02-01T00:00:00.000', '2026-03-01T00:00:00.000',
        '2026-04-01T00:00:00.000', '2026-05-01T00:00:00.000', '2026-06-01T00:00:00.000',
        '2026-07-01T00:00:00.000', '2026-08-01T00:00:00.000', '2026-09-01T00:00:00.000',
        '2026-10-01T00:00:00.000', '2026-11-01T00:00:00.000', '2026-12-01T00:00:00.000'
    );
END
GO

-- ============================================================================
-- 2. 分区方案
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.partition_schemes WHERE name = 'PS_TextileMonthly')
BEGIN
    CREATE PARTITION SCHEME [PS_TextileMonthly]
    AS PARTITION [PF_TextileMonthly]
    ALL TO ([PRIMARY]);
END
GO

-- ============================================================================
-- 3. SensorData 表（若存在则重建为分区表）
-- ============================================================================
IF OBJECT_ID('dbo.SensorData', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SensorData') AND name = 'PK_SensorData')
    BEGIN
        DROP INDEX [PK_SensorData] ON dbo.SensorData;
    END

    IF NOT EXISTS (
        SELECT * FROM sys.indexes i
        INNER JOIN sys.data_spaces ds ON i.data_space_id = ds.data_space_id
        WHERE i.object_id = OBJECT_ID('dbo.SensorData')
          AND ds.name = 'PS_TextileMonthly'
    )
    BEGIN
        CREATE CLUSTERED INDEX [PK_SensorData]
        ON dbo.SensorData ([Timestamp], [Id])
        WITH (DROP_EXISTING = OFF, ONLINE = OFF)
        ON [PS_TextileMonthly]([Timestamp]);
    END
END
GO

-- ============================================================================
-- 4. AlertLog 表
-- ============================================================================
IF OBJECT_ID('dbo.AlertLog', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AlertLog') AND name = 'PK_AlertLog')
    BEGIN
        DROP INDEX [PK_AlertLog] ON dbo.AlertLog;
    END

    IF NOT EXISTS (
        SELECT * FROM sys.indexes i
        INNER JOIN sys.data_spaces ds ON i.data_space_id = ds.data_space_id
        WHERE i.object_id = OBJECT_ID('dbo.AlertLog')
          AND ds.name = 'PS_TextileMonthly'
    )
    BEGIN
        CREATE CLUSTERED INDEX [PK_AlertLog]
        ON dbo.AlertLog ([CreatedAt], [Id])
        WITH (DROP_EXISTING = OFF, ONLINE = OFF)
        ON [PS_TextileMonthly]([CreatedAt]);
    END
END
GO

-- ============================================================================
-- 5. PredictionLog 表
-- ============================================================================
IF OBJECT_ID('dbo.PredictionLog', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PredictionLog') AND name = 'PK_PredictionLog')
    BEGIN
        DROP INDEX [PK_PredictionLog] ON dbo.PredictionLog;
    END

    IF NOT EXISTS (
        SELECT * FROM sys.indexes i
        INNER JOIN sys.data_spaces ds ON i.data_space_id = ds.data_space_id
        WHERE i.object_id = OBJECT_ID('dbo.PredictionLog')
          AND ds.name = 'PS_TextileMonthly'
    )
    BEGIN
        CREATE CLUSTERED INDEX [PK_PredictionLog]
        ON dbo.PredictionLog ([PredictedAt], [Id])
        WITH (DROP_EXISTING = OFF, ONLINE = OFF)
        ON [PS_TextileMonthly]([PredictedAt]);
    END
END
GO

-- ============================================================================
-- 6. 分区管理 - 自动扩展未来分区存储过程
-- ============================================================================
IF OBJECT_ID('dbo.sp_EnsureFuturePartitions', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_EnsureFuturePartitions;
GO

CREATE PROCEDURE dbo.sp_EnsureFuturePartitions
    @MonthsAhead INT = 3
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CurrentMonth DATETIME2 = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @BoundaryDate DATETIME2;
    DECLARE @Msg NVARCHAR(500);
    DECLARE @SQL NVARCHAR(MAX);

    DECLARE @i INT = 1;
    WHILE @i <= @MonthsAhead
    BEGIN
        SET @BoundaryDate = DATEADD(MONTH, @i, @CurrentMonth);

        IF NOT EXISTS (
            SELECT 1 FROM sys.partition_range_values prv
            INNER JOIN sys.partition_functions pf ON prv.function_id = pf.function_id
            WHERE pf.name = 'PF_TextileMonthly'
              AND prv.value = @BoundaryDate
        )
        BEGIN
            ALTER PARTITION SCHEME [PS_TextileMonthly] NEXT USED [PRIMARY];
            ALTER PARTITION FUNCTION [PF_TextileMonthly]() SPLIT RANGE (@BoundaryDate);
            SET @Msg = N'已创建新分区边界: ' + CONVERT(NVARCHAR(30), @BoundaryDate, 126);
            RAISERROR(@Msg, 0, 1) WITH NOWAIT;
        END

        SET @i = @i + 1;
    END
END
GO

-- ============================================================================
-- 7. 分区信息查询视图
-- ============================================================================
IF OBJECT_ID('dbo.vw_PartitionInfo', 'V') IS NOT NULL
    DROP VIEW dbo.vw_PartitionInfo;
GO

CREATE VIEW dbo.vw_PartitionInfo
AS
SELECT
    t.name AS TableName,
    i.name AS IndexName,
    p.partition_number AS PartitionNumber,
    prv_left.value AS LowerBoundary,
    prv_right.value AS UpperBoundary,
    p.rows AS RowCount,
    CONVERT(DECIMAL(18,2), (au.total_pages * 8.0) / 1024) AS SizeMB
FROM sys.tables t
INNER JOIN sys.indexes i ON t.object_id = i.object_id
INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
INNER JOIN sys.allocation_units au ON p.partition_id = au.container_id
LEFT JOIN sys.partition_range_values prv_left ON
    prv_left.function_id = (SELECT function_id FROM sys.partition_functions WHERE name = 'PF_TextileMonthly')
    AND prv_left.boundary_id = p.partition_number - 1
LEFT JOIN sys.partition_range_values prv_right ON
    prv_right.function_id = (SELECT function_id FROM sys.partition_functions WHERE name = 'PF_TextileMonthly')
    AND prv_right.boundary_id = p.partition_number
WHERE i.index_id IN (0, 1);
GO

PRINT 'SQL Server 按月分区表配置脚本执行完成';
GO
