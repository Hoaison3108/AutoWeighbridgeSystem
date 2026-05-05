using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoWeighbridgeSystem.Models
{
    [Table("Users")] // Chỉ định rõ tên bảng trong SQL Server
    public class User
    {
        [Key]
        public int Id { get; set; } // Nên dùng 'Id' thay vì 'UserId' để chuẩn hóa convention của Entity Framework

        [Required]
        [StringLength(50)]
        public string Username { get; set; } // Tên đăng nhập

        [Required]
        [StringLength(255)]
        public string Password { get; set; } // Mật khẩu (Nên lưu độ dài lớn để chứa chuỗi mã hóa Hash)

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } // Tên đầy đủ hiển thị trên phần mềm

        [Required]
        [StringLength(50)]
        public string Role { get; set; } // Phân quyền: "Admin", "Operator", "Viewer"

        public bool IsActive { get; set; } = true; // Cho phép khóa tài khoản tạm thời
    }
}
