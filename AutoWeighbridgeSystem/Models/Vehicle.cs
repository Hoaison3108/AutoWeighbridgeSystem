using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoWeighbridgeSystem.Models
{
    public class Vehicle
    {
        // 1. KHÓA CHÍNH (Surrogate Key) - Tự động tăng
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int VehicleId { get; set; }

        // 2. BIỂN SỐ XE - Bắt buộc nhập, tối đa 20 ký tự
        [Required]
        [StringLength(20)]
        public string LicensePlate { get; set; }

        // 3. MÃ THẺ RFID
        [StringLength(50)]
        public string RfidCardId { get; set; }

        // 4. TRỌNG LƯỢNG BÌ
        [Column(TypeName = "decimal(18,2)")] // Định dạng chuẩn cho cân điện tử (2 số thập phân)
        public decimal TareWeight { get; set; }

        // 5. KHÓA NGOẠI
        public string CustomerId { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; }

        // BỔ SUNG: Cờ Xóa mềm (Mặc định là false)
        public bool IsDeleted { get; set; } = false;

    }
}
