
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public class DustSensorData
    {
        [Key]
        public long Id { get; set; }

        public int SensorId { get; set; }

        public int TextileId { get; set; }

        public DateTime ReadingTime { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal? PM2_5 { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal? PM10 { get; set; }

        [Column(TypeName = "decimal(12,4)")]
        public decimal FrassDensity { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        public decimal? Temperature { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        public decimal? Humidity { get; set; }

        public int HoleCount { get; set; } = 0;

        [Column(TypeName = "decimal(12,4)")]
        public decimal HoleDensity { get; set; }

        public int? ZigBeeSignalStrength { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
