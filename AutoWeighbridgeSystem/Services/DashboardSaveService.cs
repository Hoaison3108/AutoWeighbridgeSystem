using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Common;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ thực thi lưu phiếu cân: xác thực đầu vào, gọi <see cref="WeighingBusinessService"/>
    /// để xử lý phiếu, kích hoạt còi báo hiệu khi lưu thành công.
    /// Đóng vai trò là "cầu nối" giữa ViewModel và Business Logic.
    /// </summary>
    public sealed class DashboardSaveService
    {
        private readonly WeighingBusinessService _weighingBusiness;
        private readonly AlarmService _alarmService;

        public DashboardSaveService(WeighingBusinessService weighingBusiness, AlarmService alarmService)
        {
            _weighingBusiness = weighingBusiness;
            _alarmService     = alarmService;
        }

        /// <summary>
        /// Thực thi quy trình lưu phiếu cân: resolve VehicleId → gọi business service → kích còi.
        /// </summary>
        /// <param name="request">Dữ liệu yêu cầu lưu phiếu từ Dashboard.</param>
        /// <returns>
        /// <see cref="DashboardSaveResult.Success"/> nếu lưu thành công;<br/>
        /// <see cref="DashboardSaveResult.Failed"/> nếu thất bại có thể hiển thị cho người dùng;<br/>
        /// <see cref="DashboardSaveResult.Error"/> nếu có exception không mong đợi.
        /// </returns>
        public async Task<DashboardSaveResult> ExecuteSaveAsync(DashboardSaveRequest request)
        {
            try
            {
                // Chuẩn hóa biển số trước khi đẩy xuống tầng Database
                string formattedPlate = request.LicensePlate.FormatLicensePlate();

                // Truyền trực tiếp định dạng chuỗi
                // Để vehicleId bằng 0 sẽ ép WeighingBusinessService tự tìm kiếm Vehicle thật dưới Database
                var result = await _weighingBusiness.ProcessWeighingAsync(
                    formattedPlate,
                    0, 
                    request.CustomerName,
                    request.ProductName,
                    request.FinalWeight,
                    request.IsOnePassMode);

                if (!result.IsSuccess)
                    return DashboardSaveResult.Failed(result.Message);

                // Kích hoạt còi báo hiệu (fire-and-forget, lỗi không ảnh hưởng kết quả)
                _ = _alarmService.TriggerAlarmAsync();
                return DashboardSaveResult.Success(request.FinalWeight, result.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi thực thi giao dịch cân");
                return DashboardSaveResult.Error();
            }
        }
    }

    // =========================================================================
    // REQUEST / RESULT VALUE OBJECTS
    // =========================================================================

    /// <summary>Dữ liệu đầu vào cho một lệnh lưu phiếu cân từ Dashboard dạng thô (Raw string).</summary>
    public sealed record DashboardSaveRequest(
        string LicensePlate,
        string CustomerName,
        string ProductName,
        decimal FinalWeight,
        bool IsOnePassMode);

    /// <summary>Kết quả trả về sau khi thực thi <see cref="DashboardSaveService.ExecuteSaveAsync"/>.</summary>
    public sealed record DashboardSaveResult
    {
        /// <summary>Lưu phiếu thành công hay không.</summary>
        public bool IsSuccess { get; init; }

        /// <summary><c>true</c> nếu thất bại do exception (không nên hiển thị message cho user).</summary>
        public bool HasException { get; init; }

        /// <summary>Nội dung thông báo kết quả (hiển thị cho người dùng).</summary>
        public string Message { get; init; }

        /// <summary>Trọng lượng đã được lưu (dùng để hiển thị xác nhận).</summary>
        public decimal FinalWeight { get; init; }

        /// <summary>Lưu thành công.</summary>
        public static DashboardSaveResult Success(decimal finalWeight, string message) => new()
        {
            IsSuccess   = true,
            FinalWeight = finalWeight,
            Message     = message
        };

        /// <summary>Thất bại có lý do rõ ràng (có thể hiển thị cho người dùng).</summary>
        public static DashboardSaveResult Failed(string message) => new()
        {
            IsSuccess = false,
            Message   = message
        };

        /// <summary>Thất bại do exception không mong đợi (chỉ nên log, không hiển thị chi tiết cho user).</summary>
        public static DashboardSaveResult Error() => new()
        {
            IsSuccess    = false,
            HasException = true
        };
    }
}
