using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace AutoWeighbridgeSystem.Services.Protocols
{
    /// <summary>
    /// Contract để tạo <see cref="IScaleProtocol"/> từ tên giao thức dạng chuỗi (lấy từ appsettings.json).
    /// </summary>
    public interface IScaleProtocolFactory
    {
        /// <summary>
        /// Tạo instance của giao thức khớp với <paramref name="protocolName"/>.
        /// Nếu không tìm thấy, tự động fallback về VishayVT220 và ghi Warning.
        /// </summary>
        IScaleProtocol Create(string protocolName);

        /// <summary>
        /// Danh sách tên tất cả giao thức đang được hỗ trợ.
        /// Dùng để populate dropdown trong SettingsView.
        /// </summary>
        IReadOnlyList<string> SupportedProtocols { get; }
    }

    /// <summary>
    /// Implementation của <see cref="IScaleProtocolFactory"/> dùng Dictionary để map tên → instance factory.
    /// <para>
    /// <b>Thêm giao thức mới</b>: Chỉ cần bổ sung một dòng vào <c>_registry</c>.
    /// Không cần sửa bất kỳ switch-case nào ở App.xaml.cs hay SettingsViewModel.
    /// </para>
    /// </summary>
    public sealed class ScaleProtocolFactory : IScaleProtocolFactory
    {
        // =========================================================================
        // ĐĂNG KÝ GIAO THỨC — Thêm giao thức mới vào đây
        // =========================================================================
        private static readonly Dictionary<string, Func<IScaleProtocol>> _registry =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["VishayVT220"] = () => new VishayVT220protocol(),

                // --- Thêm giao thức mới theo mẫu bên dưới ---
                // ["Toledo8142"]  = () => new Toledo8142Protocol(),
                // ["MettlerIND"]  = () => new MettlerIndProtocol(),
            };

        // =========================================================================
        // PUBLIC API
        // =========================================================================

        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedProtocols { get; } =
            _registry.Keys.OrderBy(k => k).ToList();

        /// <inheritdoc/>
        public IScaleProtocol Create(string protocolName)
        {
            if (!string.IsNullOrWhiteSpace(protocolName) &&
                _registry.TryGetValue(protocolName, out var factory))
            {
                return factory();
            }

            Log.Warning("[PROTOCOL] Giao thức '{Name}' không tìm thấy trong registry. Dùng VishayVT220 mặc định.", protocolName);
            return new VishayVT220protocol();
        }
    }
}
