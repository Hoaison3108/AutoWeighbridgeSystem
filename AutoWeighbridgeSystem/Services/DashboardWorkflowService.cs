using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.Services
{
    public sealed class DashboardWorkflowService
    {
        private readonly RfidBusinessService _rfidBusiness;
        private readonly Dictionary<string, DateTime> _rfidCooldowns = new();

        private int _rfidCooldownSeconds = 3;

        private string _pendingLicensePlate;
        private string _pendingCustomerName;
        private string _pendingProductName;
        private int _pendingVehicleId;

        public decimal MinWeightThreshold { get; private set; } = 200;
        public bool HasPendingVehicle { get; private set; }

        public DashboardWorkflowService(IConfiguration configuration, RfidBusinessService rfidBusiness)
        {
            _rfidBusiness = rfidBusiness;
            LoadConfiguration(configuration);
        }

        private void LoadConfiguration(IConfiguration configuration)
        {
            if (decimal.TryParse(configuration["ScaleSettings:MinWeightThreshold"], out decimal threshold))
                MinWeightThreshold = threshold;
            if (int.TryParse(configuration["ScaleSettings:RfidCooldownSeconds"], out int cooldown))
                _rfidCooldownSeconds = cooldown;
        }

        public bool ShouldIgnoreRfidRead(string readerRole)
        {
            if (_rfidCooldowns.TryGetValue(readerRole, out DateTime lastRead))
                if ((DateTime.Now - lastRead).TotalSeconds < _rfidCooldownSeconds) return true;

            _rfidCooldowns[readerRole] = DateTime.Now;
            return false;
        }

        public void ClearPendingData()
        {
            _pendingLicensePlate = null;
            _pendingCustomerName = null;
            _pendingProductName = null;
            _pendingVehicleId = 0;
            HasPendingVehicle = false;
        }

        public PendingVehicleData GetPendingVehicleData()
        {
            return new PendingVehicleData(
                _pendingLicensePlate,
                _pendingCustomerName,
                _pendingProductName,
                _pendingVehicleId);
        }

        public ScaleWorkflowDecision EvaluateScaleEvent(decimal weight, bool isStable, bool isAutoMode, bool isProcessingSave, bool isWeightLocked)
        {
            if (weight < MinWeightThreshold)
            {
                if (HasPendingVehicle && !isWeightLocked)
                {
                    ClearPendingData();
                    return ScaleWorkflowDecision.ClearPendingAndReset("CÂN VỀ KHÔNG - HỦY LỆNH CHỜ");
                }

                return ScaleWorkflowDecision.None();
            }

            if (isAutoMode && isStable && HasPendingVehicle && !isProcessingSave && !isWeightLocked)
            {
                var pending = GetPendingVehicleData();
                ClearPendingData();
                return ScaleWorkflowDecision.SaveWithPending(weight, pending);
            }

            return ScaleWorkflowDecision.None();
        }

        public async Task<RfidWorkflowDecision> EvaluateRfidEventAsync(string cardId, string selectedProductName, bool isAutoMode, bool isScaleStable, decimal currentWeight)
        {
            if (!isAutoMode) return RfidWorkflowDecision.Message("CHẾ ĐỘ TAY - BỎ QUA THẺ!");

            var rfidResult = await _rfidBusiness.ProcessRawCardAsync(cardId);

            if (!rfidResult.IsSuccess || rfidResult.IsNewCard)
                return RfidWorkflowDecision.Message($"THẺ {rfidResult.CleanCardId} CHƯA ĐĂNG KÝ!");

            var vehicle = rfidResult.ExistingVehicle;
            _pendingLicensePlate = vehicle.LicensePlate;
            _pendingCustomerName = vehicle.Customer?.CustomerName ?? "Khách lẻ";
            _pendingProductName = selectedProductName ?? "Hàng hóa";
            _pendingVehicleId = vehicle.VehicleId;
            HasPendingVehicle = true;

            if (isScaleStable && currentWeight >= MinWeightThreshold)
            {
                var pending = GetPendingVehicleData();
                ClearPendingData();
                return RfidWorkflowDecision.SaveNow(currentWeight, pending);
            }

            return RfidWorkflowDecision.Pending($"NHẬN XE: {_pendingLicensePlate}. ĐỢI ỔN ĐỊNH...");
        }
    }

    public sealed record PendingVehicleData(string LicensePlate, string CustomerName, string ProductName, int VehicleId);

    public sealed record ScaleWorkflowDecision
    {
        public bool ShouldClearPendingAndReset { get; init; }
        public bool ShouldSave { get; init; }
        public decimal WeightToSave { get; init; }
        public PendingVehicleData PendingVehicle { get; init; }
        public string CameraMessage { get; init; }

        public static ScaleWorkflowDecision None() => new();
        public static ScaleWorkflowDecision ClearPendingAndReset(string message) => new()
        {
            ShouldClearPendingAndReset = true,
            CameraMessage = message
        };

        public static ScaleWorkflowDecision SaveWithPending(decimal weight, PendingVehicleData pending) => new()
        {
            ShouldSave = true,
            WeightToSave = weight,
            PendingVehicle = pending
        };
    }

    public sealed record RfidWorkflowDecision
    {
        public bool ShouldShowMessage { get; init; }
        public string CameraMessage { get; init; }
        public bool MessageAutoHide { get; init; } = true;
        public bool ShouldStartPendingTimeout { get; init; }
        public bool ShouldSave { get; init; }
        public decimal WeightToSave { get; init; }
        public PendingVehicleData PendingVehicle { get; init; }

        public static RfidWorkflowDecision Message(string message) => new()
        {
            ShouldShowMessage = true,
            CameraMessage = message
        };

        public static RfidWorkflowDecision Pending(string message) => new()
        {
            ShouldShowMessage = true,
            CameraMessage = message,
            MessageAutoHide = false,
            ShouldStartPendingTimeout = true
        };

        public static RfidWorkflowDecision SaveNow(decimal weight, PendingVehicleData pending) => new()
        {
            ShouldSave = true,
            WeightToSave = weight,
            PendingVehicle = pending
        };
    }
}
