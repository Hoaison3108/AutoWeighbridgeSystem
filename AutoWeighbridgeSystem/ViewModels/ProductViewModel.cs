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
    public partial class ProductViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IUserNotificationService _notificationService;

        [ObservableProperty] private Product _selectedProduct = new() { ProductId = string.Empty };
        [ObservableProperty] private ObservableCollection<Product> _productList = new();
        [ObservableProperty] private Product _gridSelectedItem;
        [ObservableProperty] private bool _isEditMode = false;
        
        // --- AUTOCOMPLETE & SEARCH ---
        [ObservableProperty] private string _searchText = "";
        
        /// <summary>
        /// Gợi ý tên sản phẩm (giống Dashboard).
        /// </summary>
        public AutocompleteProvider<string> ProductAutocomplete { get; } 
            = new AutocompleteProvider<string>(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// View để lọc danh sách DataGrid bên phải dựa trên SearchText.
        /// </summary>
        public System.ComponentModel.ICollectionView ProductListView { get; private set; }

        public ProductViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IUserNotificationService notificationService)
        {
            _dbContextFactory = dbContextFactory;
            _notificationService = notificationService;

            // Khởi tạo View Collection để lọc DataGrid
            ProductListView = System.Windows.Data.CollectionViewSource.GetDefaultView(ProductList);
            ProductListView.Filter = p => {
                if (string.IsNullOrWhiteSpace(SearchText)) return true;
                var product = p as Product;
                if (product == null) return true;
                string search = SearchText.ToLower();
                return product.ProductName.ToLower().Contains(search) || 
                       product.ProductId.ToLower().Contains(search);
            };

            _ = LoadDataAsync();
        }

        partial void OnSearchTextChanged(string value)
        {
            ProductListView.Refresh();
        }

        partial void OnGridSelectedItemChanged(Product value)
        {
            if (value != null)
            {
                SelectedProduct = new Product
                {
                    ProductId = value.ProductId,
                    ProductName = value.ProductName,
                    IsDeleted = value.IsDeleted
                };
                IsEditMode = true;
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            // -------------------------------------------------------------------------
            // BƯỚC 1: CHUẨN HÓA NGAY LẬP TỨC (Dùng cho cả Thêm và Sửa)
            // -------------------------------------------------------------------------
            // Chúng ta chuẩn hóa trước khi Validation để tránh trường hợp người dùng 
            // chỉ nhập dấu cách (space) mà vẫn vượt qua kiểm tra NullOrWhiteSpace.

            if (SelectedProduct != null)
            {
                // Viết hoa và cắt khoảng trắng cho Mã
                SelectedProduct.ProductId = SelectedProduct.ProductId?.Trim().ToUpper() ?? string.Empty;

                // Cắt khoảng trắng thừa cho Tên (Giữ nguyên hoa thường theo ý người dùng)
                SelectedProduct.ProductName = SelectedProduct.ProductName?.Trim() ?? string.Empty;
            }

            // -------------------------------------------------------------------------
            // BƯỚC 2: VALIDATION (Kiểm tra sau khi đã chuẩn hóa)
            // -------------------------------------------------------------------------
            if (string.IsNullOrWhiteSpace(SelectedProduct.ProductId))
            {
                _notificationService.ShowWarning(UiText.Messages.ProductIdRequired, UiText.Titles.Info);
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedProduct.ProductName))
            {
                _notificationService.ShowWarning(UiText.Messages.ProductNameRequired, UiText.Titles.Info);
                return;
            }

            try
            {
                using var db = _dbContextFactory.CreateDbContext();

                // Tìm kiếm bản ghi dựa trên Mã đã được chuẩn hóa
                var existing = await db.Products
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.ProductId == SelectedProduct.ProductId);

                if (!IsEditMode) // --- CHẾ ĐỘ THÊM MỚI ---
                {
                    if (existing != null)
                    {
                        if (existing.IsDeleted)
                        {
                            // Logic khôi phục (giữ nguyên như cũ)
                            if (_notificationService.Confirm(UiText.Messages.RestoreDeletedProductConfirm(SelectedProduct.ProductId), UiText.Titles.Info))
                            {
                                existing.IsDeleted = false;
                                existing.ProductName = SelectedProduct.ProductName;
                                db.Products.Update(existing);
                            }
                            else return;
                        }
                        else
                        {
                            _notificationService.ShowError(UiText.Messages.ProductAlreadyExists, UiText.Titles.Error);
                            return;
                        }
                    }
                    else
                    {
                        db.Products.Add(SelectedProduct);
                    }
                }
                else // --- CHẾ ĐỘ CẬP NHẬT ---
                {
                    if (existing != null)
                    {
                        // Cập nhật tên đã được Trim() ở Bước 1
                        existing.ProductName = SelectedProduct.ProductName;
                        db.Products.Update(existing);
                    }
                    else
                    {
                        // Trường hợp hy hữu: Đang sửa mà bản ghi bị ai đó xóa mất trong DB
                        _notificationService.ShowError(UiText.Messages.ProductNotFoundToUpdate, UiText.Titles.Error);
                        return;
                    }
                }

                await db.SaveChangesAsync();
                _notificationService.ShowInfo(UiText.Messages.ProductSaveSuccess, UiText.Titles.Success);

                await LoadDataAsync();
                ClearForm();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError(UiText.Messages.SystemErrorWithDetail(ex.Message));
            }
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (string.IsNullOrEmpty(SelectedProduct.ProductId)) return;

            // Đảm bảo mã xóa cũng được chuẩn hóa
            string cleanId = SelectedProduct.ProductId.Trim().ToUpper();

            if (_notificationService.Confirm(
                UiText.Messages.DeleteProductConfirm(cleanId),
                "Xác nhận xóa",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning))
            {
                try
                {
                    using var db = _dbContextFactory.CreateDbContext();
                    var item = await db.Products.FindAsync(cleanId);
                    if (item != null)
                    {
                        item.IsDeleted = true;
                        db.Products.Update(item);
                        await db.SaveChangesAsync();
                        await LoadDataAsync();
                        ClearForm();
                        _notificationService.ShowInfo(UiText.Messages.ProductDeleteSuccess);
                    }
                }
                catch (Exception ex) { _notificationService.ShowError(UiText.Messages.DeleteError(ex.Message)); }
            }
        }

        [RelayCommand]
        private void ClearForm()
        {
            SelectedProduct = new Product { ProductId = string.Empty };
            GridSelectedItem = null;
            IsEditMode = false;
        }

        private async Task LoadDataAsync()
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var list = await db.Products
                    .AsNoTracking()
                    .OrderBy(p => p.ProductName)
                    .ToListAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    ProductList.Clear();
                    foreach (var p in list) ProductList.Add(p);

                    // Cập nhật gợi ý tên hàng hóa
                    var names = list.Select(p => p.ProductName).Distinct().ToArray();
                    ProductAutocomplete.UpdateItems(names);
                });
            }
            catch (Exception ex) { _notificationService.LogError(ex, "Lỗi load dữ liệu sản phẩm"); }
        }
    }
}