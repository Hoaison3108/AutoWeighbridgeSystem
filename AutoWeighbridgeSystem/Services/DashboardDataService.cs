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

            // Trích xuất trực tiếp chuỗi dạng text thô từ DB, sử dụng hàm Distinct() SQL
            // Đảm bảo không tốn RAM và tự dọn dẹp các biển số hay tên khách hàng trùng lặp.
            var vehicles = await db.Vehicles
                .AsNoTracking()
                .Where(v => !v.IsDeleted && !string.IsNullOrEmpty(v.LicensePlate))
                .Select(v => v.LicensePlate)
                .Distinct()
                .ToListAsync();

            var customers = await db.Customers
                .AsNoTracking()
                .Where(c => !c.IsDeleted && !string.IsNullOrEmpty(c.CustomerName))
                .Select(c => c.CustomerName)
                .Distinct()
                .ToListAsync();

            var products = await db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !string.IsNullOrEmpty(p.ProductName))
                .Select(p => p.ProductName)
                .Distinct()
                .ToListAsync();

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

            // 1. Ưu tiên: Lấy TẤT CẢ các phiếu đang chờ cân lần 2 (Chưa hoàn thành và chưa bị hủy)
            var pendingTickets = await db.WeighingTickets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.TimeOut == null && !t.IsVoid)
                .OrderBy(t => t.TimeIn) // Đưa phiếu chờ lâu nhất lên đầu
                .ToListAsync();

            // 2. Lịch sử: Lấy giới hạn N phiếu gần nhất ĐÃ HOÀN THÀNH hoặc BỊ HỦY
            var completedTickets = await db.WeighingTickets
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.TimeOut != null || t.IsVoid)
                .OrderByDescending(t => t.TimeIn) // Lấy phiếu mới nhất lên đầu
                .Take(take)
                .ToListAsync();

            // 3. Gộp lại: Nhóm "Đợi cân" nằm trên, Nhóm "Lịch sử" nối theo sau.
            return pendingTickets.Concat(completedTickets).ToList();
        }

        /// <summary>
        /// Lấy thông tin chi tiết của một xe dựa trên biển số.
        /// Sử dụng để điền nhanh thông tin (Bì, Khách hàng) trong các form nhập liệu.
        /// </summary>
        public async Task<Vehicle?> GetVehicleByPlateAsync(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate)) return null;
            using var db = _dbContextFactory.CreateDbContext();
            return await db.Vehicles
                .Include(v => v.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.LicensePlate == plate && !v.IsDeleted);
        }
    }

    /// <summary>Snapshot dữ liệu danh mục (chỉ lưu dạng chuỗi thô để chống ngập RAM) được tải khi Dashboard khởi động.</summary>
    public sealed record DashboardInitialData(
        IReadOnlyList<string>  Vehicles,
        IReadOnlyList<string> Customers,
        IReadOnlyList<string>  Products,
        string DefaultProductName);
}
