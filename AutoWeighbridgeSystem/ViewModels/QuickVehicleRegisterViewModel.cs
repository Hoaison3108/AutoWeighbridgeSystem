using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class QuickVehicleRegisterViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IUserNotificationService _notificationService;
        private readonly ScaleService _scaleService;

        public QuickVehicleRegisterViewModel(
            string licensePlate, 
            IDbContextFactory<AppDbContext> dbContextFactory,
            IUserNotificationService notificationService,
            ScaleService scaleService)
        {
            _dbContextFactory = dbContextFactory;
            _notificationService = notificationService;
            _scaleService = scaleService;

            LicensePlate = licensePlate;
            TareWeight = null; // Cho phép bỏ trống nếu xe có hàng
            
            _ = LoadCustomersAsync();
        }

        [ObservableProperty]
        private string _licensePlate;

        [ObservableProperty]
        private decimal? _tareWeight;

        [ObservableProperty]
        private ObservableCollection<Customer> _customers = new();

        [ObservableProperty]
        private Customer _selectedCustomer;

        /// <summary>Truyền hàm đóng cửa sổ từ UI xuống</summary>
        public Action CloseAction { get; set; }
        
        /// <summary>True nếu đã bấm Lưu thành công, luồng cân tiếp tục</summary>
        public bool IsRegisteredAndSaved { get; private set; } = false;

        private async Task LoadCustomersAsync()
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var list = await db.Customers.AsNoTracking().Where(c => !c.IsDeleted).ToListAsync();
                Customers = new ObservableCollection<Customer>(list);
                
                // Mặc định chọn KVL nếu có
                SelectedCustomer = Customers.FirstOrDefault(c => c.CustomerId == "KVL") ?? list.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi tải danh sách khách hàng: " + ex.Message);
            }
        }

        [RelayCommand]
        private void GetScaleWeight()
        {
            try
            {
                if (_scaleService != null)
                {
                    if (_scaleService.IsDisabled)
                    {
                        _notificationService.ShowWarning("Không thể lấy khối lượng: Đầu cân đang bị vô hiệu hóa (None).");
                        return;
                    }
                    TareWeight = _scaleService.CurrentWeight;
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi đọc giá trị từ đầu cân: " + ex.Message);
            }
        }

        [RelayCommand]
        private async Task RegisterVehicleAsync()
        {
            if (string.IsNullOrWhiteSpace(LicensePlate))
            {
                _notificationService.ShowWarning("Vui lòng không để trống Biển số xe.");
                return;
            }

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                
                bool rxExists = await db.Vehicles.IgnoreQueryFilters().AnyAsync(v => v.LicensePlate == LicensePlate);
                if (rxExists)
                {
                    _notificationService.ShowWarning("Biển số này đã tồn tại trong Danh mục.");
                    return;
                }

                var newVehicle = new Vehicle
                {
                    LicensePlate = LicensePlate.ToUpper(),
                    TareWeight = TareWeight ?? 0, // Bỏ trống tương đương Bì = 0
                    CustomerId = SelectedCustomer?.CustomerId ?? "KVL",
                    IsDeleted = false
                };

                db.Vehicles.Add(newVehicle);
                await db.SaveChangesAsync();

                IsRegisteredAndSaved = true;
                CloseAction?.Invoke();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi lưu hệ thống: " + ex.Message);
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            IsRegisteredAndSaved = false;
            CloseAction?.Invoke();
        }
    }
}
