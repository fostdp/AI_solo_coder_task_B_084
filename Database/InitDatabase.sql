
-- =============================================
-- 古代织绣品虫蛀与霉变协同监测系统
-- 数据库初始化脚本 (SQL Server)
-- =============================================

USE [master]
GO

IF EXISTS (SELECT name FROM sys.databases WHERE name = N'TextileMonitoringDB')
BEGIN
    ALTER DATABASE [TextileMonitoringDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE
    DROP DATABASE [TextileMonitoringDB]
END
GO

CREATE DATABASE [TextileMonitoringDB]
GO

USE [TextileMonitoringDB]
GO

-- =============================================
-- 织绣品表
-- =============================================
CREATE TABLE [dbo].[Textiles] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Dynasty] NVARCHAR(50) NOT NULL,
    [Material] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(1000) NULL,
    [WidthCm] DECIMAL(10,2) NOT NULL,
    [HeightCm] DECIMAL(10,2) NOT NULL,
    [AreaCm2] AS (CAST([WidthCm] * [HeightCm] AS DECIMAL(18,2))),
    [Location] NVARCHAR(200) NOT NULL,
    [ImageUrl] NVARCHAR(500) NULL,
    [AcquisitionDate] DATETIME NULL,
    [Status] INT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_Textiles] PRIMARY KEY CLUSTERED ([Id] ASC)
)
GO

CREATE NONCLUSTERED INDEX [IX_Textiles_Dynasty] ON [dbo].[Textiles]([Dynasty])
GO

CREATE NONCLUSTERED INDEX [IX_Textiles_Status] ON [dbo].[Textiles]([Status])
GO

-- =============================================
-- 传感器表
-- =============================================
CREATE TABLE [dbo].[Sensors] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [SensorCode] NVARCHAR(50) NOT NULL,
    [SensorType] INT NOT NULL,
    [TextileId] INT NOT NULL,
    [PositionX] DECIMAL(8,4) NULL,
    [PositionY] DECIMAL(8,4) NULL,
    [InstallationDate] DATETIME NOT NULL DEFAULT GETDATE(),
    [LastCalibrationDate] DATETIME NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [ZigBeeAddress] NVARCHAR(20) NOT NULL,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_Sensors] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_Sensors_SensorCode] UNIQUE NONCLUSTERED ([SensorCode] ASC),
    CONSTRAINT [FK_Sensors_Textiles] FOREIGN KEY ([TextileId]) REFERENCES [dbo].[Textiles]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_Sensors_SensorType] ON [dbo].[Sensors]([SensorType])
GO

CREATE NONCLUSTERED INDEX [IX_Sensors_TextileId] ON [dbo].[Sensors]([TextileId])
GO

-- =============================================
-- 粉尘传感器数据表（蛀虫排泄物监测）
-- =============================================
CREATE TABLE [dbo].[DustSensorData] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [SensorId] INT NOT NULL,
    [TextileId] INT NOT NULL,
    [ReadingTime] DATETIME NOT NULL,
    [PM2_5] DECIMAL(12,4) NULL,
    [PM10] DECIMAL(12,4) NULL,
    [FrassDensity] DECIMAL(12,4) NOT NULL,
    [Temperature] DECIMAL(8,2) NULL,
    [Humidity] DECIMAL(8,2) NULL,
    [HoleCount] INT NOT NULL DEFAULT 0,
    [HoleDensity] AS (CASE WHEN [TextileId] > 0 THEN CAST([HoleCount] AS DECIMAL(12,4)) ELSE 0 END),
    [ZigBeeSignalStrength] INT NULL,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_DustSensorData] PRIMARY KEY CLUSTERED ([Id] ASC)
)
GO

CREATE NONCLUSTERED INDEX [IX_DustSensorData_SensorId_Time] ON [dbo].[DustSensorData]([SensorId], [ReadingTime] DESC)
GO

CREATE NONCLUSTERED INDEX [IX_DustSensorData_TextileId_Time] ON [dbo].[DustSensorData]([TextileId], [ReadingTime] DESC)
GO

-- =============================================
-- 真菌孢子捕捉器数据表
-- =============================================
CREATE TABLE [dbo].[FungiSensorData] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [SensorId] INT NOT NULL,
    [TextileId] INT NOT NULL,
    [ReadingTime] DATETIME NOT NULL,
    [SporeCount] DECIMAL(15,4) NOT NULL,
    [FungiCFU] DECIMAL(15,4) NOT NULL,
    [Temperature] DECIMAL(8,2) NULL,
    [Humidity] DECIMAL(8,2) NULL,
    [DominantFungiType] NVARCHAR(100) NULL,
    [ZigBeeSignalStrength] INT NULL,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_FungiSensorData] PRIMARY KEY CLUSTERED ([Id] ASC)
)
GO

CREATE NONCLUSTERED INDEX [IX_FungiSensorData_SensorId_Time] ON [dbo].[FungiSensorData]([SensorId], [ReadingTime] DESC)
GO

CREATE NONCLUSTERED INDEX [IX_FungiSensorData_TextileId_Time] ON [dbo].[FungiSensorData]([TextileId], [ReadingTime] DESC)
GO

-- =============================================
-- 虫蛀孔洞坐标表
-- =============================================
CREATE TABLE [dbo].[HoleMarkers] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [TextileId] INT NOT NULL,
    [DustDataId] BIGINT NULL,
    [PositionX] DECIMAL(10,4) NOT NULL,
    [PositionY] DECIMAL(10,4) NOT NULL,
    [RadiusMm] DECIMAL(8,4) NOT NULL,
    [DetectedTime] DATETIME NOT NULL,
    [Severity] INT NOT NULL DEFAULT 0,
    [Remarks] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_HoleMarkers] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_HoleMarkers_Textiles] FOREIGN KEY ([TextileId]) REFERENCES [dbo].[Textiles]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_HoleMarkers_TextileId] ON [dbo].[HoleMarkers]([TextileId])
GO

-- =============================================
-- 霉变区域表
-- =============================================
CREATE TABLE [dbo].[MoldRegions] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [TextileId] INT NOT NULL,
    [FungiDataId] BIGINT NULL,
    [CenterX] DECIMAL(10,4) NOT NULL,
    [CenterY] DECIMAL(10,4) NOT NULL,
    [RadiusMm] DECIMAL(8,4) NOT NULL,
    [AreaCm2] DECIMAL(12,4) NOT NULL,
    [DetectedTime] DATETIME NOT NULL,
    [Severity] INT NOT NULL DEFAULT 0,
    [FungiType] NVARCHAR(100) NULL,
    [Remarks] NVARCHAR(500) NULL,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_MoldRegions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_MoldRegions_Textiles] FOREIGN KEY ([TextileId]) REFERENCES [dbo].[Textiles]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_MoldRegions_TextileId] ON [dbo].[MoldRegions]([TextileId])
GO

-- =============================================
-- 预测结果表
-- =============================================
CREATE TABLE [dbo].[Predictions] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [TextileId] INT NOT NULL,
    [PredictionDate] DATE NOT NULL,
    [PredictionModel] INT NOT NULL,
    [HorizonDays] INT NOT NULL,
    [PredictedHoleDensity] DECIMAL(12,4) NULL,
    [PredictedFungiCFU] DECIMAL(15,4) NULL,
    [PredictedSynergyRisk] DECIMAL(12,4) NULL,
    [Confidence] DECIMAL(5,4) NULL,
    [RiskLevel] INT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_Predictions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Predictions_Textiles] FOREIGN KEY ([TextileId]) REFERENCES [dbo].[Textiles]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_Predictions_TextileId_Date] ON [dbo].[Predictions]([TextileId], [PredictionDate] DESC)
GO

-- =============================================
-- 告警表
-- =============================================
CREATE TABLE [dbo].[Alerts] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [TextileId] INT NOT NULL,
    [AlertType] INT NOT NULL,
    [AlertLevel] INT NOT NULL,
    [Title] NVARCHAR(200) NOT NULL,
    [Message] NVARCHAR(1000) NOT NULL,
    [HoleDensity] DECIMAL(12,4) NULL,
    [FungiCFU] DECIMAL(15,4) NULL,
    [SynergyRisk] DECIMAL(12,4) NULL,
    [Threshold] DECIMAL(12,4) NOT NULL,
    [ActualValue] DECIMAL(15,4) NOT NULL,
    [DingTalkPushed] BIT NOT NULL DEFAULT 0,
    [EmailPushed] BIT NOT NULL DEFAULT 0,
    [PushedAt] DATETIME NULL,
    [Acknowledged] BIT NOT NULL DEFAULT 0,
    [AcknowledgedBy] NVARCHAR(100) NULL,
    [AcknowledgedAt] DATETIME NULL,
    [Resolved] BIT NOT NULL DEFAULT 0,
    [ResolvedAt] DATETIME NULL,
    [Remarks] NVARCHAR(1000) NULL,
    [CreatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_Alerts] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Alerts_Textiles] FOREIGN KEY ([TextileId]) REFERENCES [dbo].[Textiles]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_Alerts_TextileId_Time] ON [dbo].[Alerts]([TextileId], [CreatedAt] DESC)
GO

CREATE NONCLUSTERED INDEX [IX_Alerts_AlertLevel] ON [dbo].[Alerts]([AlertLevel])
GO

CREATE NONCLUSTERED INDEX [IX_Alerts_Resolved] ON [dbo].[Alerts]([Resolved], [CreatedAt] DESC)
GO

-- =============================================
-- 告警配置表
-- =============================================
CREATE TABLE [dbo].[AlertConfigs] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [ConfigKey] NVARCHAR(100) NOT NULL,
    [ConfigValue] NVARCHAR(500) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [UpdatedAt] DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT [PK_AlertConfigs] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_AlertConfigs_Key] UNIQUE NONCLUSTERED ([ConfigKey] ASC)
)
GO

-- =============================================
-- 初始化告警配置
-- =============================================
INSERT INTO [dbo].[AlertConfigs] ([ConfigKey], [ConfigValue], [Description]) VALUES
(N'HoleDensityThreshold', N'5.0', N'虫蛀孔洞密度告警阈值 (个/100cm²)'),
(N'FungiCFUThreshold', N'300.0', N'霉菌告警阈值 (CFU/g)'),
(N'SynergyRiskThreshold', N'50.0', N'协同风险指数告警阈值'),
(N'WarningHoleDensity', N'3.0', N'虫蛀预警密度 (个/100cm²)'),
(N'WarningFungiCFU', N'200.0', N'霉菌预警阈值 (CFU/g)'),
(N'DingTalkWebhook', N'https://oapi.dingtalk.com/robot/send?access_token=YOUR_ACCESS_TOKEN', N'钉钉机器人Webhook地址'),
(N'DingTalkSecret', N'YOUR_SECRET', N'钉钉机器人加签密钥'),
(N'SmtpHost', N'smtp.example.com', N'SMTP服务器地址'),
(N'SmtpPort', N'587', N'SMTP服务器端口'),
(N'SmtpUser', N'alerts@example.com', N'SMTP发件账号'),
(N'SmtpPassword', N'YOUR_PASSWORD', N'SMTP发件密码'),
(N'SmtpEnableSsl', N'true', N'SMTP启用SSL'),
(N'EmailRecipients', N'admin@example.com;curator@example.com', N'告警邮件收件人(分号分隔)')
GO

-- =============================================
-- 初始化织绣品数据 (100件明清织绣)
-- =============================================
SET NOCOUNT ON
GO

DECLARE @Dynasties TABLE (Name NVARCHAR(50))
INSERT INTO @Dynasties VALUES (N'明代早期'), (N'明代中期'), (N'明代晚期'), (N'清代早期'), (N'清代中期'), (N'清代晚期')

DECLARE @Materials TABLE (Name NVARCHAR(100))
INSERT INTO @Materials VALUES (N'桑蚕丝缎'), (N'云锦'), (N'蜀锦'), (N'宋锦'), (N'缂丝'), (N'织金锦'), (N'妆花缎'), (N'素罗'), (N'花绫'), (N'刺绣')

DECLARE @Locations TABLE (Name NVARCHAR(200))
INSERT INTO @Locations VALUES 
(N'A区展柜01'), (N'A区展柜02'), (N'A区展柜03'), (N'A区展柜04'), (N'A区展柜05'),
(N'B区展柜01'), (N'B区展柜02'), (N'B区展柜03'), (N'B区展柜04'), (N'B区展柜05'),
(N'C区展柜01'), (N'C区展柜02'), (N'C区展柜03'), (N'C区展柜04'), (N'C区展柜05'),
(N'D区库房01'), (N'D区库房02'), (N'D区库房03'), (N'D区库房04'), (N'D区库房05')

DECLARE @i INT = 1
DECLARE @NamePrefixes NVARCHAR(100) = N'云龙,团花,缠枝,花鸟,人物,山水,博古,龙凤,麒麟,狮子,牡丹,莲花,菊花,梅花,竹石'

WHILE @i <= 100
BEGIN
    DECLARE @Name NVARCHAR(200)
    DECLARE @Dynasty NVARCHAR(50)
    DECLARE @Material NVARCHAR(100)
    DECLARE @Location NVARCHAR(200)
    DECLARE @Width DECIMAL(10,2)
    DECLARE @Height DECIMAL(10,2)
    DECLARE @AcquisitionDate DATETIME

    SET @Name = CONCAT(
        (SELECT TOP 1 value FROM STRING_SPLIT(@NamePrefixes, N',') ORDER BY NEWID()),
        N'纹',
        (SELECT TOP 1 Name FROM @Materials ORDER BY NEWID()),
        FORMAT(@i, N'000')
    )
    
    SET @Dynasty = (SELECT TOP 1 Name FROM @Dynasties ORDER BY NEWID())
    SET @Material = (SELECT TOP 1 Name FROM @Materials ORDER BY NEWID())
    SET @Location = (SELECT TOP 1 Name FROM @Locations ORDER BY NEWID())
    SET @Width = 30 + (ABS(CHECKSUM(NEWID())) % 120) + (ABS(CHECKSUM(NEWID())) % 100) / 10.0
    SET @Height = 40 + (ABS(CHECKSUM(NEWID())) % 180) + (ABS(CHECKSUM(NEWID())) % 100) / 10.0
    SET @AcquisitionDate = DATEADD(DAY, -(ABS(CHECKSUM(NEWID())) % 10950), GETDATE())

    INSERT INTO [dbo].[Textiles] ([Name], [Dynasty], [Material], [Description], [WidthCm], [HeightCm], [Location], [ImageUrl], [AcquisitionDate], [Status])
    VALUES (
        @Name,
        @Dynasty,
        @Material,
        CONCAT(@Dynasty, N'时期', @Material, N'精品，图案精美，保存状况良好。编号：TX-', FORMAT(@i, '0000')),
        @Width,
        @Height,
        @Location,
        CONCAT(N'/images/textile_', FORMAT(@i, '000'), N'.jpg'),
        @AcquisitionDate,
        CASE WHEN (ABS(CHECKSUM(NEWID())) % 100) < 85 THEN 0 ELSE (ABS(CHECKSUM(NEWID())) % 3) + 1 END
    )

    SET @i = @i + 1
END
GO

-- =============================================
-- 初始化传感器数据 (30台粉尘+20台真菌孢子捕捉器)
-- =============================================

-- 粉尘传感器 (30台，监测蛀虫排泄物)
DECLARE @DustCount INT = 1
WHILE @DustCount <= 30
BEGIN
    DECLARE @DustTextileId INT
    DECLARE @DustPosX DECIMAL(8,4)
    DECLARE @DustPosY DECIMAL(8,4)
    DECLARE @DustZigBee NVARCHAR(20)
    
    SET @DustTextileId = 1 + (ABS(CHECKSUM(NEWID())) % 100)
    SET @DustPosX = (ABS(CHECKSUM(NEWID())) % 10000) / 100.0
    SET @DustPosY = (ABS(CHECKSUM(NEWID())) % 10000) / 100.0
    SET @DustZigBee = CONCAT(N'0x', CONVERT(VARCHAR(16), ABS(CHECKSUM(NEWID())) % 65535, 16))
    
    INSERT INTO [dbo].[Sensors] ([SensorCode], [SensorType], [TextileId], [PositionX], [PositionY], [ZigBeeAddress])
    VALUES (
        CONCAT(N'DUS-', FORMAT(@DustCount, '000')),
        1,
        @DustTextileId,
        @DustPosX,
        @DustPosY,
        @DustZigBee
    )
    
    SET @DustCount = @DustCount + 1
END
GO

-- 真菌孢子捕捉器 (20台)
DECLARE @FungiCount INT = 1
WHILE @FungiCount <= 20
BEGIN
    DECLARE @FungiTextileId INT
    DECLARE @FungiPosX DECIMAL(8,4)
    DECLARE @FungiPosY DECIMAL(8,4)
    DECLARE @FungiZigBee NVARCHAR(20)
    
    SET @FungiTextileId = 1 + (ABS(CHECKSUM(NEWID())) % 100)
    SET @FungiPosX = (ABS(CHECKSUM(NEWID())) % 10000) / 100.0
    SET @FungiPosY = (ABS(CHECKSUM(NEWID())) % 10000) / 100.0
    SET @FungiZigBee = CONCAT(N'0x', CONVERT(VARCHAR(16), ABS(CHECKSUM(NEWID())) % 65535, 16))
    
    INSERT INTO [dbo].[Sensors] ([SensorCode], [SensorType], [TextileId], [PositionX], [PositionY], [ZigBeeAddress])
    VALUES (
        CONCAT(N'FUN-', FORMAT(@FungiCount, '000')),
        2,
        @FungiTextileId,
        @FungiPosX,
        @FungiPosY,
        @FungiZigBee
    )
    
    SET @FungiCount = @FungiCount + 1
END
GO

-- =============================================
-- 生成历史传感器数据 (过去30天，每4小时上报一次)
-- =============================================

-- 粉尘传感器历史数据
DECLARE @Days INT = 30
DECLARE @Hours INT = 0

DECLARE DustCursor CURSOR FOR SELECT Id, TextileId FROM [dbo].[Sensors] WHERE SensorType = 1
DECLARE @DustSensorId INT, @DustTextileIdData INT

OPEN DustCursor
FETCH NEXT FROM DustCursor INTO @DustSensorId, @DustTextileIdData

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @CurrentTime DATETIME = DATEADD(HOUR, -@Days * 24, GETDATE())
    DECLARE @BaseFrass DECIMAL(12,4) = 0.5 + (ABS(CHECKSUM(NEWID())) % 500) / 100.0
    DECLARE @BaseHoles INT = ABS(CHECKSUM(NEWID())) % 10
    
    WHILE @CurrentTime <= GETDATE()
    BEGIN
        DECLARE @FrassVariation DECIMAL(12,4) = (ABS(CHECKSUM(NEWID())) % 200) / 100.0 - 1.0
        DECLARE @Temp DECIMAL(8,2) = 18.0 + (ABS(CHECKSUM(NEWID())) % 100) / 10.0
        DECLARE @Hum DECIMAL(8,2) = 45.0 + (ABS(CHECKSUM(NEWID())) % 250) / 10.0
        DECLARE @HoleAdd INT = CASE WHEN (ABS(CHECKSUM(NEWID())) % 100) < 10 THEN 1 ELSE 0 END
        
        SET @BaseFrass = @BaseFrass + @FrassVariation
        IF @BaseFrass < 0.1 SET @BaseFrass = 0.1
        IF @BaseFrass > 15 SET @BaseFrass = 15
        
        SET @BaseHoles = @BaseHoles + @HoleAdd
        
        INSERT INTO [dbo].[DustSensorData] ([SensorId], [TextileId], [ReadingTime], [PM2_5], [PM10], [FrassDensity], [Temperature], [Humidity], [HoleCount], [ZigBeeSignalStrength])
        VALUES (
            @DustSensorId,
            @DustTextileIdData,
            @CurrentTime,
            @BaseFrass * 2.5,
            @BaseFrass * 4.0,
            @BaseFrass,
            @Temp,
            @Hum,
            @BaseHoles,
            -(40 + (ABS(CHECKSUM(NEWID())) % 40))
        )
        
        SET @CurrentTime = DATEADD(HOUR, 4, @CurrentTime)
    END
    
    FETCH NEXT FROM DustCursor INTO @DustSensorId, @DustTextileIdData
END

CLOSE DustCursor
DEALLOCATE DustCursor
GO

-- 真菌传感器历史数据
DECLARE FungiCursor CURSOR FOR SELECT Id, TextileId FROM [dbo].[Sensors] WHERE SensorType = 2
DECLARE @FungiSensorId INT, @FungiTextileIdData INT

OPEN FungiCursor
FETCH NEXT FROM FungiCursor INTO @FungiSensorId, @FungiTextileIdData

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @FungiCurrentTime DATETIME = DATEADD(HOUR, -@Days * 24, GETDATE())
    DECLARE @BaseCFU DECIMAL(15,4) = 20.0 + (ABS(CHECKSUM(NEWID())) % 2000) / 10.0
    
    WHILE @FungiCurrentTime <= GETDATE()
    BEGIN
        DECLARE @CFUVariation DECIMAL(15,4) = (ABS(CHECKSUM(NEWID())) % 200) / 10.0 - 10.0
        DECLARE @FungiTemp DECIMAL(8,2) = 18.0 + (ABS(CHECKSUM(NEWID())) % 100) / 10.0
        DECLARE @FungiHum DECIMAL(8,2) = 45.0 + (ABS(CHECKSUM(NEWID())) % 250) / 10.0
        DECLARE @DominantFungi NVARCHAR(100)
        
        SET @BaseCFU = @BaseCFU + @CFUVariation
        IF @BaseCFU < 5 SET @BaseCFU = 5
        IF @BaseCFU > 800 SET @BaseCFU = 800
        
        SELECT TOP 1 @DominantFungi = fungiType FROM (VALUES
            (N'曲霉属'), (N'青霉属'), (N'毛霉属'), (N'根霉属'), (N'木霉属'), (N'未知')
        ) AS F(fungiType) ORDER BY NEWID()
        
        INSERT INTO [dbo].[FungiSensorData] ([SensorId], [TextileId], [ReadingTime], [SporeCount], [FungiCFU], [Temperature], [Humidity], [DominantFungiType], [ZigBeeSignalStrength])
        VALUES (
            @FungiSensorId,
            @FungiTextileIdData,
            @FungiCurrentTime,
            @BaseCFU * 12.5,
            @BaseCFU,
            @FungiTemp,
            @FungiHum,
            @DominantFungi,
            -(40 + (ABS(CHECKSUM(NEWID())) % 40))
        )
        
        SET @FungiCurrentTime = DATEADD(HOUR, 4, @FungiCurrentTime)
    END
    
    FETCH NEXT FROM FungiCursor INTO @FungiSensorId, @FungiTextileIdData
END

CLOSE FungiCursor
DEALLOCATE FungiCursor
GO

-- =============================================
-- 生成初始虫蛀孔洞和霉变区域样本数据
-- =============================================

-- 为部分织绣品生成虫蛀孔洞标记
DECLARE @TextileWithHoles INT = 1
WHILE @TextileWithHoles <= 100
BEGIN
    IF (ABS(CHECKSUM(NEWID())) % 100) < 40
    BEGIN
        DECLARE @HoleNum INT = 1 + (ABS(CHECKSUM(NEWID())) % 8)
        DECLARE @HoleIdx INT = 0
        
        WHILE @HoleIdx < @HoleNum
        BEGIN
            INSERT INTO [dbo].[HoleMarkers] ([TextileId], [PositionX], [PositionY], [RadiusMm], [DetectedTime], [Severity])
            VALUES (
                @TextileWithHoles,
                (ABS(CHECKSUM(NEWID())) % 10000) / 100.0,
                (ABS(CHECKSUM(NEWID())) % 10000) / 100.0,
                0.5 + (ABS(CHECKSUM(NEWID())) % 40) / 10.0,
                DATEADD(HOUR, -(ABS(CHECKSUM(NEWID())) % 720), GETDATE()),
                (ABS(CHECKSUM(NEWID())) % 3)
            )
            SET @HoleIdx = @HoleIdx + 1
        END
    END
    
    SET @TextileWithHoles = @TextileWithHoles + 1
END
GO

-- 为部分织绣品生成霉变区域
DECLARE @TextileWithMold INT = 1
WHILE @TextileWithMold <= 100
BEGIN
    IF (ABS(CHECKSUM(NEWID())) % 100) < 35
    BEGIN
        DECLARE @MoldNum INT = 1 + (ABS(CHECKSUM(NEWID())) % 5)
        DECLARE @MoldIdx INT = 0
        
        WHILE @MoldIdx < @MoldNum
        BEGIN
            DECLARE @MoldRadius DECIMAL(8,4) = 1.0 + (ABS(CHECKSUM(NEWID())) % 80) / 10.0
            DECLARE @MoldArea DECIMAL(12,4) = PI() * @MoldRadius * @MoldRadius / 100.0
            
            INSERT INTO [dbo].[MoldRegions] ([TextileId], [CenterX], [CenterY], [RadiusMm], [AreaCm2], [DetectedTime], [Severity], [FungiType])
            VALUES (
                @TextileWithMold,
                (ABS(CHECKSUM(NEWID())) % 10000) / 100.0,
                (ABS(CHECKSUM(NEWID())) % 10000) / 100.0,
                @MoldRadius,
                @MoldArea,
                DATEADD(HOUR, -(ABS(CHECKSUM(NEWID())) % 720), GETDATE()),
                (ABS(CHECKSUM(NEWID())) % 3),
                (SELECT TOP 1 F FROM (VALUES (N'曲霉属'), (N'青霉属'), (N'毛霉属')) AS T(F) ORDER BY NEWID())
            )
            SET @MoldIdx = @MoldIdx + 1
        END
    END
    
    SET @TextileWithMold = @TextileWithMold + 1
END
GO

PRINT N'数据库初始化完成！'
PRINT N'织绣品数量: ' + CAST((SELECT COUNT(*) FROM [dbo].[Textiles]) AS NVARCHAR)
PRINT N'粉尘传感器数量: ' + CAST((SELECT COUNT(*) FROM [dbo].[Sensors] WHERE SensorType = 1) AS NVARCHAR)
PRINT N'真菌孢子捕捉器数量: ' + CAST((SELECT COUNT(*) FROM [dbo].[Sensors] WHERE SensorType = 2) AS NVARCHAR)
GO
