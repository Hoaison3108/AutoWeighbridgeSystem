using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class CustomerViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IUserNotificationService _notificationService;

        [ObservableProperty] private Customer _selectedCustomer = new() { CustomerId = string.Empty };
        [ObservableProperty] private ObservableCollection<Customer> _customerList = new();
        [ObservableProperty] private Customer _gridSelectedItem;
        [ObservableProperty] private bool _isEditMode = false;
        
        // --- AUTOCOMPLETE & SEARCH ---
        [ObservableProperty] private string _searchText = "";
        
        /// <summary>
        /// Gợi ý tên khách hàng (giống Dashboard).
        /// </summary>
        public AutocompleteProvider<string> CustomerAutocomplete { get; } 
            = new AutocompleteProvider<string>(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// View để lọc danh sách DataGrid bên phải dựa trên SearchText.
        /// </summary>
        public System.ComponentModel.ICollectionView CustomerListView { get; private set; }

        public CustomerViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IUserNotificationService notificationService)
        {
            _dbContextFactory = dbContextFactory;
            _notificationService = notificationService;

            // Khởi tạo View Collection để lọc DataGrid
            CustomerListView = System.Windows.Data.CollectionViewSource.GetDefaultView(CustomerList);
            CustomerListView.Filter = c => {
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                var customer = c as Customer;
                if (customer == null) return true;
                string search = SearchText.ToLower();
                return customer.CustomerName.ToLower().Contains(search) || 
                       customer.CustomerId.ToLower().Contains(search);
            };

            _ = LoadDataAsync();
        }

        partial void OnSearchTextChanged(string value)
        {
            CustomerListView.Refresh();
        }

        // --- XỬ LÝ KHI CHỌN DÒNG TRÊN GRID ---
        partial void OnGridSelectedItemChanged(Customer value)
        {
            if (value != null)
            {
                SelectedCustomer = new Customer
                {
                    CustomerId = value.CustomerId,
                    CustomerName = value.CustomerName,
                    IsDeleted = value.IsDeleted
                };
                IsEditMode = true;
            }
        }

        // --- NGHIỆP VỤ LƯU / CẬP NHẬT ---
        [RelayCommand]
        private async Task SaveAsync()
        {
            // 1. CHUẨN HÓA DỮ LIỆU ĐẦU VÀO
            if (SelectedCustomer != null)
            {
                SelectedCustomer.CustomerId = SelectedCustomer.CustomerId?.Trim().ToUpper() ?? string.Empty;
                SelectedCustomer.CustomerName = SelectedCustomer.CustomerName?.Trim() ?? string.Empty;
            }

            // 2. VALIDATION
            if (string.IsNullOrWhiteSpace(SelectedCustomer.CustomerId))
            {
                _notificationService.ShowWarning(UiText.Messages.CustomerIdRequired, UiText.Titles.Warning);
                return;
            }

            try
            {
                using var db = _dbContextFactory.CreateDbContext();

                // Kiểm tra tồn tại (bao gồm cả hàng ẩn)
                var existing = await db.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.CustomerId == SelectedCustomer.CustomerId);

                if (!IsEditMode) // CHẾ ĐỘ THÊM MỚI
                {
                    if (existing != null)
                    {
                        if (existing.IsDeleted)
                        {
                            if (_notificationService.Confirm(UiText.Messages.RestoreDeletedCustomerConfirm(SelectedCustomer.CustomerId), UiText.Titles.Info))
                            {
                                existing.IsDeleted = false;
                                existing.CustomerName = SelectedCustomer.CustomerName;
                                db.Customers.Update(existing);
                            }
                            else return;
                        }
                        else
                        {
                            _notificationService.ShowWarning(UiText.Messages.CustomerAlreadyExists);
                            return;
                        }
                    }
                    else
                    {
                        db.Customers.Add(SelectedCustomer);
                    }
                }
                else // CHẾ ĐỘ CẬP NHẬT
                {
                    if (existing != null)
                    {
                        existing.CustomerName = SelectedCustomer.CustomerName;
                        db.Customers.Update(existing);
                    }
                }

                await db.SaveChangesAsync();
                _notificationService.ShowInfo(UiText.Messages.CustomerSaveSuccess);
                await LoadDataAsync();
                ClearForm();
            }
            catch (Exception ex) { _notificationService.ShowError(UiText.Messages.GenericError(ex.Message)); }
        }

        // --- XÓA MỀM ---
        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (string.IsNullOrEmpty(SelectedCustomer.CustomerId)) return;

            if (_notificationService.Confirm(
                UiText.Messages.DeleteCustomerConfirm(SelectedCustomer.CustomerName, SelectedCustomer.CustomerId),
                "Xác nhận xóa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning))
            {
                try
                {
                    using var db = _dbContextFactory.CreateDbContext();
                    var customer = await db.Customers.FindAsync(SelectedCustomer.CustomerId);

                    if (customer != null)
                    {
                        customer.IsDeleted = true;
                        db.Customers.Update(customer);
                        await db.SaveChangesAsync();

                        await LoadDataAsync();
                        ClearForm();
                        _notificationService.ShowInfo(UiText.Messages.CustomerDeleteSuccess);
                    }
                }
                catch (Exception ex) { _notificationService.ShowError(UiText.Messages.DeleteError(ex.Message)); }
            }
        }

        [RelayCommand]
        private void ClearForm()
        {
            SelectedCustomer = new Customer { CustomerId = string.Empty };
            GridSelectedItem = null;
            IsEditMode = false;
        }

        private async Task LoadDataAsync()
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var list = await db.Customers
                    .AsNoTracking()
                    .OrderBy(c => c.CustomerName)
                    .ToListAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CustomerList.Clear();
                    foreach (var c in list) CustomerList.Add(c);

                    // Cập nhật gợi ý tên khách hàng
                    var names = list.Select(c => c.CustomerName).Distinct().ToArray();
                    CustomerAutocomplete.UpdateItems(names);
                });
            }
            catch (Exception ex) { _notificationService.LogError(ex, "Lỗi load dữ liệu khách hàng"); }
        }
    }
}