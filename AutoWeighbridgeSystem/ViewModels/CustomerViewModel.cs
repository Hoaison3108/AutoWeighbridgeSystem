using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
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

        [ObservableProperty] private Customer _selectedCustomer = new() { CustomerId = string.Empty };
        [ObservableProperty] private ObservableCollection<Customer> _customerList = new();
        [ObservableProperty] private Customer _gridSelectedItem;
        [ObservableProperty] private bool _isEditMode = false;

        public CustomerViewModel(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
            _ = LoadDataAsync();
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
                MessageBox.Show("Mã khách hàng không được để trống!", "Cảnh báo");
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
                            var resume = MessageBox.Show($"Mã [{SelectedCustomer.CustomerId}] đã bị xóa trước đó. Khôi phục?", "Thông báo", MessageBoxButton.YesNo);
                            if (resume == MessageBoxResult.Yes)
                            {
                                existing.IsDeleted = false;
                                existing.CustomerName = SelectedCustomer.CustomerName;
                                db.Customers.Update(existing);
                            }
                            else return;
                        }
                        else
                        {
                            MessageBox.Show("Mã khách hàng này đã tồn tại!");
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
                MessageBox.Show("Lưu thành công!");
                await LoadDataAsync();
                ClearForm();
            }
            catch (Exception ex) { MessageBox.Show("Lỗi: " + ex.Message); }
        }

        // --- XÓA MỀM ---
        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (string.IsNullOrEmpty(SelectedCustomer.CustomerId)) return;

            var result = MessageBox.Show($"Xác nhận xóa khách hàng: {SelectedCustomer.CustomerName}?\n(Mã: {SelectedCustomer.CustomerId})",
                                        "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
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
                        MessageBox.Show("Đã xóa khách hàng thành công!");
                    }
                }
                catch (Exception ex) { MessageBox.Show("Lỗi khi xóa: " + ex.Message); }
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
                    CustomerList = new ObservableCollection<Customer>(list);
                });
            }
            catch (Exception ex) { Console.WriteLine("Lỗi load dữ liệu: " + ex.Message); }
        }
    }
}