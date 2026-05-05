using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Text.RegularExpressions;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class VehicleRegistrationViewModel : ObservableObject, IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly RfidMultiService _rfidService;
        private readonly ScaleService _scaleService;
        private readonly decimal _minWeightThreshold;
        private readonly RfidBusinessService _rfidBusiness;
        private readonly IUserNotificationService _notificationService;
        private readonly AlarmService _alarmService;
        private readonly BackgroundAutomationService _automationService;

        private bool _isRfidAssignIntent = false;

        [ObservableProperty] private Vehicle _newVehicle = new();
        [ObservableProperty] private ObservableCollection<Vehicle> _registeredVehicles = new();
        [ObservableProperty] private ObservableCollection<Customer> _allCustomers = new();
        [ObservableProperty] private Customer? _selectedCustomer;
        [ObservableProperty] private Vehicle? _selectedRecord;
        [ObservableProperty] private bool _isEditMode = false;
        [ObservableProperty] private bool _syncTareWeightToAll = false;
        [ObservableProperty] private bool _isAutoMode = false;
        [ObservableProperty] private string _searchText = "";

        public AutocompleteProvider<string> VehicleAutocomplete { get; } = new(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));
        public System.ComponentModel.ICollectionView RegisteredVehiclesView { get; private set; }

        public VehicleRegistrationViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            RfidMultiService rfidService,
            ScaleService scaleService,
            IConfiguration configuration,
            RfidBusinessService rfidBusiness,
            IUserNotificationService notificationService,
            AlarmService alarmService,
            BackgroundAutomationService automationService)
        {
            _dbContextFactory = dbContextFactory;
            _rfidService = rfidService;
            _scaleService = scaleService;
            _rfidBusiness = rfidBusiness;
            _notificationService = notificationService;
            _alarmService = alarmService;
            _automationService = automationService;

            if (!decimal.TryParse(configuration["ScaleSettings:MinWeightThreshold"], out _minWeightThreshold)) _minWeightThreshold = 200;
            IsAutoMode = bool.TryParse(configuration["ScaleSettings:RegistrationDefaultAutoMode"], out bool ram) ? ram : true;

            _rfidService.CardRead              += OnCardReadAtDesk;
            _rfidService.ReaderReconnected     += OnReaderReconnected;

            // Lắng nghe chu kỳ Ngủ/Thức của bàn cân để điều khiển Desk trong Auto Mode.
            // Trong Manual Mode, handler này sẽ được bỏ qua (Desk luôn thức).
            _rfidService.AutoReadersStateChanged += OnAutoReadersStateChanged;
            
            RegisteredVehiclesView = System.Windows.Data.CollectionViewSource.GetDefaultView(RegisteredVehicles);
            RegisteredVehiclesView.Filter = v =>
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                if (v is not Vehicle vehicle) return true;
                string search = SearchText.ToLower();
                return vehicle.LicensePlate.ToLower().Contains(search) || (vehicle.Customer?.CustomerName?.ToLower().Contains(search) ?? false);
            };

            _automationService.DataChanged += OnBackgroundDataChanged;
            _ = LoadDataAsync();
        }

        /// <summary>
        /// Gọi mỗi khi một đầu đọc kết nối (lần đầu hoặc sau khi mất kết nối và kết nối lại).
        /// Desk khởi động vào trạng thái đúng tương ứng với Mode hiện tại:
        /// - Auto Mode: Ngủ (chờ khối lượng cân kích hoạt).
        /// - Manual Mode: Thức ngay (để nhận thẻ bất cứ lúc nào).
        /// </summary>
        private void OnReaderReconnected(string roleName)
        {
            if (roleName != ReaderRoles.Desk) return;

            // Auto Mode: Desk ngủ, đợi xe lên cân. Manual Mode: Desk thức luôn.
            bool shouldBeActive = !IsAutoMode;
            Log.Information("[REGISTRATION] Desk reader vừa kết nối — Đặt trạng thái: {State} (Mode: {Mode})",
                shouldBeActive ? "BẬT SÓNG" : "NGỦ CHờ CÂN",
                IsAutoMode ? "Auto" : "Manual");
            _rfidService.SetDeskReaderActive(shouldBeActive);
        }

        /// <summary>
        /// Gọi khi bàn cân bật/tắt chu kỳ phát sóng RFID (xe lên/xuống cân).
        /// Trong Auto Mode: Desk đi theo chu kỳ bàn cân (để tài xế tự quẹt thẻ).
        /// Trong Manual Mode: Bỏ qua — Desk luôn thức, không phụ thuộc bàn cân.
        /// </summary>
        private void OnAutoReadersStateChanged(bool isActive)
        {
            if (!IsAutoMode) return; // Manual Mode: không can thiệp vào trạng thái Desk

            Log.Debug("[REGISTRATION] Auto Mode — Desk theo cân: {State}", isActive ? "BẬT SÓNG" : "NGỦ");
            _rfidService.SetDeskReaderActive(isActive);
        }

        private void OnBackgroundDataChanged(string message) => _ = LoadDataAsync();

        partial void OnSearchTextChanged(string value) => RegisteredVehiclesView.Refresh();

        partial void OnSelectedRecordChanged(Vehicle value)
        {
            if (value != null)
            {
                NewVehicle = new Vehicle { VehicleId = value.VehicleId, LicensePlate = value.LicensePlate, RfidCardId = value.RfidCardId, TareWeight = value.TareWeight, CustomerId = value.CustomerId };
                SelectedCustomer = AllCustomers.FirstOrDefault(c => c.CustomerId == value.CustomerId);
                IsEditMode = true;
                _isRfidAssignIntent = false;
            }
        }

        [RelayCommand]
        private void GetWeightFromScale()
        {
            decimal currentWeight = _scaleService.CurrentWeight;
            if (currentWeight < _minWeightThreshold) { _notificationService.ShowWarning($"Cân nặng {currentWeight:N0} thấp hơn ngưỡng."); return; }
            NewVehicle.TareWeight = currentWeight;
            OnPropertyChanged(nameof(NewVehicle));
        }

        partial void OnIsAutoModeChanged(bool value)
        {
            if (value)
            {
                // Auto Mode ON: Desk ngủ, đợi bàn cân kích hoạt (weight > 200kg)
                SyncTareWeightToAll = true;
                _rfidService.SetDeskReaderActive(false);
                Log.Information("[REGISTRATION] Chuyển sang Auto Mode — Desk NGỦ, chờ xe lên cân.");
            }
            else
            {
                // Manual Mode: Desk luôn thức, không phụ thuộc bàn cân
                _rfidService.SetDeskReaderActive(true);
                Log.Information("[REGISTRATION] Chuyển sang Manual Mode — Desk luôn THỨC.");
            }
        }

        private void OnCardReadAtDesk(string readerRole, string cardId)
        {
            if (readerRole == ReaderRoles.Desk) Application.Current?.Dispatcher.InvokeAsync(async () => await ProcessScannedCardAsync(cardId));
        }

        private async Task ProcessScannedCardAsync(string cardId)
        {
            var result = await _rfidBusiness.ProcessRawCardAsync(cardId);
            if (!result.IsSuccess) return;

            if (result.ExistingVehicle != null)
            {
                if (IsEditMode && _isRfidAssignIntent)
                {
                    // Đang ở chế độ gán thẻ thủ công — không cho phép Auto Mode can thiệp
                    if (result.ExistingVehicle.VehicleId != NewVehicle.VehicleId) _notificationService.ShowWarning("Thẻ đang gán cho xe khác!");
                    _isRfidAssignIntent = false;
                }
                else
                {
                    // Tải dữ liệu xe lên form
                    SelectedRecord = RegisteredVehicles.FirstOrDefault(v => v.VehicleId == result.ExistingVehicle.VehicleId);

                    // =========================================================
                    // AUTO MODE: Lấy bì → Lưu DB → Kích chuông, không cần thao tác tay
                    // =========================================================
                    if (IsAutoMode)
                    {
                        // Bước 1: Lấy khối lượng thực tế từ bàn cân tại thời điểm quẹt thẻ
                        decimal currentWeight = _scaleService.CurrentWeight;
                        if (currentWeight < _minWeightThreshold)
                        {
                            _notificationService.ShowWarning($"Auto Mode: Bì không hợp lệ ({currentWeight:N0} kg). Xe chưa lên cân hoặc trọng lượng quá thấp.");
                            return;
                        }

                        // Ghi bì mới vào đối tượng xe đang được chọn trên form
                        NewVehicle.TareWeight = currentWeight;
                        OnPropertyChanged(nameof(NewVehicle));

                        // Bước 2: Lưu tự động vào DB (đồng bộ bì cho các xe cùng biển số nếu SyncTareWeightToAll = true)
                        await SaveAsync();

                        // Bước 3: Kích chuông báo hiệu lưu thành công
                        _ = Task.Run(() => _alarmService.TriggerAlarmAsync());
                    }
                }
            }
            else
            {
                if (IsEditMode) { NewVehicle.RfidCardId = result.CleanCardId; _isRfidAssignIntent = true; }
                else { ClearForm(); NewVehicle = new Vehicle { RfidCardId = result.CleanCardId }; _isRfidAssignIntent = true; }
                OnPropertyChanged(nameof(NewVehicle));
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            NewVehicle.LicensePlate = NewVehicle.LicensePlate.FormatLicensePlate();
            if (string.IsNullOrWhiteSpace(NewVehicle.LicensePlate)) { _notificationService.ShowWarning("Vui lòng nhập biển số."); return; }
            if (SelectedCustomer == null) { _notificationService.ShowWarning("Vui lòng chọn khách hàng."); return; }
            
            NewVehicle.CustomerId = SelectedCustomer.CustomerId;
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                if (!IsEditMode)
                {
                    var existing = await db.Vehicles.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.LicensePlate == NewVehicle.LicensePlate && v.CustomerId == NewVehicle.CustomerId);
                    if (existing != null) { _notificationService.ShowWarning("Xe đã tồn tại."); return; }
                    db.Vehicles.Add(NewVehicle);
                }
                else
                {
                    var v = await db.Vehicles.FindAsync(NewVehicle.VehicleId);
                    if (v != null) { v.LicensePlate = NewVehicle.LicensePlate; v.RfidCardId = NewVehicle.RfidCardId; v.TareWeight = NewVehicle.TareWeight; v.CustomerId = NewVehicle.CustomerId; db.Vehicles.Update(v); }
                }

                if (SyncTareWeightToAll)
                {
                    var others = await db.Vehicles.Where(v => v.LicensePlate == NewVehicle.LicensePlate && v.VehicleId != NewVehicle.VehicleId).ToListAsync();
                    foreach (var o in others) { o.TareWeight = NewVehicle.TareWeight; db.Vehicles.Update(o); }
                }

                await db.SaveChangesAsync();
                await LoadDataAsync();
                ClearForm();
            }
            catch (Exception ex) { _notificationService.ShowError(ex.Message); }
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (NewVehicle.VehicleId == 0 || !_notificationService.Confirm(UiText.Messages.VehicleDeleteConfirm, UiText.Titles.Confirm)) return;
            using var db = _dbContextFactory.CreateDbContext();
            var v = await db.Vehicles.FindAsync(NewVehicle.VehicleId);
            if (v != null) { v.IsDeleted = true; db.Vehicles.Update(v); await db.SaveChangesAsync(); await LoadDataAsync(); ClearForm(); }
        }

        [RelayCommand] private async Task TriggerManualAlarmAsync() => await _alarmService.TriggerAlarmAsync();

        [RelayCommand]
        private void ClearForm()
        {
            NewVehicle = new Vehicle(); SelectedCustomer = null; SelectedRecord = null; IsEditMode = false; SyncTareWeightToAll = false; _isRfidAssignIntent = false;
        }

        [RelayCommand]
        private async Task ClearRfid()
        {
            // Xe chưa lưu vào DB (đang tạo mới) — chỉ xóa trên form, không cần lưu
            if (NewVehicle.VehicleId == 0)
            {
                NewVehicle.RfidCardId = null;
                OnPropertyChanged(nameof(NewVehicle));
                _isRfidAssignIntent = false;
                return;
            }

            // Xe đã có trong DB — xác nhận trước khi gỡ thẻ vĩnh viễn
            if (!_notificationService.Confirm(
                    "Xác nhận gỡ thẻ RFID khỏi xe này?\nThao tác sẽ được lưu vào hệ thống ngay lập tức.",
                    "Xác nhận Gỡ Thẻ"))
                return;

            NewVehicle.RfidCardId = null;
            OnPropertyChanged(nameof(NewVehicle));
            _isRfidAssignIntent = false;

            // Lưu thẳng vào Database — không cần bấm thêm "Cập nhật Thông tin"
            await SaveAsync();
        }

        private async Task LoadDataAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Customer).ToListAsync();
            var customers = await db.Customers.AsNoTracking().ToListAsync();

            Application.Current?.Dispatcher.Invoke(() => {
                RegisteredVehicles.Clear();
                foreach (var v in vehicles) RegisteredVehicles.Add(v);
                AllCustomers = new ObservableCollection<Customer>(customers);
                VehicleAutocomplete.UpdateItems(vehicles.Select(v => v.LicensePlate).Distinct().ToArray());
            });
        }

        public void Dispose()
        {
            _rfidService.SetDeskReaderActive(false);
            _rfidService.AutoReadersStateChanged -= OnAutoReadersStateChanged;
            _rfidService.ReaderReconnected       -= OnReaderReconnected;
            _rfidService.CardRead                -= OnCardReadAtDesk;
            _automationService.DataChanged       -= OnBackgroundDataChanged;
        }
    }
}