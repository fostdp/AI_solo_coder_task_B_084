
USE [TextileMonitoringDB]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_FrassImageCaptures_TextileId_CaptureTime_INCLUDE')
BEGIN
    DROP INDEX [IX_FrassImageCaptures_TextileId_CaptureTime_INCLUDE] ON [dbo].[FrassImageCaptures]
END
GO

CREATE NONCLUSTERED INDEX [IX_FrassImageCaptures_TextileId_CaptureTime_INCLUDE]
ON [dbo].[FrassImageCaptures]([TextileId], [CaptureTime] DESC)
INCLUDE (
    [Id], [SensorId], [AverageParticleArea], [ParticleCount],
    [EllipticityMean], [AspectRatioMean], [SolidityMean], [MeanGrayscale]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_PestClassifications_TextileId_ClassifiedAt_INCLUDE')
BEGIN
    DROP INDEX [IX_PestClassifications_TextileId_ClassifiedAt_INCLUDE] ON [dbo].[PestClassificationRecords]
END
GO

CREATE NONCLUSTERED INDEX [IX_PestClassifications_TextileId_ClassifiedAt_INCLUDE]
ON [dbo].[PestClassificationRecords]([TextileId], [ClassifiedAt] DESC)
INCLUDE (
    [Id], [PredictedSpeciesId], [PredictedSpeciesName], [Confidence],
    [EstimatedPopulationSize], [RiskSeverityScore]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_PestClassifications_Species_ClassifiedAt')
BEGIN
    DROP INDEX [IX_PestClassifications_Species_ClassifiedAt] ON [dbo].[PestClassificationRecords]
END
GO

CREATE NONCLUSTERED INDEX [IX_PestClassifications_Species_ClassifiedAt]
ON [dbo].[PestClassificationRecords]([PredictedSpeciesId], [ClassifiedAt] DESC)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_VocSensorData_TextileId_ReadingTime_INCLUDE')
BEGIN
    DROP INDEX [IX_VocSensorData_TextileId_ReadingTime_INCLUDE] ON [dbo].[VocSensorData]
END
GO

CREATE NONCLUSTERED INDEX [IX_VocSensorData_TextileId_ReadingTime_INCLUDE]
ON [dbo].[VocSensorData]([TextileId], [ReadingTime] DESC)
INCLUDE (
    [Id], [SensorId], [ToluenePPB], [XylenePPB], [EthylbenzenePPB],
    [FormaldehydePPB], [AcetaldehydePPB], [_1Octen3OlPPB], [GeosminPPT],
    [_2MethylisoborneolPPT], [TotalVolatilePPB]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_VocClassifications_MoldSpecies_ClassifiedAt_INCLUDE')
BEGIN
    DROP INDEX [IX_VocClassifications_MoldSpecies_ClassifiedAt_INCLUDE] ON [dbo].[VocClassificationRecords]
END
GO

CREATE NONCLUSTERED INDEX [IX_VocClassifications_MoldSpecies_ClassifiedAt_INCLUDE]
ON [dbo].[VocClassificationRecords]([TextileId], [ClassifiedAt] DESC, [PredictedMoldSpeciesId])
INCLUDE (
    [Confidence], [EstimatedBiomassMg], [EarlyWarningSeverity], [MycotoxinRiskIndex]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_TreatmentSessions_TextileId_CreatedAt_INCLUDE')
BEGIN
    DROP INDEX [IX_TreatmentSessions_TextileId_CreatedAt_INCLUDE] ON [dbo].[NitrogenTreatmentSessions]
END
GO

CREATE NONCLUSTERED INDEX [IX_TreatmentSessions_TextileId_CreatedAt_INCLUDE]
ON [dbo].[NitrogenTreatmentSessions]([TextileId], [CreatedAt] DESC)
INCLUDE (
    [PredictedEggMortalityRate], [PredictedLarvaeMortalityRate],
    [PredictedAdultMortalityRate], [PredictedFungiSterilityRate],
    [SessionStatus], [IsSuccessCriteriaMet], [LD99Minutes],
    [ExposureDurationMinutes], [TargetOxygenConcentrationPct]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_VulnerabilityAssessments_Priority_Rank_INCLUDE')
BEGIN
    DROP INDEX [IX_VulnerabilityAssessments_Priority_Rank_INCLUDE] ON [dbo].[VulnerabilityAssessments]
END
GO

CREATE NONCLUSTERED INDEX [IX_VulnerabilityAssessments_Priority_Rank_INCLUDE]
ON [dbo].[VulnerabilityAssessments]([PriorityId], [TopsisRank], [AssessmentDate] DESC)
INCLUDE (
    [TextileId], [TopsisScore], [RelativeClosenessCC],
    [ProjectedYearsIfNoAction], [ProjectedYearsWithAction],
    [RestorationCostEstimateCny]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

IF EXISTS (SELECT name FROM sys.indexes WHERE name = N'IX_FiberStrengthTests_TextileId_TestDate_INCLUDE')
BEGIN
    DROP INDEX [IX_FiberStrengthTests_TextileId_TestDate_INCLUDE] ON [dbo].[FiberStrengthTests]
END
GO

CREATE NONCLUSTERED INDEX [IX_FiberStrengthTests_TextileId_TestDate_INCLUDE]
ON [dbo].[FiberStrengthTests]([TextileId], [TestDate] DESC)
INCLUDE (
    [TensileStrengthRemainingPct], [CurrentBreakingLoadN],
    [ElongationAtBreakPct], [YoungModulusGpa]
)
WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF,
      DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON,
      ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO

PRINT N'功能特性索引创建完成！'
GO
