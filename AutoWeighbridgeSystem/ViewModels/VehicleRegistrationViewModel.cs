using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

        // Đã khai báo RfidBusinessService
        private readonly RfidBusinessService _rfidBusiness;
        private readonly IUserNotificationService _notificationService;
        private readonly AlarmService _alarmService;

        private Vehicle _pendingAutoVehicle; // Xe đang chờ cân ổn định để lưu tự động
        private readonly object _pendingLock = new object();

        [ObservableProperty] private Vehicle _newVehicle = new();
        [ObservableProperty] private ObservableCollection<Vehicle> _registeredVehicles = new();
        [ObservableProperty] private ObservableCollection<Customer> _allCustomers = new();
        [ObservableProperty] private Customer _selectedCustomer;
        [ObservableProperty] private Vehicle _selectedRecord;
        [ObservableProperty] private bool _isEditMode = false;
        [ObservableProperty] private bool _syncTareWeightToAll = false;
        [ObservableProperty] private bool _isAutoMode = false;

        // --- AUTOCOMPLETE & SEARCH ---
        [ObservableProperty] private string _searchText = "";

        /// <summary>
        /// Gợi ý biển số xe khi đăng ký/tìm kiếm (giống Dashboard).
        /// </summary>
        public AutocompleteProvider<string> VehicleAutocomplete { get; }
            = new AutocompleteProvider<string>(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// View để lọc danh sách DataGrid bên phải dựa trên SearchText.
        /// </summary>
        public System.ComponentModel.ICollectionView RegisteredVehiclesView { get; private set; }

        // CẬP NHẬT 1: Thêm RfidBusinessService vào tham số Constructor
        public VehicleRegistrationViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            RfidMultiService rfidService,
            ScaleService scaleService,
            IConfiguration configuration,
            RfidBusinessService rfidBusiness,
            IUserNotificationService notificationService,
            AlarmService alarmService)
        {
            _dbContextFactory = dbContextFactory;
            _rfidService = rfidService;
            _scaleService = scaleService;
            _rfidBusiness = rfidBusiness;
            _notificationService = notificationService;
            _alarmService = alarmService;

            if (!decimal.TryParse(configuration["ScaleSettings:MinWeightThreshold"], out _minWeightThreshold))
            {
                _minWeightThreshold = 200;
            }

            _rfidService.CardRead += OnCardReadAtDesk;
            _scaleService.WeightChanged += OnScaleWeightChanged;

            // Khởi tạo View Collection để lọc DataGrid
            RegisteredVehiclesView = System.Windows.Data.CollectionViewSource.GetDefaultView(RegisteredVehicles);
            RegisteredVehiclesView.Filter = v =>
            {
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                var vehicle = v as Vehicle;
                if (vehicle == null) return true;
                string search = SearchText.ToLower();
                return vehicle.LicensePlate.ToLower().Contains(search) ||
                       (vehicle.Customer?.CustomerName?.ToLower().Contains(search) ?? false) ||
                       (vehicle.RfidCardId?.ToLower().Contains(search) ?? false);
            };

            _ = LoadDataAsync();
        }

        partial void OnSearchTextChanged(string value)
        {
            RegisteredVehiclesView.Refresh();
        }

        partial void OnSelectedRecordChanged(Vehicle value)
        {
            if (value != null)
            {
                NewVehicle = new Vehicle
                {
                    VehicleId = value.VehicleId,
                    LicensePlate = value.LicensePlate,
                    RfidCardId = value.RfidCardId,
                    TareWeight = value.TareWeight,
                    CustomerId = value.CustomerId
                };

                SelectedCustomer = AllCustomers.FirstOrDefault(c => c.CustomerId == value.CustomerId);
                IsEditMode = true;
                _isRfidAssignIntent = false; // xem thông tin xe, chưa có ý định gán thẻ
            }
        }

        [RelayCommand]
        private void GetWeightFromScale()
        {
            decimal currentWeight = _scaleService.CurrentWeight;

            if (currentWeight < _minWeightThreshold)
            {
                _notificationService.ShowWarning(
                    UiText.Messages.ScaleBelowThreshold(currentWeight, _minWeightThreshold),
                    UiText.Titles.Warning);
                return;
            }

            NewVehicle.TareWeight = currentWeight;
            OnPropertyChanged(nameof(NewVehicle));
        }

        // =========================================================================
        // RFID ASSIGN INTENT — khác IsEditMode (chỉ là trạng thái UI form)
        // IsRfidAssignIntent = true chỉ khi người dùng đang chủ động muốn gán/đổi thẻ
        // =========================================================================

        /// <summary>
        /// True khi người dùng đang trong luồng gán thẻ (form mở + xe chưa có thẻ,
        /// hoặc vừa nhận thẻ trắng để gán). Chỉ khi true mới cảnh báo "thẻ rác/xung đột".
        /// </summary>
        private bool _isRfidAssignIntent = false;

        partial void OnIsAutoModeChanged(bool value)
        {
            if (value) SyncTareWeightToAll = true;
            else _pendingAutoVehicle = null; // Tắt Auto thì hủy hàng đợi
        }

        private void OnScaleWeightChanged(decimal weight, bool isStable)
        {
            if (!IsAutoMode || _pendingAutoVehicle == null || !isStable) return;

            // Nếu xe đã dừng hẳn và đang có xe chờ lưu
            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                Vehicle vehicleToSave = null;
                lock (_pendingLock)
                {
                    if (_pendingAutoVehicle == null) return;
                    vehicleToSave = _pendingAutoVehicle;
                    _pendingAutoVehicle = null; // Clear ngay để tránh lưu 2 lần
                }

                if (weight >= _minWeightThreshold)
                {
                    NewVehicle.TareWeight = weight;
                    OnPropertyChanged(nameof(NewVehicle));
                    await SaveAsync();
                    await _alarmService.TriggerAlarmAsync();
                    _notificationService.ShowInfo($"[AUTO] Xe dừng hẳn - Đã chốt bì: {weight:N0} kg");
                }
            });
        }

        private void OnCardReadAtDesk(string readerRole, string cardId)
        {
            if (readerRole.Equals(ReaderRoles.Desk, StringComparison.OrdinalIgnoreCase))
            {
                Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    await ProcessScannedCardAsync(cardId);
                });
            }
        }

        // =========================================================================
        // XỬ LÝ QUẸT THẺ RFID TẠI BÀN
        // =========================================================================
        private async Task ProcessScannedCardAsync(string cardId)
        {
            try
            {
                var result = await _rfidBusiness.ProcessRawCardAsync(cardId);

                if (!result.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"[RFID Lỗi]: {result.ErrorMessage}");
                    return;
                }

                if (result.ExistingVehicle != null)
                {
                    // ── THẺ ĐÃ ĐƯỢC GÁN CHO MỘT XE ──────────────────────────────────
                    if (IsEditMode && _isRfidAssignIntent)
                    {
                        // Người dùng đang chủ động muốn gán thẻ này vào form
                        if (result.ExistingVehicle.VehicleId == NewVehicle.VehicleId)
                        {
                            // Thẻ chính là thẻ hiện tại của xe này — không cần đổi
                            _notificationService.ShowInfo("Thẻ này đã được gắn cho chính xe này.");
                            _isRfidAssignIntent = false;
                        }
                        else
                        {
                            // Thẻ thuộc xe khác — cảnh báo thật sự
                            _notificationService.ShowWarning(
                                $"Thẻ xung đột: Thẻ này đang được gán cho xe {result.ExistingVehicle.LicensePlate}. " +
                                $"Vui lòng dùng thẻ chưa được đăng ký!");
                        }
                    }
                    else
                    {
                        // Không có intent gán — quẹt để tra cứu: tự động trỏ đến xe chủ thẻ
                        SelectedRecord = RegisteredVehicles.FirstOrDefault(
                            v => v.VehicleId == result.ExistingVehicle.VehicleId);
                    }

                    // --- LOGIC AUTO MODE (Dành cho xe ĐÃ CÓ thẻ) ---
                    if (IsAutoMode)
                    {
                        decimal currentWeight = _scaleService.CurrentWeight;
                        bool isStable = _scaleService.IsScaleStable;

                        if (currentWeight >= _minWeightThreshold)
                        {
                            if (isStable)
                            {
                                // 1. Điền thông tin lên UI ngay (đã làm ở trên qua SelectedRecord)
                                // 2. Cập nhật cân nặng & Lưu
                                NewVehicle.TareWeight = currentWeight;
                                OnPropertyChanged(nameof(NewVehicle));
                                await SaveAsync();
                                await _alarmService.TriggerAlarmAsync();
                            }
                            else
                            {
                                // Đưa vào hàng chờ ổn định
                                lock (_pendingLock)
                                {
                                    _pendingAutoVehicle = result.ExistingVehicle;
                                }
                                _notificationService.ShowInfo($"Đã nhận diện xe {result.ExistingVehicle.LicensePlate} - Đang chờ cân ổn định...");
                            }
                        }
                        else
                        {
                            _notificationService.ShowWarning(
                                UiText.Messages.ScaleBelowThreshold(currentWeight, _minWeightThreshold));
                        }
                    }
                }
                else
                {
                    // ── THẺ TRẮNG (CHƯA ĐĂNG KÝ CHO AI) ─────────────────────────────
                    if (IsEditMode)
                    {
                        // Điền thẻ vào form hiện tại — bật intent vì vừa nhận thẻ mới
                        NewVehicle.RfidCardId = result.CleanCardId;
                        OnPropertyChanged(nameof(NewVehicle));
                        OnPropertyChanged("NewVehicle.RfidCardId");
                        _isRfidAssignIntent = true;  // từ giờ nếu quẹt thẻ khác thì cảnh báo
                        _notificationService.ShowInfo("Đã nạp mã thẻ mới. Vui lòng bấm LƯU để chốt gán thẻ.");
                    }
                    else
                    {
                        // Free-view: khởi tạo form thêm mới với thẻ này
                        ClearForm();
                        NewVehicle = new Vehicle
                        {
                            RfidCardId = result.CleanCardId,
                            LicensePlate = "",
                            TareWeight = 0
                        };
                        OnPropertyChanged(nameof(NewVehicle));
                        OnPropertyChanged("NewVehicle.RfidCardId");
                        _isRfidAssignIntent = true;  // form đang chờ gán thẻ này
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi ngoại lệ RFID ViewModel: " + ex.Message);
            }
        }


        [RelayCommand]
        private async Task SaveAsync()
        {
            NewVehicle.LicensePlate = NewVehicle.LicensePlate.FormatLicensePlate();
            // Ép xóa trắng về giá trị null chuẩn hóa chống Lỗi Cơ Sở Dữ Liệu
            NewVehicle.RfidCardId = string.IsNullOrWhiteSpace(NewVehicle.RfidCardId) ? null : NewVehicle.RfidCardId.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(NewVehicle.LicensePlate))
            {
                _notificationService.ShowWarning(UiText.Messages.EnterLicensePlate); return;
            }

            if (SelectedCustomer == null)
            {
                _notificationService.ShowWarning(UiText.Messages.SelectCustomerFromList); return;
            }
            NewVehicle.CustomerId = SelectedCustomer.CustomerId;

            try
            {
                using var db = _dbContextFactory.CreateDbContext();

                var existing = await db.Vehicles.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(v => v.LicensePlate == NewVehicle.LicensePlate && v.CustomerId == NewVehicle.CustomerId);

                if (!IsEditMode)
                {
                    if (existing != null)
                    {
                        if (existing.IsDeleted)
                        {
                            if (_notificationService.Confirm(UiText.Messages.RestoreDeletedVehicleConfirm, UiText.Titles.Confirm))
                            {
                                existing.IsDeleted = false;
                                existing.TareWeight = NewVehicle.TareWeight;
                                existing.RfidCardId = NewVehicle.RfidCardId;
                                db.Vehicles.Update(existing);
                            }
                            else return;
                        }
                        else
                        {
                            _notificationService.ShowWarning(UiText.Messages.VehicleAlreadyExists); return;
                        }
                    }
                    else
                    {
                        db.Vehicles.Add(NewVehicle);
                    }
                }
                else
                {
                    var v = await db.Vehicles.FindAsync(NewVehicle.VehicleId);
                    if (v != null)
                    {
                        v.LicensePlate = NewVehicle.LicensePlate;
                        v.RfidCardId = NewVehicle.RfidCardId;
                        v.TareWeight = NewVehicle.TareWeight;
                        v.CustomerId = NewVehicle.CustomerId;
                        db.Vehicles.Update(v);
                    }
                }

                // Đồng bộ khối lượng bì cho tất cả các bản ghi có chung Biển số xe
                if (SyncTareWeightToAll)
                {
                    var sameLicensePlateVehicles = await db.Vehicles
                        .Where(v => v.LicensePlate == NewVehicle.LicensePlate)
                        .ToListAsync();

                    foreach (var vehicle in sameLicensePlateVehicles)
                    {
                        // Bỏ qua bản ghi đang thao tác hiện tại
                        if (vehicle.VehicleId != NewVehicle.VehicleId)
                        {
                            vehicle.TareWeight = NewVehicle.TareWeight;
                            db.Vehicles.Update(vehicle);
                        }
                    }
                }

                await db.SaveChangesAsync();
                await LoadDataAsync();

                // Hiển thị thông báo chi tiết để người dùng xác nhận
                string detailMsg = $"Đã lưu thành công xe {NewVehicle.LicensePlate}\nKhối lượng thân vỏ: {NewVehicle.TareWeight:N0} kg";
                _notificationService.ShowInfo(detailMsg, "XÁC NHẬN LƯU");

                ClearForm();
            }
            catch (Exception ex) { _notificationService.ShowError(UiText.Messages.GenericError(ex.Message)); }
        }

        private async Task<Customer> GetOrCreateCustomerAsync(AppDbContext db, string customerName)
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.CustomerName.ToLower() == customerName.ToLower());

            if (customer == null)
            {
                string generatedId = "CUST-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                if (customerName == "Khách vãng lai") generatedId = "WALK-IN";

                customer = new Customer
                {
                    CustomerId = generatedId,
                    CustomerName = customerName
                };

                db.Customers.Add(customer);
                await db.SaveChangesAsync();
            }
            return customer;
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (NewVehicle.VehicleId == 0) return;
            if (_notificationService.Confirm(UiText.Messages.VehicleDeleteConfirm, UiText.Titles.Confirm))
            {
                using var db = _dbContextFactory.CreateDbContext();
                var v = await db.Vehicles.FindAsync(NewVehicle.VehicleId);
                if (v != null)
                {
                    v.IsDeleted = true;
                    db.Vehicles.Update(v);
                    await db.SaveChangesAsync();
                    await LoadDataAsync();
                    ClearForm();
                }
            }
        }

        [RelayCommand]
        private async Task TriggerManualAlarmAsync() => await _alarmService.TriggerAlarmAsync();

        [RelayCommand]
        private void ClearForm()
        {
            NewVehicle = new Vehicle();
            SelectedCustomer = null;
            SelectedRecord = null;
            IsEditMode = false;
            SyncTareWeightToAll = false; // Reset checkbox đồng bộ
            _isRfidAssignIntent = false; // reset intent khi xóa form
        }

        [RelayCommand]
        private void ClearRfid()
        {
            NewVehicle.RfidCardId = null;
            OnPropertyChanged(nameof(NewVehicle));
            OnPropertyChanged("NewVehicle.RfidCardId");

            // Tắt chế độ chờ gán thẻ để tránh cảnh báo thẻ rác
            _isRfidAssignIntent = false;

            _notificationService.ShowInfo("Đã gỡ mã thẻ trên Form. Vui lòng bấm LƯU để xác nhận thay đổi.");
        }

        private async Task LoadDataAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Customer).ToListAsync();
            var customers = await db.Customers.AsNoTracking().ToListAsync();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                RegisteredVehicles.Clear();
                foreach (var v in vehicles) RegisteredVehicles.Add(v);

                AllCustomers = new ObservableCollection<Customer>(customers);

                // Cập nhật danh sách gợi ý biển số (chỉ lấy biển không trùng lặp)
                var uniquePlates = vehicles.Select(v => v.LicensePlate).Distinct().ToArray();
                VehicleAutocomplete.UpdateItems(uniquePlates);
            });
        }

        public void Dispose()
        {
            _rfidService.CardRead -= OnCardReadAtDesk;
            _scaleService.WeightChanged -= OnScaleWeightChanged;
        }

    }
}