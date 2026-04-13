using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    public class RfidBusinessService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

        public RfidBusinessService(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        /// <summary>
        /// Hàm dùng chung cho mọi ViewModel để xử lý mã thẻ thô
        /// </summary>
        public async Task<RfidProcessResult> ProcessRawCardAsync(string rawCardId)
        {
            var result = new RfidProcessResult();

            try
            {
                // 1. Dùng Regex lọc lấy số
                var match = Regex.Match(rawCardId, @"\d+");
                if (!match.Success)
                {
                    result.IsSuccess = false;
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
                result.IsSuccess = false;
                result.ErrorMessage = "Lỗi Database khi tra cứu thẻ: " + ex.Message;
            }

            return result;
        }
    }
}