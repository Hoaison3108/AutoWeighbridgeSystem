using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    public sealed class DashboardSaveService
    {
        private readonly WeighingBusinessService _weighingBusiness;
        private readonly AlarmService _alarmService;

        public DashboardSaveService(WeighingBusinessService weighingBusiness, AlarmService alarmService)
        {
            _weighingBusiness = weighingBusiness;
            _alarmService = alarmService;
        }

        public async Task<DashboardSaveResult> ExecuteSaveAsync(DashboardSaveRequest request)
        {
            try
            {
                int vehicleId = request.SelectedVehicleId ?? 0;
                if (vehicleId == 0 && !string.IsNullOrEmpty(request.LicensePlate))
                {
                    var matched = request.VehicleList.FirstOrDefault(v =>
                        v.LicensePlate.Equals(request.LicensePlate, StringComparison.OrdinalIgnoreCase));
                    if (matched != null) vehicleId = matched.VehicleId;
                }

                var result = await _weighingBusiness.ProcessWeighingAsync(
                    request.LicensePlate,
                    vehicleId,
                    request.CustomerName,
                    request.ProductName,
                    request.FinalWeight,
                    request.IsOnePassMode);

                if (!result.IsSuccess)
                    return DashboardSaveResult.Failed(result.Message);

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

    public sealed record DashboardSaveRequest(
        string LicensePlate,
        int? SelectedVehicleId,
        IReadOnlyCollection<Vehicle> VehicleList,
        string CustomerName,
        string ProductName,
        decimal FinalWeight,
        bool IsOnePassMode);

    public sealed record DashboardSaveResult
    {
        public bool IsSuccess { get; init; }
        public bool HasException { get; init; }
        public string Message { get; init; }
        public decimal FinalWeight { get; init; }

        public static DashboardSaveResult Success(decimal finalWeight, string message) => new()
        {
            IsSuccess = true,
            FinalWeight = finalWeight,
            Message = message
        };

        public static DashboardSaveResult Failed(string message) => new()
        {
            IsSuccess = false,
            Message = message
        };

        public static DashboardSaveResult Error() => new()
        {
            IsSuccess = false,
            HasException = true
        };
    }
}
