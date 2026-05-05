using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Tests
{
    /// <summary>
    /// Unit tests cho <see cref="WeighingBusinessService"/>.
    /// Dùng EF Core InMemory database để tránh phụ thuộc vào SQL Server thật.
    /// Mỗi test nhận một database instance riêng biệt (xem <see cref="BuildFactory"/>).
    /// </summary>
    public class WeighingBusinessServiceTests : IDisposable
    {
        // =========================================================================
        // SETUP
        // =========================================================================

        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly WeighingBusinessService _sut; // System Under Test

        public WeighingBusinessServiceTests()
        {
            // Tắt log Serilog trong test để output gọn
            Log.Logger = new LoggerConfiguration().CreateLogger();

            _factory = BuildFactory($"TestDb_{Guid.NewGuid()}");
            _sut = new WeighingBusinessService(_factory);

            // Seed dữ liệu ban đầu
            SeedDatabase(_factory);
        }

        public void Dispose()
        {
            // InMemory DB tự clear khi DbContext bị dispose
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private static IDbContextFactory<AppDbContext> BuildFactory(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new TestDbContextFactory(options);
        }

        private static void SeedDatabase(IDbContextFactory<AppDbContext> factory)
        {
            using var db = factory.CreateDbContext();

            // Khách hàng
            db.Customers.Add(new Customer
            {
                CustomerId   = "KH001",
                CustomerName = "Công ty ABC",
                IsDeleted    = false
            });

            // Xe có bì đã đăng ký
            db.Vehicles.Add(new Vehicle
            {
                VehicleId    = 1,
                LicensePlate = "51A-12345",
                TareWeight   = 8000m,
                CustomerId   = "KH001",
                IsDeleted    = false
            });

            // Xe KHÔNG có bì (TareWeight = 0)
            db.Vehicles.Add(new Vehicle
            {
                VehicleId    = 2,
                LicensePlate = "51B-99999",
                TareWeight   = 0,
                CustomerId   = "KH001",
                IsDeleted    = false
            });

            db.SaveChanges();
        }

        // =========================================================================
        // TEST: ProcessWeighingAsync — CÂN 1 LẦN (ONE-PASS)
        // =========================================================================

        [Fact]
        public async Task ProcessWeighing_OnePass_WithValidTare_ShouldCreateCompletedTicket()
        {
            // Arrange
            decimal grossWeight = 15_000m;
            decimal tare        = 8_000m;

            // Act
            var result = await _sut.ProcessWeighingAsync(
                licensePlate:  "51A-12345",
                vehicleId:     1,
                customerName:  "Công ty ABC",
                productName:   "Xi Măng",
                finalWeight:   grossWeight,
                isOnePassMode: true);

            // Assert
            Assert.True(result.IsSuccess, result.Message);
            Assert.False(result.IsFirstWeighing, "One-pass không được trả IsFirstWeighing = true");
            Assert.NotNull(result.Ticket);
            Assert.Equal(grossWeight,           result.Ticket!.GrossWeight);
            Assert.Equal(tare,                  result.Ticket.TareWeight);
            Assert.Equal(grossWeight - tare,    result.Ticket.NetWeight);
            Assert.NotNull(result.Ticket.TimeOut);   // Phiếu phải được đóng ngay
        }

        [Fact]
        public async Task ProcessWeighing_OnePass_NoTareRegistered_ShouldFail()
        {
            // Arrange: xe có TareWeight = 0

            // Act
            var result = await _sut.ProcessWeighingAsync(
                licensePlate:  "51B-99999",
                vehicleId:     2,
                customerName:  "Khách lẻ",
                productName:   "Hàng hóa",
                finalWeight:   10_000m,
                isOnePassMode: true);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("bì", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ProcessWeighing_OnePass_GrossLessThanTare_NetShouldBeZero()
        {
            // Arrange: gross < tare → net âm → phải clamp về 0
            decimal gross = 5_000m; // nhỏ hơn bì 8000

            // Act
            var result = await _sut.ProcessWeighingAsync(
                licensePlate:  "51A-12345",
                vehicleId:     1,
                customerName:  "Công ty ABC",
                productName:   "Xi Măng",
                finalWeight:   gross,
                isOnePassMode: true);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.Ticket!.NetWeight);
        }

        // =========================================================================
        // TEST: ProcessWeighingAsync — CÂN 2 LẦN
        // =========================================================================

        [Fact]
        public async Task ProcessWeighing_TwoPass_FirstWeigh_ShouldCreateOpenTicket()
        {
            // Act: cân lần 1
            var result = await _sut.ProcessWeighingAsync(
                licensePlate:  "51A-12345",
                vehicleId:     1,
                customerName:  "Công ty ABC",
                productName:   "Xi Măng",
                finalWeight:   15_000m,
                isOnePassMode: false);

            // Assert
            Assert.True(result.IsSuccess,         result.Message);
            Assert.True(result.IsFirstWeighing,   "Lần 1 phải trả IsFirstWeighing = true");

            // Kiểm tra DB: phải có một phiếu đang mở (TimeOut = null)
            using var db = _factory.CreateDbContext();
            var openTickets = await db.WeighingTickets
                .CountAsync(t => t.LicensePlate == "51A-12345" && t.TimeOut == null && !t.IsVoid);
            Assert.Equal(1, openTickets);
        }

        [Fact]
        public async Task ProcessWeighing_TwoPass_SecondWeigh_ShouldCloseTicketWithCorrectNet()
        {
            // Arrange: cân lần 1
            await _sut.ProcessWeighingAsync(
                licensePlate:  "51A-12345",
                vehicleId:     1,
                customerName:  "Công ty ABC",
                productName:   "Xi Măng",
                finalWeight:   15_000m,   // Gross = 15t
                isOnePassMode: false);

            // Act: cân lần 2
            var result = await _sut.ProcessWeighingAsync(
                licensePlate:  "51A-12345",
                vehicleId:     1,
                customerName:  "Công ty ABC",
                productName:   "Xi Măng",
                finalWeight:   8_000m,    // Tare = 8t
                isOnePassMode: false);

            // Assert
            Assert.True(result.IsSuccess, result.Message);
            Assert.False(result.IsFirstWeighing, "Lần 2 phải trả IsFirstWeighing = false");

            // Kiểm tra DB: phiếu đã đóng, net đúng
            using var db = _factory.CreateDbContext();
            var ticket = await db.WeighingTickets
                .FirstOrDefaultAsync(t => t.LicensePlate == "51A-12345" && t.TimeOut != null);
            Assert.NotNull(ticket);
            Assert.Equal(15_000m, ticket!.GrossWeight);
            Assert.Equal(8_000m,  ticket.TareWeight);
            Assert.Equal(7_000m,  ticket.NetWeight);
        }

        [Fact]
        public async Task ProcessWeighing_TwoPass_SecondWeighLarger_ShouldSwapGrossAndTare()
        {
            // Arrange: lần 1 ít hơn lần 2 (xe vào rỗng, ra có hàng)
            await _sut.ProcessWeighingAsync(
                "51A-12345", 1, "Công ty ABC", "Xi Măng", 8_000m, false);

            // Act: lần 2 nhiều hơn → gross/tare phải được hoán đổi
            var result = await _sut.ProcessWeighingAsync(
                "51A-12345", 1, "Công ty ABC", "Xi Măng", 15_000m, false);

            Assert.True(result.IsSuccess);

            using var db = _factory.CreateDbContext();
            var ticket = await db.WeighingTickets
                .FirstAsync(t => t.LicensePlate == "51A-12345" && t.TimeOut != null);
            Assert.Equal(15_000m, ticket.GrossWeight);
            Assert.Equal(8_000m,  ticket.TareWeight);
            Assert.Equal(7_000m,  ticket.NetWeight);
        }

        [Fact]
        public async Task ProcessWeighing_TwoPass_OpenTicketOver24h_ShouldAutoVoidAndCreateNew()
        {
            // Arrange: tạo thủ công một phiếu "treo" hơn 24h
            using (var db = _factory.CreateDbContext())
            {
                db.WeighingTickets.Add(new WeighingTicket
                {
                    TicketID     = "OLD-001",
                    LicensePlate = "51A-12345",
                    CustomerName = "Công ty ABC",
                    ProductName  = "Xi Măng",
                    TimeIn       = DateTime.Now.AddHours(-25), // hơn 24h
                    TimeOut      = null,
                    GrossWeight  = 10_000m,
                    IsVoid       = false
                });
                await db.SaveChangesAsync();
            }

            // Act: cân mới
            var result = await _sut.ProcessWeighingAsync(
                "51A-12345", 1, "Công ty ABC", "Xi Măng", 15_000m, false);

            // Assert
            Assert.True(result.IsSuccess, result.Message);
            Assert.True(result.IsFirstWeighing, "Sau auto-void, lần này phải là Lần 1");

            // Phiếu cũ phải đã bị void tự động
            using var db2 = _factory.CreateDbContext();
            var oldTicket = await db2.WeighingTickets
                .IgnoreQueryFilters()
                .FirstAsync(t => t.TicketID == "OLD-001");
            Assert.True(oldTicket.IsVoid);
            Assert.Contains("24h", oldTicket.VoidReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ProcessWeighing_EmptyLicensePlate_ShouldReturnFailure()
        {
            var result = await _sut.ProcessWeighingAsync(
                licensePlate:  "",
                vehicleId:     0,
                customerName:  "Test",
                productName:   "Test",
                finalWeight:   10_000m,
                isOnePassMode: false);

            Assert.False(result.IsSuccess);
        }

        // =========================================================================
        // TEST: VoidTicketAsync
        // =========================================================================

        [Fact]
        public async Task VoidTicket_ExistingNonVoidedTicket_ShouldSucceed()
        {
            // Arrange: tạo phiếu trong DB
            using (var db = _factory.CreateDbContext())
            {
                db.WeighingTickets.Add(new WeighingTicket
                {
                    TicketID     = "260417-001",
                    LicensePlate = "51A-12345",
                    CustomerName = "Công ty ABC",
                    ProductName  = "Xi Măng",
                    TimeIn       = DateTime.Now,
                    GrossWeight  = 10_000m,
                    IsVoid       = false
                });
                await db.SaveChangesAsync();
            }

            // Act
            var (isSuccess, message) = await _sut.VoidTicketAsync("260417-001", "Test hủy");

            // Assert
            Assert.True(isSuccess, message);
            Assert.Equal("260417-001", message);

            using var db2 = _factory.CreateDbContext();
            var ticket = await db2.WeighingTickets
                .IgnoreQueryFilters()
                .FirstAsync(t => t.TicketID == "260417-001");
            Assert.True(ticket.IsVoid);
            Assert.Equal("Test hủy", ticket.VoidReason);
        }

        [Fact]
        public async Task VoidTicket_AlreadyVoided_ShouldReturnFailure()
        {
            // Arrange
            using (var db = _factory.CreateDbContext())
            {
                db.WeighingTickets.Add(new WeighingTicket
                {
                    TicketID     = "260417-002",
                    LicensePlate = "51A-12345",
                    CustomerName = "Công ty ABC",
                    ProductName  = "Xi Măng",
                    TimeIn       = DateTime.Now,
                    GrossWeight  = 10_000m,
                    IsVoid       = true,
                    VoidReason   = "Đã hủy trước"
                });
                await db.SaveChangesAsync();
            }

            // Act
            var (isSuccess, message) = await _sut.VoidTicketAsync("260417-002");

            // Assert
            Assert.False(isSuccess);
            Assert.Contains("đã bị hủy", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task VoidTicket_NonExistentId_ShouldReturnFailure()
        {
            var (isSuccess, message) = await _sut.VoidTicketAsync("KHONG-TON-TAI");

            Assert.False(isSuccess);
            Assert.Contains("không tìm thấy", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task VoidTicket_EmptyId_ShouldReturnFailure()
        {
            var (isSuccess, _) = await _sut.VoidTicketAsync("");
            Assert.False(isSuccess);
        }
    }

    // =========================================================================
    // HELPER: TestDbContextFactory — implement IDbContextFactory<AppDbContext>
    // =========================================================================

    /// <summary>
    /// Implement <see cref="IDbContextFactory{TContext}"/> đơn giản dùng trong test.
    /// Mỗi lần <c>CreateDbContext()</c> tạo ra một context mới dùng chung options (InMemory DB).
    /// </summary>
    internal sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options) => _options = options;

        public AppDbContext CreateDbContext() => new AppDbContext(_options);
    }
}
