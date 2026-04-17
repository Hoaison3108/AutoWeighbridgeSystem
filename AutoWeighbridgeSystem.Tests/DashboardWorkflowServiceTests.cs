using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Tests
{
    /// <summary>
    /// Unit tests cho <see cref="DashboardWorkflowService"/>.
    /// <para>
    /// Kiểm tra thuần logic quyết định (Decision Logic) — không cần DB thật:
    /// <see cref="RfidBusinessService"/> được mock bằng NSubstitute để giả lập
    /// kết quả tra cứu thẻ thành công / không thành công.
    /// </para>
    /// </summary>
    public class DashboardWorkflowServiceTests
    {
        // =========================================================================
        // SETUP
        // =========================================================================

        private readonly DashboardWorkflowService _sut;
        private readonly RfidBusinessService _rfidBusinessMock;

        private const decimal MIN_WEIGHT = 200m;

        public DashboardWorkflowServiceTests()
        {
            Log.Logger = new LoggerConfiguration().CreateLogger();

            // Mock IConfiguration trả về thông số cố định
            var config = Substitute.For<IConfiguration>();
            config["ScaleSettings:MinWeightThreshold"].Returns("200");
            config["ScaleSettings:RfidCooldownSeconds"].Returns("3");

            // Mock RfidBusinessService — dùng InMemory DB thật
            // (không mock method vì là class concrete, không có interface)
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"RfidDb_{Guid.NewGuid()}")
                .Options;
            var rfidFactory = new TestDbContextFactory(options);

            // Seed xe có thẻ RFID
            using (var db = rfidFactory.CreateDbContext())
            {
                db.Customers.Add(new Customer { CustomerId = "KVL", CustomerName = "Khách vãng lai", IsDeleted = false });
                db.Vehicles.Add(new Vehicle
                {
                    VehicleId    = 1,
                    LicensePlate = "51A-12345",
                    TareWeight   = 8_000m,
                    CustomerId   = "KVL",
                    RfidCardId   = "1234567890",
                    IsDeleted    = false
                });
                db.SaveChanges();
            }

            _rfidBusinessMock = new RfidBusinessService(rfidFactory);
            _sut = new DashboardWorkflowService(config, _rfidBusinessMock);
        }

        // =========================================================================
        // TEST: EvaluateScaleEvent
        // =========================================================================

        [Fact]
        public void EvaluateScaleEvent_WeightBelowThreshold_NoPendingVehicle_ShouldReturnNone()
        {
            var decision = _sut.EvaluateScaleEvent(
                weight:          50m,  // < 200 kg
                isStable:        true,
                isAutoMode:      true,
                isProcessingSave:false,
                isWeightLocked:  false);

            Assert.False(decision.ShouldSave);
            Assert.False(decision.ShouldClearPendingAndReset);
        }

        [Fact]
        public void EvaluateScaleEvent_WeightBelowThreshold_WithPendingVehicle_ShouldClearPending()
        {
            // Arrange: đặt xe vào hàng chờ thủ công qua reflection helper
            SetPendingVehicle();

            // Act
            var decision = _sut.EvaluateScaleEvent(
                weight:          10m,   // < 200
                isStable:        true,
                isAutoMode:      true,
                isProcessingSave:false,
                isWeightLocked:  false);

            // Assert
            Assert.True(decision.ShouldClearPendingAndReset);
            Assert.False(decision.ShouldSave);
            Assert.False(_sut.HasPendingVehicle);  // Phải xóa hàng chờ
        }

        [Fact]
        public void EvaluateScaleEvent_AllConditionsMet_ShouldReturnSave()
        {
            // Arrange: có xe chờ, cân ổn định, Auto mode
            SetPendingVehicle();

            var decision = _sut.EvaluateScaleEvent(
                weight:          15_000m,
                isStable:        true,
                isAutoMode:      true,
                isProcessingSave:false,
                isWeightLocked:  false);

            Assert.True(decision.ShouldSave);
            Assert.Equal(15_000m, decision.WeightToSave);
            Assert.NotNull(decision.PendingVehicle);
            Assert.Equal("51A-12345", decision.PendingVehicle!.LicensePlate);

            // Hàng chờ phải được xóa sau khi ra lệnh lưu
            Assert.False(_sut.HasPendingVehicle);
        }

        [Fact]
        public void EvaluateScaleEvent_NoPendingVehicle_ShouldNotSave()
        {
            // Không có xe trong hàng chờ → không lưu dù cân ổn định
            var decision = _sut.EvaluateScaleEvent(
                weight:          15_000m,
                isStable:        true,
                isAutoMode:      true,
                isProcessingSave:false,
                isWeightLocked:  false);

            Assert.False(decision.ShouldSave);
        }

        [Fact]
        public void EvaluateScaleEvent_ManualMode_ShouldNotAutoSave()
        {
            SetPendingVehicle();

            var decision = _sut.EvaluateScaleEvent(
                weight:          15_000m,
                isStable:        true,
                isAutoMode:      false,   // Chế độ tay
                isProcessingSave:false,
                isWeightLocked:  false);

            Assert.False(decision.ShouldSave);
        }

        [Fact]
        public void EvaluateScaleEvent_ScaleUnstable_ShouldNotSave()
        {
            SetPendingVehicle();

            var decision = _sut.EvaluateScaleEvent(
                weight:          15_000m,
                isStable:        false,  // Cân chưa ổn định
                isAutoMode:      true,
                isProcessingSave:false,
                isWeightLocked:  false);

            Assert.False(decision.ShouldSave);
        }

        [Fact]
        public void EvaluateScaleEvent_IsProcessingSave_ShouldNotSave()
        {
            SetPendingVehicle();

            var decision = _sut.EvaluateScaleEvent(
                weight:          15_000m,
                isStable:        true,
                isAutoMode:      true,
                isProcessingSave:true,   // Đang xử lý lưu
                isWeightLocked:  false);

            Assert.False(decision.ShouldSave);
        }

        [Fact]
        public void EvaluateScaleEvent_WeightLocked_ShouldNotClearPending()
        {
            // Khi cân về 0 nhưng trọng lượng đang bị chốt → không reset
            SetPendingVehicle();

            var decision = _sut.EvaluateScaleEvent(
                weight:          10m,
                isStable:        false,
                isAutoMode:      true,
                isProcessingSave:false,
                isWeightLocked:  true);  // Đang bị chốt thủ công

            Assert.False(decision.ShouldClearPendingAndReset);
            // Hàng chờ vẫn còn
            Assert.True(_sut.HasPendingVehicle);
        }

        // =========================================================================
        // TEST: EvaluateRfidEventAsync
        // =========================================================================

        [Fact]
        public async Task EvaluateRfidEvent_ManualMode_ShouldReturnMessage()
        {
            var decision = await _sut.EvaluateRfidEventAsync(
                cardId:              "1234567890",
                selectedProductName: "Xi Măng",
                isAutoMode:          false,     // Chế độ tay
                isScaleStable:       true,
                currentWeight:       10_000m);

            Assert.True(decision.ShouldShowMessage);
            Assert.Contains("TAY", decision.CameraMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(decision.ShouldSave);
        }

        [Fact]
        public async Task EvaluateRfidEvent_UnknownCard_ShouldReturnUnregisteredMessage()
        {
            var decision = await _sut.EvaluateRfidEventAsync(
                cardId:              "9999999999",   // Thẻ không tồn tại trong DB
                selectedProductName: "Xi Măng",
                isAutoMode:          true,
                isScaleStable:       true,
                currentWeight:       10_000m);

            Assert.True(decision.ShouldShowMessage);
            Assert.Contains("CHƯA ĐĂNG KÝ", decision.CameraMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(decision.ShouldSave);
        }

        [Fact]
        public async Task EvaluateRfidEvent_KnownCard_ScaleStableAndHeavy_ShouldSaveNow()
        {
            // Thẻ "1234567890" → xe "51A-12345" đã seed
            var decision = await _sut.EvaluateRfidEventAsync(
                cardId:              "1234567890",
                selectedProductName: "Xi Măng",
                isAutoMode:          true,
                isScaleStable:       true,
                currentWeight:       15_000m);  // > 200kg

            Assert.True(decision.ShouldSave);
            Assert.Equal(15_000m, decision.WeightToSave);
            Assert.Equal("51A-12345", decision.PendingVehicle!.LicensePlate);
        }

        [Fact]
        public async Task EvaluateRfidEvent_KnownCard_ScaleUnstable_ShouldPend()
        {
            var decision = await _sut.EvaluateRfidEventAsync(
                cardId:              "1234567890",
                selectedProductName: "Xi Măng",
                isAutoMode:          true,
                isScaleStable:       false,     // Cân chưa ổn định
                currentWeight:       15_000m);

            Assert.False(decision.ShouldSave);
            Assert.True(decision.ShouldStartPendingTimeout);
            Assert.True(_sut.HasPendingVehicle);
        }

        [Fact]
        public async Task EvaluateRfidEvent_KnownCard_WeightTooLight_ShouldPend()
        {
            var decision = await _sut.EvaluateRfidEventAsync(
                cardId:              "1234567890",
                selectedProductName: "Xi Măng",
                isAutoMode:          true,
                isScaleStable:       true,
                currentWeight:       50m);  // < 200 kg

            Assert.False(decision.ShouldSave);
            Assert.True(decision.ShouldStartPendingTimeout);
        }

        // =========================================================================
        // TEST: ShouldIgnoreRfidRead (Cooldown)
        // =========================================================================

        [Fact]
        public void ShouldIgnoreRfidRead_FirstRead_ShouldNotIgnore()
        {
            bool result = _sut.ShouldIgnoreRfidRead("ScaleIn");
            Assert.False(result);
        }

        [Fact]
        public void ShouldIgnoreRfidRead_SecondReadImmediately_ShouldIgnore()
        {
            _sut.ShouldIgnoreRfidRead("ScaleIn"); // Lần 1 — không ignore
            bool result = _sut.ShouldIgnoreRfidRead("ScaleIn"); // Lần 2 ngay sau — phải ignore
            Assert.True(result);
        }

        [Fact]
        public void ShouldIgnoreRfidRead_DifferentReaders_ShouldNotIgnoreEachOther()
        {
            _sut.ShouldIgnoreRfidRead("ScaleIn");
            bool result = _sut.ShouldIgnoreRfidRead("ScaleOut"); // Đầu đọc khác
            Assert.False(result);
        }

        // =========================================================================
        // TEST: ClearPendingData
        // =========================================================================

        [Fact]
        public void ClearPendingData_ShouldResetHasPendingVehicle()
        {
            SetPendingVehicle();
            Assert.True(_sut.HasPendingVehicle);

            _sut.ClearPendingData();

            Assert.False(_sut.HasPendingVehicle);
        }

        [Fact]
        public void GetPendingVehicleData_AfterClear_ShouldReturnNulls()
        {
            SetPendingVehicle();
            _sut.ClearPendingData();

            var data = _sut.GetPendingVehicleData();

            Assert.Null(data.LicensePlate);
            Assert.Null(data.CustomerName);
            Assert.Null(data.ProductName);
            Assert.Equal(0, data.VehicleId);
        }

        // =========================================================================
        // HELPER: Đặt xe vào hàng chờ thông qua EvaluateRfidEventAsync public API
        // =========================================================================

        private void SetPendingVehicle()
        {
            // Dùng EvaluateRfidEventAsync với thẻ hợp lệ + cân chưa ổn định
            // để đưa xe vào hàng chờ (Pending)
            _sut.EvaluateRfidEventAsync(
                cardId:              "1234567890",
                selectedProductName: "Xi Măng",
                isAutoMode:          true,
                isScaleStable:       false,
                currentWeight:       0m).GetAwaiter().GetResult();
        }
    }
}
