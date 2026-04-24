namespace AutoWeighbridgeSystem.Common
{
    public static class UiText
    {
        public static class Titles
        {
            public const string Warning = "Cảnh báo";
            public const string WarningUpper = "Cảnh Báo";
            public const string Info = "Thông báo";
            public const string Confirm = "Xác nhận";
            public const string Cancel = "Hủy";
            public const string Error = "Lỗi";
            public const string SystemError = "Lỗi Hệ Thống";
            public const string Success = "Thành công";
            public const string Printer = "Máy in";
        }

        public static class Camera
        {
            public const string OnlineStatus = "Camera Online";
        }

        public static class Messages
        {
            public const string LogoutConfirm = "Bạn có chắc chắn muốn đăng xuất?";
            public const string ManualModeDisableAuto = "Vui lòng tắt chế độ AUTO!";
            public const string EnterLicensePlate = "Vui lòng nhập Biển số xe!";
            public const string SelectCustomerFromList = "Vui lòng chọn khách hàng từ danh sách!";
            public const string LockWeightBeforeSave = "Vui lòng chốt số cân!";
            public const string UnstableScaleWarning = "Cân đang dao động, vui lòng đợi ổn định!";
            public const string CancelTicketConfirm = "Xác nhận HỦY phiếu?";

            public const string SaveConfigSuccess = "Lưu cấu hình thành công! Hãy khởi động lại ứng dụng.";
            public const string NoDataToExport = "Không có dữ liệu để xuất!";
            public const string RestoreDeletedVehicleConfirm = "Xe này đã bị xóa trước đó. Khôi phục?";
            public const string VehicleAlreadyExists = "Xe này đã tồn tại!";
            public const string VehicleSaveSuccess = "Lưu thông tin thành công!";
            public const string VehicleDeleteConfirm = "Xác nhận xóa?";

            public const string ProductIdRequired = "Mã sản phẩm không được để trống!";
            public const string ProductNameRequired = "Tên sản phẩm không được để trống!";
            public const string ProductAlreadyExists = "Mã sản phẩm đã tồn tại!";
            public const string ProductNotFoundToUpdate = "Không tìm thấy sản phẩm để cập nhật!";
            public const string ProductSaveSuccess = "Lưu dữ liệu thành công!";
            public const string ProductDeleteSuccess = "Đã xóa sản phẩm thành công!";

            public const string CustomerIdRequired = "Mã khách hàng không được để trống!";
            public const string CustomerAlreadyExists = "Mã khách hàng này đã tồn tại!";
            public const string CustomerSaveSuccess = "Lưu thành công!";
            public const string CustomerDeleteSuccess = "Đã xóa khách hàng thành công!";

            public static string SaveConfigError(string message) => "Lỗi lưu cấu hình: " + message;
            public static string SqlConnectionErrorTitle() => $"{Titles.SystemError} - SQL Connection";
            public static string DataQueryError(string message) => $"Lỗi truy xuất dữ liệu: {message}";
            public static string VoidTicketConfirm(string ticketId) => $"Xác nhận HỦY phiếu: {ticketId}?";
            public static string PrintTicketInfo(string ticketId) => $"Đang in lại phiếu {ticketId}...";
            public static string ScaleBelowThreshold(decimal currentWeight, decimal minWeightThreshold)
                => $"Khối lượng hiện tại ({currentWeight:N0} kg) thấp hơn ngưỡng tối thiểu ({minWeightThreshold:N0} kg).";
            public static string GenericError(string message) => "Lỗi: " + message;
            public static string SystemErrorWithDetail(string message) => "Lỗi hệ thống: " + message;
            public static string DeleteError(string message) => "Lỗi khi xóa: " + message;
            public static string RestoreDeletedProductConfirm(string productId) => $"Mã [{productId}] đã bị xóa. Khôi phục?";
            public static string DeleteProductConfirm(string productId) => $"Xác nhận xóa mã: {productId}?";
            public static string RestoreDeletedCustomerConfirm(string customerId) => $"Mã [{customerId}] đã bị xóa trước đó. Khôi phục?";
            public static string DeleteCustomerConfirm(string customerName, string customerId)
                => $"Xác nhận xóa khách hàng: {customerName}?\n(Mã: {customerId})";
            public static string SaveTicketConfirm(string licensePlate, decimal lockedWeight)
                => $"Lưu phiếu cho xe {licensePlate} - {lockedWeight:N0} kg?";
            public static string AutoSaveSuccessOverlay(string licensePlate, decimal finalWeight, string serverMessage)
                => $"🔒 ĐÃ CHỐT: {finalWeight:N0} KG\n{serverMessage}";
        }
    }
}
