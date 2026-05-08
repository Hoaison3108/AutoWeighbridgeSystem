using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ điều phối luồng nghiệp vụ chính của Dashboard:
    /// phân tích sự kiện từ Scale và RFID để đưa ra <b>quyết định hành động</b>
    /// (lưu phiếu / reset form / chờ thêm).
    /// <para>
    /// Lưu ý: Class này chỉ ra quyết định (<i>what to do</i>), không tự thực thi.
    /// Việc thực thi thuộc về <see cref="DashboardEventCoordinator"/>.
    /// </para>
    /// </summary>
    public sealed class DashboardWorkflowService
    {
        private readonly RfidBusinessService _rfidBusiness;
        private readonly ConcurrentDictionary<string, DateTime> _rfidCooldowns = new();
        private readonly object _stateLock = new();

        private int _rfidCooldownSeconds = 3;

        // Dữ liệu xe đang chờ cân (sau khi RFID quét nhưng xe chưa lên cân)
        private string? _pendingLicensePlate;
        private string? _pendingCustomerName;
        private string? _pendingProductName;
        private int _pendingVehicleId;
        private bool _pendingIsOnePassMode;

        /// <summary>Ngưỡng trọng lượng tối thiểu (kg) để cân được coi là hợp lệ (mặc định 200kg).</summary>
        public decimal MinWeightThreshold { get; private set; } = 200;

        /// <summary><c>true</c> khi có xe đã quét RFID thành công và đang chờ cân.</summary>
        public bool HasPendingVehicle { get; private set; }

        private string? _lastSavedTagId; // Lưu mã thẻ của xe vừa cân xong để phục vụ nghiệp vụ CÂN NỐI ĐUÔI THÔNG MINH
        private bool _wasUnstableSinceLastSave = false; // Đánh dấu khoảng hở (rung động) vật lý giữa 2 xe nối đuôi

        public DashboardWorkflowService(IConfiguration configuration, RfidBusinessService rfidBusiness)
        {
            _rfidBusiness = rfidBusiness;
            LoadConfiguration(configuration);
        }

        private void LoadConfiguration(IConfiguration configuration)
        {
            if (decimal.TryParse(configuration["ScaleSettings:MinWeightThreshold"], out decimal threshold))
                MinWeightThreshold = threshold;
            if (int.TryParse(configuration["ScaleSettings:RfidCooldownSeconds"], out int cooldown))
                _rfidCooldownSeconds = cooldown;
        }

        /// <summary>
        /// Kiểm tra xem lần đọc thẻ từ đầu đọc <paramref name="readerRole"/> có nên bị bỏ qua không
        /// (do cooldown chống đọc trùng).
        /// </summary>
        /// <param name="readerRole">Vai trò đầu đọc (ScaleIn, ScaleOut, Desk).</param>
        /// <returns><c>true</c> nếu nên bỏ qua lần đọc này.</returns>
        public bool ShouldIgnoreRfidRead(string readerRole)
        {
            if (_rfidCooldowns.TryGetValue(readerRole, out DateTime lastRead))
                if ((DateTime.Now - lastRead).TotalSeconds < _rfidCooldownSeconds) return true;

            _rfidCooldowns[readerRole] = DateTime.Now;
            return false;
        }

        /// <summary>Xóa toàn bộ dữ liệu xe đang chờ, đặt lại <see cref="HasPendingVehicle"/> = false.</summary>
        public void ClearPendingData()
        {
            lock (_stateLock)
            {
                _pendingLicensePlate = null;
                _pendingCustomerName = null;
                _pendingProductName  = null;
                _pendingVehicleId    = 0;
                HasPendingVehicle    = false;
            }
        }

        /// <summary>Lấy snapshot dữ liệu xe đang chờ (dùng để truyền vào lệnh lưu phiếu).</summary>
        public PendingVehicleData GetPendingVehicleData()
        {
            lock (_stateLock)
            {
                return new PendingVehicleData(
                    _pendingLicensePlate,
                    _pendingCustomerName,
                    _pendingProductName,
                    _pendingVehicleId,
                    _pendingIsOnePassMode);
            }
        }

        private decimal _lastSavedWeight = 0;

        /// <summary>
        /// Phân tích sự kiện từ đầu cân và đưa ra quyết định:
        /// <list type="bullet">
        ///   <item>Nếu cân trở về 0 (hoặc giảm mạnh sau khi cân) và có xe chờ → reset hàng chờ.</item>
        ///   <item>Nếu chế độ Auto, cân ổn định, có xe chờ và không đang lưu → ra lệnh lưu phiếu.</item>
        ///   <item>Các trường hợp còn lại → không làm gì (<see cref="ScaleWorkflowDecision.None"/>).</item>
        /// </list>
        /// </summary>
        /// <param name="weight">Trọng lượng hiện tại (kg).</param>
        /// <param name="isStable">Cân đang ổn định hay không.</param>
        /// <param name="isAutoMode">Chế độ tự động hay thủ công.</param>
        /// <param name="isProcessingSave">Đang có giao dịch lưu phiếu chạy.</param>
        /// <param name="isWeightLocked">Trọng lượng đã bị chốt thủ công.</param>
        public ScaleWorkflowDecision EvaluateScaleEvent(decimal weight, bool isStable, bool isAutoMode, bool isProcessingSave, bool isWeightLocked)
        {
            lock (_stateLock)
            {
                // 1. KIỂM TRA GIẢI PHÓNG BÀN CÂN (XE ĐÃ XUỐNG)
                // Điều kiện A: Trọng lượng về dưới ngưỡng tối thiểu (ví dụ < 200kg)
                bool isScaleReleased = weight < MinWeightThreshold;
                
                // Điều kiện B (Chống kẹt cân): Nếu xe vừa cân xong rất nặng, nhưng khi xuống cân vẫn kẹt một lượng lớn.
                // Nếu cân hiện tại giảm xuống dưới 30% tải trọng xe trước -> Coi như xe đã xuống.
                if (!isScaleReleased && _lastSavedWeight > MinWeightThreshold)
                {
                    if (weight < (_lastSavedWeight * 0.3m)) 
                    {
                        isScaleReleased = true;
                    }
                }

                if (isScaleReleased)
                {
                    _lastSavedWeight = 0; // Reset bộ nhớ tải trọng
                    _lastSavedTagId = null; // Xóa mã thẻ cũ để sẵn sàng cho chu kỳ CÂN NỐI ĐUÔI tiếp theo
                    _wasUnstableSinceLastSave = true; // Bàn cân trống hoàn toàn

                    if (HasPendingVehicle && !isWeightLocked)
                    {
                        ClearPendingData();
                        return ScaleWorkflowDecision.ClearPendingAndReset("XE ĐÃ XUỐNG CÂN - HỦY LỆNH CHỜ");
                    }
 
                    return ScaleWorkflowDecision.None();
                }

                // 2. THEO DÕI BIẾN ĐỘNG VẬT LÝ (GAP DETECTION)
                // Nếu cân đang rung động -> Dấu hiệu xe cũ đang ra hoặc xe mới đang vào (Khoảng hở nối đuôi).
                if (!isStable)
                {
                    _wasUnstableSinceLastSave = true;
                }
 
                if (isAutoMode && isStable && HasPendingVehicle && !isProcessingSave && !isWeightLocked)
                {
                    var pending = new PendingVehicleData(
                        _pendingLicensePlate,
                        _pendingCustomerName,
                        _pendingProductName,
                        _pendingVehicleId,
                        _pendingIsOnePassMode);
 
                    // Lưu lại tải trọng xe này để làm mốc reset cho xe sau nếu cân bị kẹt
                    _lastSavedWeight = weight;

                    // Xóa dữ liệu sau khi đã lấy snapshot
                    _pendingLicensePlate = null;
                    _pendingCustomerName = null;
                    _pendingProductName  = null;
                    _pendingVehicleId    = 0;
                    _pendingIsOnePassMode = false;
                    HasPendingVehicle    = false;
 
                    return ScaleWorkflowDecision.SaveWithPending(weight, pending);
                }
 
                return ScaleWorkflowDecision.None();
            }
        }

        /// <summary>
        /// Gọi hàm này sau khi phiếu cân đã được lưu thành công vào Database.
        /// Giúp hệ thống ghi nhớ trạng thái để phục vụ cân nối đuôi.
        /// </summary>
        public void MarkAsSaved(string? tagId, decimal weight)
        {
            lock (_stateLock)
            {
                _lastSavedTagId = tagId;
                _lastSavedWeight = weight;
                _wasUnstableSinceLastSave = false; // Bắt đầu theo dõi rung động cho xe tiếp theo
                ClearPendingData();
            }
        }

        /// <summary>
        /// Phân tích sự kiện RFID và đưa ra quyết định:
        /// <list type="bullet">
        ///   <item>Chế độ tay → bỏ qua.</item>
        ///   <item>Thẻ chưa đăng ký → hiển thị thông báo.</item>
        ///   <item>Cân đang ổn định và đủ tải → lưu ngay (<see cref="RfidWorkflowDecision.SaveNow"/>).</item>
        ///   <item>Cân chưa ổn định → lưu vào hàng chờ, bắt đầu đếm ngược timeout.</item>
        /// </list>
        /// </summary>
        /// <param name="cardId">Mã thẻ RFID đã làm sạch.</param>
        /// <param name="selectedProductName">Tên hàng hóa đang được chọn trên form.</param>
        /// <param name="isAutoMode">Chế độ tự động hay thủ công.</param>
        /// <param name="isScaleStable">Cân đang ổn định hay không.</param>
        /// <param name="currentWeight">Trọng lượng hiện tại (kg).</param>
        /// <param name="isOnePassMode">Chế độ cân một lần hay hai lần.</param>
        public async Task<RfidWorkflowDecision> EvaluateRfidEventAsync(string cardId, string selectedProductName, bool isAutoMode, bool isScaleStable, decimal currentWeight, bool isOnePassMode)
        {
            if (!isAutoMode) return RfidWorkflowDecision.Message("CHẾ ĐỘ TAY - BỎ QUA THẺ!");

            // DB check không cần lock
            var rfidResult = await _rfidBusiness.ProcessRawCardAsync(cardId);

            if (!rfidResult.IsSuccess || rfidResult.IsNewCard)
                return RfidWorkflowDecision.Message($"THẺ {rfidResult.CleanCardId} CHƯA ĐĂNG KÝ!");

            lock (_stateLock)
            {
                var tagId = rfidResult.CleanCardId;

                // 3. LOGIC CÂN NỐI ĐUÔI THÔNG MINH (Xử lý khi bàn cân vẫn đang có tải trọng)
                if (currentWeight >= MinWeightThreshold)
                {
                    // A. Chống cân lặp: Nếu mã thẻ vừa quẹt trùng với xe vừa cân xong -> Từ chối xử lý
                    if (tagId == _lastSavedTagId)
                    {
                        return RfidWorkflowDecision.Message("THẺ VỪA CÂN XONG - YÊU CẦU XE RA KHỎI CÂN!");
                    }

                    // B. Kiểm tra khoảng hở vật lý: Phải có nhịp rung động (unstable) thì mới coi là xe mới
                    if (!_wasUnstableSinceLastSave)
                    {
                        return RfidWorkflowDecision.Message("CHƯA CÓ KHOẢNG HỞ - YÊU CẦU XE TÁCH NHAU RA!");
                    }
                }

                // C. Kiểm tra trạng thái đang chờ: Nếu đang có xe đã quẹt thẻ nhưng chưa cân xong -> Chặn xe khác quẹt đè
                if (HasPendingVehicle)
                {
                    return RfidWorkflowDecision.Message("ĐANG CÓ XE CHỜ TRÊN BÀN CÂN!");
                }

                var vehicle = rfidResult.ExistingVehicle;
                _pendingLicensePlate = vehicle.LicensePlate;
                _pendingCustomerName = vehicle.Customer?.CustomerName ?? "Khách lẻ";
                _pendingProductName  = selectedProductName ?? "Hàng hóa";
                _pendingVehicleId    = vehicle.VehicleId;
                _pendingIsOnePassMode = isOnePassMode;
                HasPendingVehicle    = true;

                if (isScaleStable && currentWeight >= MinWeightThreshold)
                {
                    var pending = new PendingVehicleData(
                        _pendingLicensePlate,
                        _pendingCustomerName,
                        _pendingProductName,
                        _pendingVehicleId,
                        _pendingIsOnePassMode);

                    // Reset ngay lập tức vì lệnh lưu phiếu sẽ được thực thi
                    _pendingLicensePlate = null;
                    _pendingCustomerName = null;
                    _pendingProductName  = null;
                    _pendingVehicleId    = 0;
                    _pendingIsOnePassMode = false;
                    HasPendingVehicle    = false;

                    return RfidWorkflowDecision.SaveNow(currentWeight, pending);
                }

                return RfidWorkflowDecision.Pending($"NHẬN XE: {_pendingLicensePlate}. ĐỢI ỔN ĐỊNH...");
            }
        }
    }

    // =========================================================================
    // RESULT TYPES (Value Objects — bất biến, mô tả quyết định trả về)
    // =========================================================================

    /// <summary>Dữ liệu xe đang chờ cân (snapshot tại thời điểm RFID quét).</summary>
    public sealed record PendingVehicleData(string? LicensePlate, string? CustomerName, string? ProductName, int VehicleId, bool IsOnePassMode);

    /// <summary>
    /// Kết quả quyết định từ <see cref="DashboardWorkflowService.EvaluateScaleEvent"/>.
    /// Dùng static factory methods để tạo instance rõ nghĩa.
    /// </summary>
    public sealed record ScaleWorkflowDecision
    {
        /// <summary>Cần reset form và xóa dữ liệu chờ (khi cân về 0).</summary>
        public bool ShouldClearPendingAndReset { get; init; }

        /// <summary>Cần lưu phiếu cân ngay.</summary>
        public bool ShouldSave { get; init; }

        /// <summary>Trọng lượng cần lưu (kg).</summary>
        public decimal WeightToSave { get; init; }

        /// <summary>Thông tin xe đang chờ (dùng khi <see cref="ShouldSave"/> = true).</summary>
        public PendingVehicleData? PendingVehicle { get; init; }

        /// <summary>Nội dung thông báo hiển thị lên màn hình camera.</summary>
        public string? CameraMessage { get; init; }

        /// <summary>Không cần làm gì.</summary>
        public static ScaleWorkflowDecision None() => new();

        /// <summary>Cần reset form, kèm thông báo lý do.</summary>
        public static ScaleWorkflowDecision ClearPendingAndReset(string message) => new()
        {
            ShouldClearPendingAndReset = true,
            CameraMessage = message
        };

        /// <summary>Cần lưu phiếu với dữ liệu xe từ hàng chờ.</summary>
        public static ScaleWorkflowDecision SaveWithPending(decimal weight, PendingVehicleData pending) => new()
        {
            ShouldSave    = true,
            WeightToSave  = weight,
            PendingVehicle = pending
        };
    }

    /// <summary>
    /// Kết quả quyết định từ <see cref="DashboardWorkflowService.EvaluateRfidEventAsync"/>.
    /// Dùng static factory methods để tạo instance rõ nghĩa.
    /// </summary>
    public sealed record RfidWorkflowDecision
    {
        /// <summary>Cần hiển thị thông báo lên màn hình camera.</summary>
        public bool ShouldShowMessage { get; init; }

        /// <summary>Nội dung thông báo.</summary>
        public string? CameraMessage { get; init; }

        /// <summary>Nếu <c>true</c>, thông báo tự ẩn sau 3 giây. Nếu <c>false</c>, giữ cho đến khi form reset.</summary>
        public bool MessageAutoHide { get; init; } = true;

        /// <summary>Cần bắt đầu đếm ngược timeout chờ xe lên cân.</summary>
        public bool ShouldStartPendingTimeout { get; init; }

        /// <summary>Cần lưu phiếu ngay (cân đã ổn định khi RFID quét).</summary>
        public bool ShouldSave { get; init; }

        /// <summary>Trọng lượng cần lưu (kg).</summary>
        public decimal WeightToSave { get; init; }

        /// <summary>Thông tin xe (dùng khi <see cref="ShouldSave"/> = true).</summary>
        public PendingVehicleData? PendingVehicle { get; init; }

        /// <summary>Chỉ hiển thị thông báo ngắn (thẻ chưa đăng ký, chế độ tay, ...).</summary>
        public static RfidWorkflowDecision Message(string message) => new()
        {
            ShouldShowMessage = true,
            CameraMessage     = message
        };

        /// <summary>Xe vào hàng chờ; bắt đầu đếm ngược timeout.</summary>
        public static RfidWorkflowDecision Pending(string message) => new()
        {
            ShouldShowMessage         = true,
            CameraMessage             = message,
            MessageAutoHide           = false,
            ShouldStartPendingTimeout = true
        };

        /// <summary>Lưu phiếu ngay với trọng lượng hiện tại và dữ liệu xe đã xác định.</summary>
        public static RfidWorkflowDecision SaveNow(decimal weight, PendingVehicleData pending) => new()
        {
            ShouldSave     = true,
            WeightToSave   = weight,
            PendingVehicle = pending
        };
    }
}
