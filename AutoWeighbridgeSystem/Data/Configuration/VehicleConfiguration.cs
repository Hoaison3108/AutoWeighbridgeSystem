using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Data.Configuration
{
    public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
    {
        public void Configure(EntityTypeBuilder<Vehicle> builder)
        {
            builder.HasKey(v => v.VehicleId);

            // Đánh Index để tra cứu nhanh biển số
            builder.HasIndex(v => v.LicensePlate);

            // Chống trùng lặp RFID (Chỉ tính trên những xe chưa xóa và có thẻ)
            builder.HasIndex(v => v.RfidCardId).IsUnique().HasFilter("[IsDeleted] = 0 AND [RfidCardId] IS NOT NULL");

            builder.Property(v => v.TareWeight).HasColumnType("decimal(18, 2)");

            // --- KHẮC PHỤC LỖI 1: Tự động lọc xe đã xóa ---
            builder.HasQueryFilter(v => !v.IsDeleted);

            builder.HasData(
                new Vehicle { VehicleId = 1, LicensePlate = "86C-12345", RfidCardId = "1234", TareWeight = 12500, CustomerId = "MX1" }
            );
        }
    }
}
