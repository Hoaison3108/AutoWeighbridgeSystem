using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly ScaleService _scaleService;
        private readonly RfidMultiService _rfidService;
        private readonly RfidBusinessService _rfidBusiness;
        private readonly AlarmService _alarmService;
        private readonly WeighingBusinessService _weighingBusiness;
        private readonly AppSession _appSession;

        // --- CẤU HÌNH & TRẠNG THÁI LUỒNG ---
        private decimal _minWeightThreshold = 200;
        private int _rfidCooldownSeconds = 3;
        private int _queueTimeoutSeconds = 45;
        private int _hardwareWatchdogSeconds = 15;

        private readonly System.Collections.Generic.Dictionary<string, DateTime> _rfidCooldowns = new();
        private DateTime _lastScaleDataReceivedTime = DateTime.Now;
        private CancellationTokenSource _pendingTimeoutCts;
        private CancellationTokenSource _watchdogCts;

        private bool _isProcessingSave = false; // Cờ chặn lưu trùng (Spam Click)

        // --- HÀNG CHỜ (PENDING DATA) CHO LUỒNG AUTO ---
        private string _pendingLicensePlate;
        private string _pendingCustomerName;
        private string _pendingProductName;
        private int _pendingVehicleId;
        private bool _hasPendingVehicle = false;

        public Unosquare.FFME.MediaElement VideoPlayer { get; set; }

        // --- PROPERTIES BINDING (UI) ---
        [ObservableProperty] private string _weightDisplay = "0";
        [ObservableProperty] private bool _isScaleStable = false;

        // Dữ liệu trong TextBox (Dữ liệu thực tế sẽ lưu)
        [ObservableProperty] private string _licensePlate = "";
        [ObservableProperty] private string _customerName = "";
        [ObservableProperty] private string _productName = "";

        [ObservableProperty] private string _rfidInput = "";
        [ObservableProperty] private string _rfidLocationLabel = "Mã thẻ RFID (Đang chờ...):";

        // Trạng thái chế độ
        [ObservableProperty] private bool _isAutoMode = true;
        [ObservableProperty] private bool _isOnePassMode = false;

        [ObservableProperty] private Uri _cameraUri;
        [ObservableProperty] private string _cameraStatus = "Camera Online";
        [ObservableProperty] private ObservableCollection<WeighingTicket> _recentTickets = new();
        [ObservableProperty] private WeighingTicket _selectedRecentTicket;

        // Danh mục ComboBox
        [ObservableProperty] private ObservableCollection<Product> _productList = new();
        [ObservableProperty] private Product _selectedProduct;
        [ObservableProperty] private ObservableCollection<Vehicle> _vehicleList = new();
        [ObservableProperty] private ObservableCollection<Customer> _customerList = new();
        [ObservableProperty] private Vehicle _selectedVehicle;
        [ObservableProperty] private Customer _selectedCustomer;

        // Cơ chế Chốt cân (Snapshot)
        [ObservableProperty] private decimal _lockedWeight = 0;
        [ObservableProperty] private bool _isWeightLocked = false;

        // =========================================================================
        // KHỞI TẠO (CONSTRUCTOR)
        // =========================================================================
        public DashboardViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IConfiguration configuration,
            ScaleService scaleService,
            RfidMultiService rfidService,
            RfidBusinessService rfidBusiness,
            AlarmService alarmService,
            WeighingBusinessService weighingBusiness,
            AppSession appSession)
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            _scaleService = scaleService;
            _rfidService = rfidService;
            _rfidBusiness = rfidBusiness;
            _alarmService = alarmService;
            _weighingBusiness = weighingBusiness;
            _appSession = appSession;

            LoadConfiguration();

            // Đăng ký sự kiện phần cứng
            _scaleService.WeightChanged += OnScaleWeightChanged;
            _rfidService.CardRead += OnRfidCardRead;

            InitializeCamera();

            // Tải dữ liệu bất đồng bộ
            _ = LoadRecentTicketsAsync();
            _ = LoadInitialDataAsync();

            StartHardwareWatchdog();
        }

        private void LoadConfiguration()
        {
            if (decimal.TryParse(_configuration["ScaleSettings:MinWeightThreshold"], out decimal pw)) _minWeightThreshold = pw;
            if (int.TryParse(_configuration["ScaleSettings:RfidCooldownSeconds"], out int cd)) _rfidCooldownSeconds = cd;
            if (int.TryParse(_configuration["ScaleSettings:QueueTimeoutSeconds"], out int qt)) _queueTimeoutSeconds = qt;
            if (int.TryParse(_configuration["ScaleSettings:HardwareWatchdogSeconds"], out int wd)) _hardwareWatchdogSeconds = wd;

            // Nạp cấu hình mặc định từ appsettings.json
            if (bool.TryParse(_configuration["ScaleSettings:DefaultToAutoMode"], out bool isAuto)) IsAutoMode = isAuto;
            if (bool.TryParse(_configuration["ScaleSettings:DefaultToOnePassMode"], out bool isOnePass)) IsOnePassMode = isOnePass;
        }

        // =========================================================================
        // LOGIC LIÊN KẾT DỮ LIỆU (AUTO-FILL TỪ COMBOBOX SANG TEXTBOX)
        // =========================================================================

        partial void OnSelectedVehicleChanged(Vehicle value)
        {
            if (value == null) return;
            LicensePlate = value.LicensePlate;
            if (value.Customer != null)
            {
                SelectedCustomer = CustomerList.FirstOrDefault(c => c.CustomerId == value.CustomerId);
                CustomerName = value.Customer.CustomerName;
            }
        }

        partial void OnSelectedCustomerChanged(Customer value)
        {
            if (value != null) CustomerName = value.CustomerName;
        }

        partial void OnSelectedProductChanged(Product value)
        {
            if (value != null) ProductName = value.ProductName;
        }

        // =========================================================================
        // HÀM ĐIỀU PHỐI GIAO DỊCH (Dùng chung cho cả Auto và Manual)
        // =========================================================================
        private async Task ProcessAndSaveWeighingAsync(decimal finalWeight)
        {
            if (_isProcessingSave) return;
            _isProcessingSave = true;

            try
            {
                LockedWeight = finalWeight;
                IsWeightLocked = true;
                WeightDisplay = LockedWeight.ToString("N0");

                // --- SỬA LẠI LOGIC LẤY ID TẠI ĐÂY ---
                // Ưu tiên 1: Lấy từ RFID (_pendingVehicleId)
                // Ưu tiên 2: Lấy từ ComboBox (SelectedVehicle)
                int vId = _hasPendingVehicle ? _pendingVehicleId : (SelectedVehicle?.VehicleId ?? 0);

                // Ưu tiên 3: Nếu vẫn bằng 0 (do gõ tay), thử tìm ID dựa trên Biển số đã nhập
                if (vId == 0 && !string.IsNullOrEmpty(LicensePlate))
                {
                    var matched = VehicleList.FirstOrDefault(v => v.LicensePlate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase));
                    if (matched != null) vId = matched.VehicleId;
                }
                // ------------------------------------

                var result = await _weighingBusiness.ProcessWeighingAsync(
                    LicensePlate, vId, CustomerName, ProductName, LockedWeight, IsOnePassMode);

                if (result.IsSuccess)
                {
                    // 3. PHẢN HỒI THÀNH CÔNG (Còi hú + Hiện thông báo Camera + Load lại lưới)
                    ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG\n{result.Message}", false);
                    _ = _alarmService.TriggerAlarmAsync();
                    await LoadRecentTicketsAsync();

                    // Delay 2s để người dùng kịp quan sát kết quả trước khi xóa form
                    await Task.Delay(2000);
                    ResetForm();
                }
                else
                {
                    // 4. XỬ LÝ LỖI (Ví dụ: Xe chưa có bì khi cân 1 lần)
                    MessageBox.Show(result.Message, "Cảnh Báo Nghiệp Vụ", MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsWeightLocked = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi thực thi giao dịch cân");
                IsWeightLocked = false;
            }
            finally
            {
                _isProcessingSave = false;
            }
        }

        // =========================================================================
        // LUỒNG TỰ ĐỘNG (AUTO MODE - RFID)
        // =========================================================================

        private void OnScaleWeightChanged(decimal weight, bool isStable)
        {
            _lastScaleDataReceivedTime = DateTime.Now;

            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                // Chỉ cập nhật số nhảy nếu UI chưa bị chốt số
                if (!IsWeightLocked) WeightDisplay = weight.ToString("N0");

                IsScaleStable = isStable;

                // Xử lý xe lùi ra khỏi cân
                if (weight < _minWeightThreshold)
                {
                    if (_hasPendingVehicle && !IsWeightLocked)
                    {
                        ClearPendingData();
                        ResetForm();
                        ShowCameraMessage("CÂN VỀ KHÔNG - HỦY LỆNH CHỜ");
                    }
                    return;
                }

                // KÍCH HOẠT AUTO CÂN: Cân ổn định + Đã nhận diện thẻ
                if (IsAutoMode && isStable && _hasPendingVehicle && !_isProcessingSave)
                {
                    LicensePlate = _pendingLicensePlate;
                    CustomerName = _pendingCustomerName;
                    ProductName = _pendingProductName;
                    ClearPendingData();

                    await ProcessAndSaveWeighingAsync(weight);
                }
            });
        }

        private void OnRfidCardRead(string readerRole, string cardId)
        {
            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (_rfidCooldowns.TryGetValue(readerRole, out DateTime lastRead))
                    if ((DateTime.Now - lastRead).TotalSeconds < _rfidCooldownSeconds) return;

                _rfidCooldowns[readerRole] = DateTime.Now;

                // Bỏ qua thẻ tại bàn làm việc (Desk), chỉ nhận tại trạm cân
                if (readerRole == ReaderRoles.Desk) return;

                RfidInput = cardId;
                RfidLocationLabel = $"Mã thẻ RFID (Nhận từ {readerRole}):";

                if (!IsAutoMode) { ShowCameraMessage("CHẾ ĐỘ TAY - BỎ QUA THẺ!"); return; }

                await ProcessWeighbridgeRfidAsync(cardId);
            });
        }

        private async Task ProcessWeighbridgeRfidAsync(string cardId)
        {
            try
            {
                var rfidResult = await _rfidBusiness.ProcessRawCardAsync(cardId);

                if (!rfidResult.IsSuccess || rfidResult.IsNewCard)
                {
                    ShowCameraMessage($"THẺ {rfidResult.CleanCardId} CHƯA ĐĂNG KÝ!");
                    return;
                }

                var vehicle = rfidResult.ExistingVehicle;
                _pendingLicensePlate = vehicle.LicensePlate;
                _pendingCustomerName = vehicle.Customer?.CustomerName ?? "Khách lẻ";
                _pendingProductName = SelectedProduct?.ProductName ?? "Hàng hóa";
                _pendingVehicleId = vehicle.VehicleId;
                _hasPendingVehicle = true;

                // Nếu xe đã đậu ổn định trên bàn cân rồi mới quẹt thẻ
                if (IsScaleStable && _scaleService.CurrentWeight >= _minWeightThreshold)
                {
                    LicensePlate = _pendingLicensePlate;
                    CustomerName = _pendingCustomerName;
                    ProductName = _pendingProductName;
                    ClearPendingData();
                    await ProcessAndSaveWeighingAsync(_scaleService.CurrentWeight);
                }
                else
                {
                    ShowCameraMessage($"NHẬN XE: {_pendingLicensePlate}. ĐỢI ỔN ĐỊNH...", false);
                    StartPendingTimeout();
                }
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi xử lý RFID"); }
        }

        // =========================================================================
        // LUỒNG THỦ CÔNG & HỦY PHIẾU (MANUAL MODE)
        // =========================================================================

        [RelayCommand]
        private void ToggleWeightLock()
        {
            if (!IsWeightLocked)
            {
                if (!IsScaleStable) { MessageBox.Show("Cân đang dao động, vui lòng đợi ổn định!", "Cảnh báo"); return; }
                if (_scaleService.CurrentWeight < _minWeightThreshold) return;

                LockedWeight = _scaleService.CurrentWeight;
                IsWeightLocked = true;
                ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG", false);
            }
            else { IsWeightLocked = false; LockedWeight = 0; }
        }

        [RelayCommand]
        private async Task ManualSaveAsync()
        {
            if (IsAutoMode)
            {
                MessageBox.Show("Vui lòng tắt chế độ AUTO trước khi lưu phiếu bằng tay!");
                return;
            }

            if (string.IsNullOrWhiteSpace(LicensePlate))
            {
                MessageBox.Show("Vui lòng nhập hoặc chọn Biển số xe!");
                return;
            }

            if (!IsWeightLocked || LockedWeight <= 0)
            {
                MessageBox.Show("Vui lòng bấm '🔒 CHỐT SỐ CÂN' để lấy khối lượng trước khi lưu!");
                return;
            }

            // Xác nhận lưu thủ công
            if (MessageBox.Show($"Lưu phiếu cho xe {LicensePlate} - {LockedWeight:N0} kg?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            await ProcessAndSaveWeighingAsync(LockedWeight);
        }

        [RelayCommand]
        private async Task CancelTicketAsync()
        {
            if (SelectedRecentTicket == null) return;
            if (SelectedRecentTicket.IsVoid) { MessageBox.Show("Phiếu đã bị hủy trước đó!"); return; }

            string msg = SelectedRecentTicket.TimeOut.HasValue
                ? $"Xác nhận HỦY phiếu ĐÃ HOÀN THÀNH {SelectedRecentTicket.TicketID}?"
                : $"Xác nhận HỦY LỆNH CÂN LẦN 1 của xe {SelectedRecentTicket.LicensePlate}?";

            if (MessageBox.Show(msg, "Hủy phiếu", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var ticket = await db.WeighingTickets.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TicketID == SelectedRecentTicket.TicketID);
                if (ticket != null)
                {
                    ticket.IsVoid = true;
                    ticket.VoidReason = "Hủy thủ công từ Dashboard";
                    db.WeighingTickets.Update(ticket);
                    await db.SaveChangesAsync();
                    ShowCameraMessage($"ĐÃ HỦY: {ticket.TicketID}");
                    await LoadRecentTicketsAsync();
                }
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi hủy phiếu"); }
        }

        [RelayCommand]
        private async Task TriggerManualAlarmAsync() => await _alarmService.TriggerAlarmAsync();


        // =========================================================================
        // DATA LOADING & CLEANUP
        // =========================================================================

        private async Task LoadInitialDataAsync()
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Customer).Where(v => !v.IsDeleted).ToListAsync();
                var customers = await db.Customers.AsNoTracking().ToListAsync();
                var products = await db.Products.AsNoTracking().ToListAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    VehicleList = new ObservableCollection<Vehicle>(vehicles);
                    CustomerList = new ObservableCollection<Customer>(customers);
                    ProductList = new ObservableCollection<Product>(products);

                    // Mặc định tên sản phẩm từ cấu hình
                    string defProd = _configuration["ScaleSettings:DefaultProductName"] ?? "Đá xô bồ";
                    var prod = ProductList.FirstOrDefault(p => p.ProductName.Equals(defProd, StringComparison.OrdinalIgnoreCase));
                    if (prod != null) { SelectedProduct = prod; ProductName = prod.ProductName; }
                    else ProductName = defProd;
                });
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi tải danh mục"); }
        }

        public async Task LoadRecentTicketsAsync()
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var tickets = await db.WeighingTickets.IgnoreQueryFilters().AsNoTracking()
                    .OrderByDescending(t => t.TimeIn).Take(15).ToListAsync();

                Application.Current?.Dispatcher.Invoke(() => RecentTickets = new ObservableCollection<WeighingTicket>(tickets));
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi tải nhật ký"); }
        }

        private void InitializeCamera()
        {
            string url = _configuration["CameraSettings:RtspUrl"];
            if (!string.IsNullOrEmpty(url)) CameraUri = new Uri(url);
        }

        private void StartHardwareWatchdog()
        {
            _watchdogCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_watchdogCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_hardwareWatchdogSeconds), _watchdogCts.Token);
                    if ((DateTime.Now - _lastScaleDataReceivedTime).TotalSeconds > _hardwareWatchdogSeconds)
                        ShowCameraMessage("⚠️ MẤT TÍN HIỆU ĐẦU CÂN!", false);
                }
            }, _watchdogCts.Token);
        }

        private void ResetForm()
        {
            LicensePlate = ""; CustomerName = ""; RfidInput = "";
            SelectedVehicle = null; SelectedCustomer = null;
            IsWeightLocked = false; LockedWeight = 0;
            ClearPendingData();
        }

        private void ClearPendingData() { _pendingLicensePlate = null; _hasPendingVehicle = false; _pendingTimeoutCts?.Cancel(); }

        private void StartPendingTimeout()
        {
            _pendingTimeoutCts?.Cancel(); _pendingTimeoutCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(_queueTimeoutSeconds), _pendingTimeoutCts.Token); ClearPendingData(); } catch { }
            }, _pendingTimeoutCts.Token);
        }

        private void ShowCameraMessage(string msg, bool autoHide = true)
        {
            CameraStatus = msg;
            if (autoHide) Task.Run(async () => { await Task.Delay(3000); CameraStatus = "Camera Online"; });
        }

        public void Dispose()
        {
            _watchdogCts?.Cancel(); _pendingTimeoutCts?.Cancel();
            _scaleService.WeightChanged -= OnScaleWeightChanged;
            _rfidService.CardRead -= OnRfidCardRead;
        }

        [RelayCommand]
        private void RefreshForm()
        {
            // Gọi lại hàm ResetForm đã viết để xóa trắng Biển số, Khách hàng, Loại hàng, RFID...
            ResetForm();

            // Thông báo nhanh trên màn hình Camera
            ShowCameraMessage("♻️ ĐÃ LÀM SẠCH BIỂU MẪU");

            Log.Information("[UI] Người dùng đã chủ động làm mới biểu mẫu.");
        }
    }
}