using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Data
{
    /// <summary>
    /// Lớp quản lý kết nối và truy xuất Cơ sở dữ liệu (Database Context)
    /// </summary>
    public class AppDbContext : DbContext
    {
        // =========================================================================
        // 1. KHAI BÁO CÁC DBSET (Bảng trong SQL Server)
        // =========================================================================
        public DbSet<User> Users { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<WeighingTicket> WeighingTickets { get; set; }



        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }



        // =========================================================================
        // 3. CẤU HÌNH FLUENT API & DATA SEEDING (OnModelCreating)
        // =========================================================================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ---------------------------------------------------------------------
            // "MAGIC LINE": TỰ ĐỘNG NẠP CẤU HÌNH
            // ---------------------------------------------------------------------
            // Thay vì viết entity.Property(...) thủ công cho hàng chục bảng ở đây, 
            // dòng lệnh này sẽ quét toàn bộ Project và tìm các class thừa kế IEntityTypeConfiguration
            // (như UserConfiguration, ProductConfiguration,...) để áp dụng.
            // ---------------------------------------------------------------------
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

            // Ghi chú: Các đoạn code cũ như:
            // modelBuilder.Entity<Vehicle>(entity => { ... });
            // Đã được di dời vào các file Configuration tương ứng trong thư mục Data/Configurations
            // để giúp file này luôn sạch sẽ (Clean Code).
        }
    }
}
