using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Data.Configuration
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            // 1. Cấu hình Khóa chính (Manual String ID)
            builder.HasKey(p => p.ProductId);

            builder.Property(p => p.ProductId)
                   .HasMaxLength(8)
                   .IsRequired()
                   .IsUnicode(false); // Thường mã ID dùng ký tự latin (ASCII) để tối ưu index

            // 2. Cấu hình các thuộc tính khác
            builder.Property(p => p.ProductName)
                   .HasMaxLength(200)
                   .IsRequired();

            // 3. Thiết lập Global Query Filter (Tự động loại bỏ sản phẩm đã xóa khi truy vấn)
            builder.HasQueryFilter(p => !p.IsDeleted);

            // 4. Seed Data mẫu (Mã sản phẩm do người dùng đặt)
            builder.HasData(
                new Product { ProductId = "XOBO", ProductName = "Đá xô bồ", IsDeleted = false }
             
            );
        }
    }
}