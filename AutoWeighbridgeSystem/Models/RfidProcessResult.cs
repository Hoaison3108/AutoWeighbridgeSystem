using AutoWeighbridgeSystem.Models;

namespace AutoWeighbridgeSystem.Models
{
    public class RfidProcessResult
    {
        public bool IsSuccess { get; set; }
        public string CleanCardId { get; set; }
        public Vehicle ExistingVehicle { get; set; } // Sẽ là null nếu đây là thẻ hoàn toàn mới
        public string ErrorMessage { get; set; }

        public bool IsNewCard => IsSuccess && ExistingVehicle == null;
    }
}