using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;

namespace AutoWeighbridgeSystem.Data.Configuration
{
    public class WeighingTicketConfiguration : IEntityTypeConfiguration<WeighingTicket>
    {
        public void Configure(EntityTypeBuilder<WeighingTicket> builder)
        {
            // 1. CẤU HÌNH KHÓA CHÍNH (String ID: yyMMdd-xxx)
            builder.HasKey(t => t.TicketID);
            builder.Property(t => t.TicketID).HasMaxLength(20);

            // 2. CHỐT CHẶN DỮ LIỆU (Global Query Filter)
            // Tự động ẩn các phiếu đã hủy khỏi toàn bộ truy vấn thông thường
            builder.HasQueryFilter(t => !t.IsVoid);

            // 3. TỐI ƯU HÓA TRA CỨU (Indexing)
            builder.HasIndex(t => t.LicensePlate);
            builder.HasIndex(t => t.TimeIn);
            builder.HasIndex(t => t.IsVoid);

            // 4. CẤU HÌNH QUAN HỆ (Foreign Keys)
            // Lược bỏ UserID theo yêu cầu tối giản của bạn
            builder.HasOne(t => t.Vehicle)
                   .WithMany()
                   .HasForeignKey(t => t.VehicleId)
                   .OnDelete(DeleteBehavior.Restrict);

            // 5. ĐỊNH DẠNG DỮ LIỆU SỐ (Precision)
            builder.Property(t => t.GrossWeight).HasPrecision(18, 2);
            builder.Property(t => t.TareWeight).HasPrecision(18, 2);
            builder.Property(t => t.NetWeight).HasPrecision(18, 2); // Cột thực để báo cáo nhanh

            builder.Property(t => t.Note).IsRequired(false).HasMaxLength(500);
            builder.Property(t => t.VoidReason).IsRequired(false).HasMaxLength(500);

            // 6. SEED DATA MẪU (Đúng định dạng yyMMdd-xxx)
            builder.HasData(
                new WeighingTicket
                {
                    TicketID = "260412-001", // Mã phiếu theo chuẩn SonK yêu cầu
                    VehicleId = 1,
                    LicensePlate = "86C-12345",
                    CustomerName = "Máy xay 1",
                    ProductName = "Đá xô bồ",
                    GrossWeight = 35400,
                    TareWeight = 12500,
                    NetWeight = 22900, // (Gross - Tare)
                    TimeIn = DateTime.Now.AddDays(-1),
                    TimeOut = DateTime.Now.AddDays(-1).AddMinutes(15),
                    IsVoid = false
                }
            );
        }
    }
}