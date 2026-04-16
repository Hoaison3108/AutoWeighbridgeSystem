using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ tải dữ liệu cho Dashboard: danh sách xe, khách hàng, hàng hóa
    /// và 15 phiếu cân gần nhất.
    /// Sử dụng <see cref="IDbContextFactory{TContext}"/> để tạo DbContext độc lập,
    /// tránh xung đột trên các luồng khác nhau.
    /// </summary>
    public sealed class DashboardDataService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;

        public DashboardDataService(IDbContextFactory<AppDbContext> dbContextFactory, IConfiguration configuration)
        {
            _dbContextFactory = dbContextFactory;
            _configuration    = configuration;
        }

        /// <summary>
        /// Tải tất cả dữ liệu cần thiết khi Dashboard khởi động:
        /// danh sách xe (kèm thông tin khách hàng), khách hàng, hàng hóa, và tên hàng mặc định.
        /// </summary>
        /// <returns><see cref="DashboardInitialData"/> chứa toàn bộ danh mục.</returns>
        public async Task<DashboardInitialData> LoadInitialDataAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();

            var vehicles  = await db.Vehicles
                .AsNoTracking()
                .Include(v => v.Customer)
                .Where(v => !v.IsDeleted)
                .ToListAsync();

            var customers          = await db.Customers.AsNoTracking().ToListAsync();
            var products           = await db.Products.AsNoTracking().ToListAsync();
            var defaultProductName = _configuration["ScaleSettings:DefaultProductName"] ?? "Đá xô bồ";

            return new DashboardInitialData(vehicles, customers, products, defaultProductName);
        }

        /// <summary>
        /// Tải danh sách phiếu cân gần nhất (mặc định 15 phiếu), sắp xếp từ mới đến cũ.
        /// Bao gồm cả phiếu đã hủy (<c>IgnoreQueryFilters</c>) để hiển thị đầy đủ lịch sử.
        /// </summary>
        /// <param name="take">Số phiếu tối đa cần lấy (mặc định 15).</param>
        public async Task<IReadOnlyList<WeighingTicket>> LoadRecentTicketsAsync(int take = 15)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return await db.WeighingTickets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderByDescending(t => t.TimeIn)
                .Take(take)
                .ToListAsync();
        }
    }

    /// <summary>Snapshot dữ liệu danh mục được tải khi Dashboard khởi động.</summary>
    public sealed record DashboardInitialData(
        IReadOnlyList<Vehicle>  Vehicles,
        IReadOnlyList<Customer> Customers,
        IReadOnlyList<Product>  Products,
        string DefaultProductName);
}
