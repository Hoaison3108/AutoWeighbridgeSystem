using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace AutoWeighbridgeSystem.Data
{
    /// <summary>
    /// Factory dành riêng cho Design-Time (Entity Framework Core Tools).
    /// Giúp lệnh 'dotnet ef migrations' (Add-Migration / Update-Database) có thể tự tạo DB context.
    /// Nhờ có lớp này, ta có thể xóa hoàn toàn Constructor rỗng và logic đọc file appsettings bị dính cứng (hardcode)
    /// bên trong AppDbContext, giúp AppDbContext sạch sẽ và chuẩn hóa 100% Dependency Injection.
    /// </summary>
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // Xây dựng trình đọc cấu hình từ file appsettings.json
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            string connectionString = configuration.GetConnectionString("DefaultConnection");

            // Cấu hình SQL Server
            optionsBuilder.UseSqlServer(connectionString);

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
