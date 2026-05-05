using System;

namespace AutoWeighbridgeSystem.Services
{
    public enum ViewType
    {
        Unknown,
        Dashboard,
        VehicleRegistration,
        WeighingHistory,
        Settings,
        Reports
    }

    /// <summary>
    /// Dịch vụ đơn giản để theo dõi xem người dùng đang đứng ở Tab nào.
    /// Giúp BackgroundAutomationService quyết định có nên hiện Toast thông báo hay không.
    /// </summary>
    public sealed class ViewTrackerService
    {
        public ViewType CurrentView { get; set; } = ViewType.Unknown;
    }
}
