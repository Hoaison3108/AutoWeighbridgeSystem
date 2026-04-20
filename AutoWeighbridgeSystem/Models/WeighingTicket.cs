using AutoWeighbridgeSystem.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoWeighbridgeSystem.Models
{
    public class WeighingTicket
    {
        [Key]
        [Required]
        [StringLength(20)]
        public string TicketID { get; set; } // Định dạng: yyMMddxxxx

        public int? VehicleId { get; set; }
        [ForeignKey("VehicleId")]
        public virtual Vehicle Vehicle { get; set; }

        // Snapshot dữ liệu
        public string LicensePlate { get; set; }
        public string CustomerName { get; set; }
        public string ProductName { get; set; }

        // Số liệu cân (Lưu cột thực để báo cáo nhanh)
        [Column(TypeName = "decimal(18, 2)")]
        public decimal GrossWeight { get; set; }
        [Column(TypeName = "decimal(18, 2)")]
        public decimal TareWeight { get; set; }
        [Column(TypeName = "decimal(18, 2)")]
        public decimal NetWeight { get; set; }

        public DateTime TimeIn { get; set; }
        public DateTime? TimeOut { get; set; }

        public bool IsVoid { get; set; } = false;
        public string? VoidReason { get; set; }
        public string? Note { get; set; }

        [NotMapped]
        public string Status => IsVoid ? "Bị Hủy" : (TimeOut.HasValue ? "Đã Hoàn Thành" : "Đợi cân lần 2");

        [NotMapped]
        public string StatusColor => IsVoid ? "#FF5252" : (TimeOut.HasValue ? "#00E676" : "#FFEB3B");
    }
}
