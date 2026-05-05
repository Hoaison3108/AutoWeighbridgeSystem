using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;

namespace AutoWeighbridgeSystem.Data.Configuration
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            // 1. Khai báo Khóa chính
            builder.HasKey(u => u.Id);

            // 2. Các ràng buộc bổ sung (Fluent API)
            builder.Property(u => u.Username)
                   .IsRequired()
                   .HasMaxLength(50);

            builder.Property(u => u.FullName)
                   .IsRequired()
                   .HasMaxLength(100);

            // Đảm bảo Username là duy nhất trong toàn hệ thống
            builder.HasIndex(u => u.Username)
                   .IsUnique();

            // 3. Data Seeding (Dữ liệu mẫu)
            builder.HasData(
                new User
                {
                    Id = 1,
                    Username = "admin",
                    Password = "123", // Lưu ý: thực tế sau này nên dùng BCrypt để Hash
                    FullName = "Quản trị hệ thống",
                    Role = "Admin",
                    IsActive = true
                },
                new User
                {
                    Id = 2,
                    Username = "operator",
                    Password = "123",
                    FullName = "Nhân viên trạm cân",
                    Role = "Operator",
                    IsActive = true
                },
                new User
                {
                    Id = 3,
                    Username = "viewer",
                    Password = "123",
                    FullName = "Thanh tra khoáng sản",
                    Role = "Viewer",
                    IsActive = true
                }
            );
        }
    }
}
