using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoWeighbridgeSystem.Models
{
    [Table("Products")]
    public class Product
    {
        [Key] // Xác định đây là khóa chính
        [Required]
        [MaxLength(8)] // Giới hạn độ dài mã sản phẩm để tối ưu Index trong DB
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // QUAN TRỌNG: DB sẽ không tự sinh ID
        public string ProductId { get; set; }

        [Required]
        [MaxLength(200)]
        public string ProductName { get; set; }

        public bool IsDeleted { get; set; } = false;
    }
}