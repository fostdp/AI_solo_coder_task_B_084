
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Models;

namespace TextileMonitoring.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Textile> Textiles { get; set; }
        public DbSet<Sensor> Sensors { get; set; }
        public DbSet<DustSensorData> DustSensorData { get; set; }
        public DbSet<FungiSensorData> FungiSensorData { get; set; }
        public DbSet<HoleMarker> HoleMarkers { get; set; }
        public DbSet<MoldRegion> MoldRegions { get; set; }
        public DbSet<Prediction> Predictions { get; set; }
        public DbSet<Alert> Alerts { get; set; }
        public DbSet<AlertConfig> AlertConfigs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Textile>(entity =>
            {
                entity.HasIndex(e => e.Dynasty);
                entity.HasIndex(e => e.Status);
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
        }
    }
}
