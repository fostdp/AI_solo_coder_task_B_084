
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.Data.Entities;

namespace TextileMonitoring.Data;

public class TextileMonitoringDbContext : DbContext
{
    public TextileMonitoringDbContext(DbContextOptions<TextileMonitoringDbContext> options)
        : base(options)
    {
    }

    public DbSet<Textile> Textiles => Set<Textile>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<DustSensorData> DustSensorData => Set<DustSensorData>();
    public DbSet<FungiSensorData> FungiSensorData => Set<FungiSensorData>();
    public DbSet<HoleMarker> HoleMarkers => Set<HoleMarker>();
    public DbSet<MoldRegion> MoldRegions => Set<MoldRegion>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertConfig> AlertConfigs => Set<AlertConfig>();
    public DbSet<FrassImageCapture> FrassImageCaptures => Set<FrassImageCapture>();
    public DbSet<PestClassificationRecord> PestClassificationRecords => Set<PestClassificationRecord>();
    public DbSet<VocSensorData> VocSensorData => Set<VocSensorData>();
    public DbSet<VocClassificationRecord> VocClassificationRecords => Set<VocClassificationRecord>();
    public DbSet<NitrogenTreatmentSession> NitrogenTreatmentSessions => Set<NitrogenTreatmentSession>();
    public DbSet<FiberStrengthTest> FiberStrengthTests => Set<FiberStrengthTest>();
    public DbSet<VulnerabilityAssessment> VulnerabilityAssessments => Set<VulnerabilityAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Textile>(entity =>
        {
            entity.HasIndex(e => e.Dynasty);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Name).IsUnique(false);
            entity.Property(e => e.AreaCm2)
                .HasComputedColumnSql("[WidthCm] * [HeightCm]");
        });

        modelBuilder.Entity<Sensor>(entity =>
        {
            entity.HasIndex(e => e.SensorType);
            entity.HasIndex(e => e.TextileId);
            entity.HasIndex(e => e.SensorCode).IsUnique();
            entity.HasOne(s => s.Textile)
                .WithMany(t => t.Sensors)
                .HasForeignKey(s => s.TextileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DustSensorData>(entity =>
        {
            entity.HasIndex(e => new { e.SensorId, e.ReadingTime });
            entity.HasIndex(e => new { e.TextileId, e.ReadingTime });
        });

        modelBuilder.Entity<FungiSensorData>(entity =>
        {
            entity.HasIndex(e => new { e.SensorId, e.ReadingTime });
            entity.HasIndex(e => new { e.TextileId, e.ReadingTime });
        });

        modelBuilder.Entity<HoleMarker>(entity =>
        {
            entity.HasIndex(e => e.TextileId);
            entity.HasOne(h => h.Textile)
                .WithMany(t => t.HoleMarkers)
                .HasForeignKey(h => h.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MoldRegion>(entity =>
        {
            entity.HasIndex(e => e.TextileId);
            entity.HasOne(m => m.Textile)
                .WithMany(t => t.MoldRegions)
                .HasForeignKey(m => m.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Prediction>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.PredictionDate });
            entity.HasOne(p => p.Textile)
                .WithMany(t => t.Predictions)
                .HasForeignKey(p => p.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.CreatedAt });
            entity.HasIndex(e => e.AlertLevel);
            entity.HasIndex(e => new { e.Resolved, e.CreatedAt });
            entity.HasOne(a => a.Textile)
                .WithMany(t => t.Alerts)
                .HasForeignKey(a => a.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertConfig>(entity =>
        {
            entity.HasIndex(e => e.ConfigKey).IsUnique();
        });

        modelBuilder.Entity<FrassImageCapture>(entity =>
        {
            entity.HasIndex(e => new { e.SensorId, e.CaptureTime });
            entity.HasIndex(e => new { e.TextileId, e.CaptureTime });
            entity.HasOne(e => e.Textile)
                .WithMany(t => t.FrassImageCaptures)
                .HasForeignKey(e => e.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PestClassificationRecord>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.ClassifiedAt });
            entity.HasIndex(e => new { e.PredictedSpeciesId, e.ClassifiedAt });
            entity.HasOne(e => e.Textile)
                .WithMany(t => t.PestClassificationRecords)
                .HasForeignKey(e => e.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VocSensorData>(entity =>
        {
            entity.HasIndex(e => new { e.SensorId, e.ReadingTime });
            entity.HasIndex(e => new { e.TextileId, e.ReadingTime });
        });

        modelBuilder.Entity<VocClassificationRecord>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.ClassifiedAt });
            entity.HasIndex(e => new { e.PredictedMoldSpeciesId, e.ClassifiedAt });
            entity.HasOne(e => e.Textile)
                .WithMany(t => t.VocClassificationRecords)
                .HasForeignKey(e => e.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NitrogenTreatmentSession>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.CreatedAt });
            entity.HasIndex(e => e.SessionStatus);
            entity.HasOne(e => e.Textile)
                .WithMany(t => t.NitrogenTreatmentSessions)
                .HasForeignKey(e => e.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FiberStrengthTest>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.TestDate });
            entity.HasOne(e => e.Textile)
                .WithMany(t => t.FiberStrengthTests)
                .HasForeignKey(e => e.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VulnerabilityAssessment>(entity =>
        {
            entity.HasIndex(e => new { e.TextileId, e.AssessmentDate });
            entity.HasIndex(e => e.PriorityId);
            entity.HasOne(e => e.Textile)
                .WithMany(t => t.VulnerabilityAssessments)
                .HasForeignKey(e => e.TextileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
