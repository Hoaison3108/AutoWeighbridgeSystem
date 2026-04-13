using AutoWeighbridgeSystem.Data;
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
            RfidBusinessService rfidBusiness)
        {
            _dbContextFactory = dbContextFactory;
            _rfidService = rfidService;
            _scaleService = scaleService;
            _rfidBusiness = rfidBusiness; // Gán giá trị tiêm vào

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
                MessageBox.Show($"Khối lượng hiện tại ({currentWeight:N0} kg) thấp hơn ngưỡng tối thiểu ({_minWeightThreshold:N0} kg).",
                                "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    // Nếu xe ĐÃ ĐĂNG KÝ: Chuyển sang chế độ xem/sửa, load thông tin lên Grid
                    SelectedRecord = RegisteredVehicles.FirstOrDefault(v => v.VehicleId == result.ExistingVehicle.VehicleId);
                }
                else
                {
                    // Nếu là THẺ HOÀN TOÀN MỚI
                    ClearForm();

                    // Ép UI cập nhật bằng cách gán nguyên Object mới
                    NewVehicle = new Vehicle
                    {
                        RfidCardId = result.CleanCardId,
                        LicensePlate = "",
                        TareWeight = 0
                    };

                    // Bắt buộc WPF Binding phải đọc lại giá trị
                    OnPropertyChanged(nameof(NewVehicle));
                    OnPropertyChanged("NewVehicle.RfidCardId");
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
            NewVehicle.RfidCardId = NewVehicle.RfidCardId?.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(NewVehicle.LicensePlate))
            {
                MessageBox.Show("Vui lòng nhập biển số!"); return;
            }

            if (SelectedCustomer == null)
            {
                MessageBox.Show("Vui lòng chọn khách hàng từ danh sách!"); return;
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
                            if (MessageBox.Show("Xe này đã bị xóa trước đó. Khôi phục?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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
                            MessageBox.Show("Xe này đã tồn tại!"); return;
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
                MessageBox.Show("Lưu thông tin thành công!");
            }
            catch (Exception ex) { MessageBox.Show("Lỗi: " + ex.Message); }
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
            if (MessageBox.Show("Xác nhận xóa?", "Xác nhận", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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