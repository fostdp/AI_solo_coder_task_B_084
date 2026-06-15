
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TextileMonitoring.API.Models
{
    public class FungiSensorData
    {
        [Key]
        public long Id { get; set; }

        public int SensorId { get; set; }

        public int TextileId { get; set; }

        public DateTime ReadingTime { get; set; }

        [Column(TypeName = "decimal(15,4)")]
        public decimal SporeCount { get; set; }

        [Column(TypeName = "decimal(15,4)")]
        public decimal FungiCFU { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        public decimal? Temperature { get; set; }

        [Column(TypeName = "decimal(8,2)")]
        public decimal? Humidity { get; set; }

        [MaxLength(100)]
        public string? DominantFungiType { get; set; }

        public int? ZigBeeSignalStrength { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
