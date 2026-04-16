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

        [ObservableProperty] private Vehicle _newVehicle = new();
        [ObservableProperty] private ObservableCollection<Vehicle> _registeredVehicles = new();
        [ObservableProperty] private ObservableCollection<Customer> _allCustomers = new();
        [ObservableProperty] private Customer _selectedCustomer;
        [ObservableProperty] private Vehicle _selectedRecord;
        [ObservableProperty] private bool _isEditMode = false;

        // CẬP NHẬT 1: Thêm RfidBusinessService vào tham số Constructor
        public VehicleRegistrationViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            RfidMultiService rfidService,
            ScaleService scaleService,
            IConfiguration configuration,
            RfidBusinessService rfidBusiness,
            IUserNotificationService notificationService)
        {
            _dbContextFactory = dbContextFactory;
            _rfidService = rfidService;
            _scaleService = scaleService;
            _rfidBusiness = rfidBusiness; // Gán giá trị tiêm vào
            _notificationService = notificationService;

            if (!decimal.TryParse(configuration["ScaleSettings:MinWeightThreshold"], out _minWeightThreshold))
            {
                _minWeightThreshold = 200;
            }

            _rfidService.CardRead += OnCardReadAtDesk;
            _ = LoadDataAsync();
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

        // CẬP NHẬT 2: Gọi Module nghiệp vụ thay vì tự xử lý
        private async Task ProcessScannedCardAsync(string cardId)
        {
            try
            {
                // Gọi sang Tầng Business Logic để lấy mã sạch và kiểm tra DB
                var result = await _rfidBusiness.ProcessRawCardAsync(cardId);

                // Nếu có lỗi (VD: không tìm thấy số trong chuỗi)
                if (!result.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"[RFID Lỗi]: {result.ErrorMessage}");
                    return;
                }

                if (result.ExistingVehicle != null)
                {
                    if (IsEditMode)
                    {
                        if (result.ExistingVehicle.VehicleId == NewVehicle.VehicleId)
                        {
                            _notificationService.ShowInfo("Thẻ này đã và đang được gắn cho chính xe này.");
                        }
                        else
                        {
                            _notificationService.ShowWarning($"Thẻ rác: Thẻ này đang bị gán cho xe {result.ExistingVehicle.LicensePlate}. Vui lòng sử dụng thẻ trắng mới!");
                        }
                    }
                    else
                    {
                        // Nếu đang ở chế độ xem tự do: Tự động trỏ tới xe tìm được
                        SelectedRecord = RegisteredVehicles.FirstOrDefault(v => v.VehicleId == result.ExistingVehicle.VehicleId);
                    }
                }
                else
                {
                    // THẺ RỖNG (CHƯA ĐĂNG KÝ CHO AI)
                    if (IsEditMode)
                    {
                        // Lẳng lặng điền thẻ từ vào giao diện mà không xóa Form
                        NewVehicle.RfidCardId = result.CleanCardId;
                        OnPropertyChanged(nameof(NewVehicle));
                        OnPropertyChanged("NewVehicle.RfidCardId");
                        _notificationService.ShowInfo("Đã nạp mã thẻ mới. Vui lòng bấm LƯU để chốt gán thẻ cho xe này.");
                    }
                    else
                    {
                        // Khởi tạo phôi xe mới tinh
                        ClearForm();
                        NewVehicle = new Vehicle
                        {
                            RfidCardId = result.CleanCardId,
                            LicensePlate = "",
                            TareWeight = 0
                        };

                        OnPropertyChanged(nameof(NewVehicle));
                        OnPropertyChanged("NewVehicle.RfidCardId");
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
            NewVehicle.LicensePlate = FormatLicensePlate(NewVehicle.LicensePlate);
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
                await db.SaveChangesAsync();
                await LoadDataAsync();
                ClearForm();
                _notificationService.ShowInfo(UiText.Messages.VehicleSaveSuccess);
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
        private void ClearForm()
        {
            NewVehicle = new Vehicle();
            SelectedCustomer = null;
            SelectedRecord = null;
            IsEditMode = false;
        }

        private async Task LoadDataAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Customer).ToListAsync();
            var customers = await db.Customers.AsNoTracking().ToListAsync();

            Application.Current?.Dispatcher.Invoke(() => {
                RegisteredVehicles = new ObservableCollection<Vehicle>(vehicles);
                AllCustomers = new ObservableCollection<Customer>(customers);
            });
        }

        public void Dispose()
        {
            _rfidService.CardRead -= OnCardReadAtDesk;
        }

        private string FormatLicensePlate(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string raw = input.ToUpper().Replace(" ", "").Replace(".", "").Replace("-", "");
            var match = Regex.Match(raw, @"^([0-9]{2}[A-Z]{1,2})([0-9]{4,5})$");
            return match.Success ? $"{match.Groups[1].Value}-{match.Groups[2].Value}" : input.Trim().ToUpper();
        }
    }
}