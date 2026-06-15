
-- ============================================================================
-- 初始化数据库
-- ============================================================================

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'TextileMonitoringDB')
BEGIN
    CREATE DATABASE [TextileMonitoringDB];
END
GO

USE [TextileMonitoringDB];
GO

IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'dbo')
BEGIN
    EXEC('CREATE SCHEMA [dbo]');
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'Textiles')
BEGIN
    CREATE TABLE [dbo].[Textiles] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(200) NOT NULL,
        [Dynasty] NVARCHAR(100) NULL,
        [Location] NVARCHAR(200) NULL,
        [Material] NVARCHAR(200) NULL,
        [Condition] NVARCHAR(100) NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_Textiles] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'SensorData')
BEGIN
    CREATE TABLE [dbo].[SensorData] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [Timestamp] DATETIME2 NOT NULL,
        [SensorCode] NVARCHAR(50) NOT NULL,
        [SensorType] NVARCHAR(20) NOT NULL,
        [TextileId] INT NOT NULL,
        [Temperature] FLOAT NOT NULL,
        [Humidity] FLOAT NOT NULL,
        [PM25] FLOAT NULL,
        [PM10] FLOAT NULL,
        [FrassDensity] FLOAT NULL,
        [HoleCount] INT NULL,
        [FungiCFU] FLOAT NULL,
        [FungiSpores] FLOAT NULL,
        [DominantFungi] NVARCHAR(100) NULL,
        [RSSI] SMALLINT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_SensorData] PRIMARY KEY CLUSTERED ([Timestamp] ASC, [Id] ASC)
    );
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'AlertLog')
BEGIN
    CREATE TABLE [dbo].[AlertLog] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [Severity] NVARCHAR(20) NOT NULL,
        [Category] NVARCHAR(50) NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [Message] NVARCHAR(MAX) NULL,
        [TextileId] INT NULL,
        [SensorCode] NVARCHAR(50) NULL,
        [DingTalkSent] BIT NOT NULL DEFAULT 0,
        [EmailSent] BIT NOT NULL DEFAULT 0,
        [Resolved] BIT NOT NULL DEFAULT 0,
        [ResolvedAt] DATETIME2 NULL,
        CONSTRAINT [PK_AlertLog] PRIMARY KEY CLUSTERED ([CreatedAt] ASC, [Id] ASC)
    );
END
GO

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'PredictionLog')
BEGIN
    CREATE TABLE [dbo].[PredictionLog] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [PredictedAt] DATETIME2 NOT NULL,
        [ModelType] NVARCHAR(50) NOT NULL,
        [TextileId] INT NOT NULL,
        [InputSnapshot] NVARCHAR(MAX) NULL,
        [PredictionJson] NVARCHAR(MAX) NULL,
        [HorizonHours] INT NOT NULL,
        [RiskLevel] NVARCHAR(20) NULL,
        CONSTRAINT [PK_PredictionLog] PRIMARY KEY CLUSTERED ([PredictedAt] ASC, [Id] ASC)
    );
END
GO

PRINT '数据库初始化完成';
GO
