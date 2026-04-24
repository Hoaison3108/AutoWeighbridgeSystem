using System;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Common;
using Serilog;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ chạy ngầm cấp hệ thống để xử lý các tác vụ Tự động hóa (True Auto).
    /// Đảm bảo việc Cân xe và Đăng ký xe tự động diễn ra 24/7 bất kể View nào đang hiển thị.
    /// </summary>
    public sealed class BackgroundAutomationService : IDisposable
    {
        private readonly DashboardEventCoordinator _coordinator;
        private readonly DashboardSaveService _saveService;
        private readonly RfidMultiService _rfidService;
        private readonly RfidBusinessService _rfidBusiness;
        private readonly ScaleService _scaleService;
        private readonly IConfiguration _configuration;
        private readonly IUserNotificationService _notificationService;
        private readonly AlarmService _alarmService;
        private readonly ViewTrackerService _viewTracker;

        /// <summary>Sự kiện báo cho các UI (Dashboard/Registration) biết cần làm mới danh sách dữ liệu.</summary>
        public event Action? DataChanged;

        public BackgroundAutomationService(
            DashboardEventCoordinator coordinator,
            DashboardSaveService saveService,
            RfidMultiService rfidService,
            RfidBusinessService rfidBusiness,
            ScaleService scaleService,
            IConfiguration configuration,
            IUserNotificationService notificationService,
            AlarmService alarmService,
            ViewTrackerService viewTracker)
        {
            _coordinator = coordinator;
            _saveService = saveService;
            _rfidService = rfidService;
            _rfidBusiness = rfidBusiness;
            _scaleService = scaleService;
            _configuration = configuration;
            _notificationService = notificationService;
            _alarmService = alarmService;
            _viewTracker = viewTracker;

            // 1. Đăng ký lắng nghe Cân tự động
            _coordinator.AutoSaveRequested += OnAutoSaveRequested;

            // 2. Đăng ký lắng nghe Đăng ký xe tự động (tại bàn)
            _rfidService.CardRead += OnRfidCardReadAtDesk;

            Log.Information("[AUTOMATION] Background Automation Service đã khởi động.");
        }

        // =========================================================================
        // XỬ LÝ CÂN TỰ ĐỘNG (DASHBOARD AUTO)
        // =========================================================================
        private async Task OnAutoSaveRequested(decimal weight, PendingVehicleData pendingData)
        {
            Log.Information("[AUTOMATION] Nhận yêu cầu Lưu phiếu tự động cho xe: {Plate}", pendingData.LicensePlate);

            var request = new DashboardSaveRequest(
                LicensePlate: pendingData.LicensePlate,
                CustomerName: pendingData.CustomerName,
                ProductName: pendingData.ProductName,
                FinalWeight: weight,
                IsOnePassMode: pendingData.IsOnePassMode
            );

            var result = await _saveService.ExecuteSaveAsync(request);

            if (result.IsSuccess)
            {
                Log.Information("[AUTOMATION] Đã lưu phiếu tự động thành công: {Plate}", pendingData.LicensePlate);
                
                // CHỈ HIỆN TOAST NẾU NGƯỜI DÙNG ĐANG KHÔNG Ở TAB DASHBOARD
                if (_viewTracker.CurrentView != ViewType.Dashboard)
                {
                    _notificationService.ShowInfo($"[AUTO] Đã lưu phiếu cân cho xe {pendingData.LicensePlate}: {result.FinalWeight:N0} kg");
                }
                
                // Thông báo cho UI làm mới danh sách (vẫn làm mới kể cả đang ở tab đó để nhảy số)
                DataChanged?.Invoke();
            }
            else
            {
                Log.Warning("[AUTOMATION] Lưu phiếu tự động thất bại: {Message}", result.Message);
            }
        }

        // =========================================================================
        // XỬ LÝ ĐĂNG KÝ TỰ ĐỘNG (REGISTRATION AUTO)
        // =========================================================================
        private void OnRfidCardReadAtDesk(string readerRole, string cardId)
        {
            // Chỉ xử lý đầu đọc tại bàn (Desk) cho mục đích đăng ký
            if (readerRole != ReaderRoles.Desk) return;

            // Kiểm tra cấu hình có cho phép Đăng ký Auto hay không
            bool isAutoMode = bool.TryParse(_configuration["ScaleSettings:RegistrationDefaultAutoMode"], out bool ram) ? ram : true;
            if (!isAutoMode) return;

            // Chạy trong Task để tránh block luồng nhận RFID
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _rfidBusiness.ProcessRawCardAsync(cardId);
                    
                    // Chỉ xử lý xe ĐÃ ĐĂNG KÝ thẻ (Cập nhật bì tự động)
                    if (result.IsSuccess && result.ExistingVehicle != null)
                    {
                        decimal currentWeight = _scaleService.CurrentWeight;
                        bool isStable = _scaleService.IsScaleStable;
                        decimal minThreshold = decimal.TryParse(_configuration["ScaleSettings:MinWeightThreshold"], out decimal mt) ? mt : 200;

                        if (currentWeight >= minThreshold && isStable)
                        {
                            Log.Information("[AUTOMATION] Cập nhật bì tự động cho xe: {Plate}, Cân nặng: {Weight}", 
                                result.ExistingVehicle.LicensePlate, currentWeight);
                            
                            // Thực hiện cập nhật vào DB
                            var success = await _rfidBusiness.UpdateVehicleTareWeightAsync(result.ExistingVehicle.VehicleId, currentWeight);
                            
                            if (success)
                            {
                                await _alarmService.TriggerAlarmAsync();
                                
                                // CHỈ HIỆN TOAST NẾU NGƯỜI DÙNG ĐANG KHÔNG Ở TAB ĐĂNG KÝ XE
                                if (_viewTracker.CurrentView != ViewType.VehicleRegistration)
                                {
                                    _notificationService.ShowInfo($"[AUTO] Đã cập nhật bì tự động cho xe {result.ExistingVehicle.LicensePlate}: {currentWeight:N0} kg");
                                }
                                
                                DataChanged?.Invoke();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[AUTOMATION] Lỗi xử lý đăng ký xe tự động ngầm");
                }
            });
        }

        public void Dispose()
        {
            _coordinator.AutoSaveRequested -= OnAutoSaveRequested;
            _rfidService.CardRead -= OnRfidCardReadAtDesk;
            Log.Information("[AUTOMATION] Background Automation Service đã dừng.");
        }
    }
}
