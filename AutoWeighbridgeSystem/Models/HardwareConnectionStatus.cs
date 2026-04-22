namespace AutoWeighbridgeSystem.Models
{
    /// <summary>
    /// Trạng thái kết nối của một thiết bị phần cứng (đầu cân, RFID, camera).
    /// </summary>
    public enum HardwareConnectionStatus
    {
        /// <summary>Đang kết nối hoặc chưa xác định trạng thái.</summary>
        Connecting,
        /// <summary>Kết nối thành công, đang hoạt động bình thường.</summary>
        Online,
        /// <summary>Đang thử kết nối lại sau khi mất kết nối.</summary>
        Reconnecting,
        /// <summary>Mất kết nối hoàn toàn, không thể kết nối lại.</summary>
        Offline,
        /// <summary>Thiết bị bị vô hiệu hóa chủ động (None).</summary>
        Disabled
    }
}
