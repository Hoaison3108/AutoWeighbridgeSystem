using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.Services
{
    public sealed class DashboardDataService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;

        public DashboardDataService(IDbContextFactory<AppDbContext> dbContextFactory, IConfiguration configuration)
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
        }

        public async Task<DashboardInitialData> LoadInitialDataAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();

            var vehicles = await db.Vehicles
                .AsNoTracking()
                .Include(v => v.Customer)
                .Where(v => !v.IsDeleted)
                .ToListAsync();

            var customers = await db.Customers.AsNoTracking().ToListAsync();
            var products = await db.Products.AsNoTracking().ToListAsync();
            var defaultProductName = _configuration["ScaleSettings:DefaultProductName"] ?? "Đá xô bồ";

            return new DashboardInitialData(vehicles, customers, products, defaultProductName);
        }

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

    public sealed record DashboardInitialData(
        IReadOnlyList<Vehicle> Vehicles,
        IReadOnlyList<Customer> Customers,
        IReadOnlyList<Product> Products,
        string DefaultProductName);
}
