using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ nghiệp vụ RFID cấp cao: nhận mã thẻ thô từ đầu đọc,
    /// làm sạch dữ liệu (lọc chữ số), rồi tra cứu database để xác định
    /// xe nào đang gắn với thẻ đó.
    /// Được dùng chung bởi <see cref="DashboardWorkflowService"/> và
    /// <see cref="VehicleRegistrationViewModel"/>.
    /// </summary>
    public class RfidBusinessService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

        public RfidBusinessService(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        /// <summary>
        /// Xử lý mã thẻ RFID thô: làm sạch → tra cứu database → trả về kết quả.
        /// <list type="bullet">
        ///   <item>Nếu thẻ chưa đăng ký: <see cref="RfidProcessResult.IsNewCard"/> = <c>true</c>.</item>
        ///   <item>Nếu thẻ đã gắn với xe: <see cref="RfidProcessResult.ExistingVehicle"/> chứa thông tin xe.</item>
        /// </list>
        /// </summary>
        /// <param name="rawCardId">Chuỗi thô nhận từ Serial Port (có thể chứa ký tự điều khiển).</param>
        /// <returns><see cref="RfidProcessResult"/> chứa kết quả tra cứu.</returns>
        public async Task<RfidProcessResult> ProcessRawCardAsync(string rawCardId)
        {
            var result = new RfidProcessResult();

            try
            {
                // 1. Dùng Regex lọc lấy số
                var match = Regex.Match(rawCardId, @"\d+");
                if (!match.Success)
                {
                    result.IsSuccess    = false;
                    result.ErrorMessage = $"Không tìm thấy số trong chuỗi thô: {rawCardId}";
                    return result;
                }

                result.CleanCardId = match.Value;

                // 2. Tra cứu Database xem thẻ đã gán cho xe nào chưa
                using var db = _dbContextFactory.CreateDbContext();
                result.ExistingVehicle = await db.Vehicles
                    .Include(v => v.Customer)
                    .FirstOrDefaultAsync(v => v.RfidCardId == result.CleanCardId && !v.IsDeleted);

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                result.IsSuccess    = false;
                result.ErrorMessage = "Lỗi Database khi tra cứu thẻ: " + ex.Message;
            }

            return result;
        }
    }
}