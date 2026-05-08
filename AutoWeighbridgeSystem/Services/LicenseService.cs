using System;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    public enum LicenseStatus
    {
        Valid,
        Expired,
        NoLicense,
        MachineMismatch,
        InvalidSignature
    }

    public class LicenseData
    {
        public string MachineId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
    }

    public sealed class LicenseService
    {
        private const string LicenseFileName = "license.dat";
        private const string KeyFileName = "key.dat";

        public event Action? LicenseStatusChanged;

        private LicenseStatus _currentStatus = LicenseStatus.NoLicense;
        public LicenseStatus CurrentStatus 
        { 
            get => _currentStatus;
            private set
            {
                if (_currentStatus != value)
                {
                    _currentStatus = value;
                    LicenseStatusChanged?.Invoke();
                }
            }
        }
        public LicenseData? CurrentLicense { get; private set; }
        public string MachineId => GetMachineId();

        public LicenseService()
        {
            ValidateCurrentLicense();
        }

        public string GetRemainingDays()
        {
            if (CurrentStatus != LicenseStatus.Valid || CurrentLicense == null) return "0";
            var remaining = CurrentLicense.ExpiryDate.Date - DateTime.Today;
            return remaining.Days > 0 ? remaining.Days.ToString() : "Hết hạn";
        }

        private static readonly byte[] _scrambleSalt = { 0x41, 0x75, 0x74, 0x6f, 0x57, 0x62, 0x32, 0x30, 0x32, 0x34 };

        public LicenseStatus ValidateCurrentLicense()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string licensePath = Path.Combine(appDir, LicenseFileName);
            string keyPath = Path.Combine(appDir, KeyFileName);

            if (!File.Exists(keyPath)) 
            {
                CurrentStatus = LicenseStatus.NoLicense;
                return CurrentStatus;
            }

            try
            {
                string scrambledKey = File.ReadAllText(keyPath).Trim();
                string secretKey = Descramble(scrambledKey);
                return ValidateLicenseFromFile(licensePath, secretKey);
            }
            catch
            {
                CurrentStatus = LicenseStatus.InvalidSignature;
                return CurrentStatus;
            }
        }

        public string Descramble(string scrambledText)
        {
            try
            {
                byte[] data = Convert.FromBase64String(scrambledText);
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)(data[i] ^ _scrambleSalt[i % _scrambleSalt.Length]);
                }
                return Encoding.UTF8.GetString(data);
            }
            catch { return string.Empty; }
        }

        public LicenseStatus ValidateLicenseFromFile(string filePath, string secretKey)
        {
            if (!File.Exists(filePath) || string.IsNullOrEmpty(secretKey))
            {
                CurrentStatus = LicenseStatus.NoLicense;
                return CurrentStatus;
            }

            try
            {
                string encryptedBase64 = File.ReadAllText(filePath);
                string decryptedJson = Decrypt(encryptedBase64, secretKey);
                
                var data = JsonSerializer.Deserialize<LicenseData>(decryptedJson);
                if (data == null) return LicenseStatus.NoLicense;

                if (data.MachineId != MachineId)
                {
                    CurrentStatus = LicenseStatus.MachineMismatch;
                    return CurrentStatus;
                }

                if (data.ExpiryDate < DateTime.Today)
                {
                    CurrentStatus = LicenseStatus.Expired;
                    return CurrentStatus;
                }

                CurrentLicense = data;
                CurrentStatus = LicenseStatus.Valid;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[LICENSE] Lỗi xác thực bản quyền với Key cung cấp.");
                CurrentStatus = LicenseStatus.InvalidSignature;
            }

            return CurrentStatus;
        }

        public void ImportLicense(string sourceLicensePath, string sourceKeyPath)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string destLicensePath = Path.Combine(appDir, LicenseFileName);
                string destKeyPath = Path.Combine(appDir, KeyFileName);

                if (!sourceLicensePath.Equals(destLicensePath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourceLicensePath, destLicensePath, true);

                if (!sourceKeyPath.Equals(destKeyPath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourceKeyPath, destKeyPath, true);

                Log.Information("[LICENSE] Đã import bộ đôi file bản quyền và key mới.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[LICENSE] Không thể copy bộ file bản quyền vào thư mục hệ thống.");
            }
        }

        private string GetMachineId()
        {
            try
            {
                string mbSerial = "UNKNOWN";
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        mbSerial = obj["SerialNumber"]?.ToString()?.Trim() ?? "UNKNOWN";
                        break;
                    }
                }

                if (mbSerial == "UNKNOWN" || mbSerial.Contains("Default string"))
                    mbSerial = Environment.MachineName + "-AWB-ID";

                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mbSerial));
                    string hex = BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
                    return Enumerable.Range(0, 4)
                        .Select(i => hex.Substring(i * 4, 4))
                        .Aggregate((a, b) => $"{a}-{b}");
                }
            }
            catch { return "ERROR-ID"; }
        }

        private string Decrypt(string encryptedBase64, string password)
        {
            byte[] fullCipher = Convert.FromBase64String(encryptedBase64);
            using (Aes aes = Aes.Create())
            {
                using (var sha256 = SHA256.Create())
                {
                    aes.Key = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                }

                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs))
                {
                    return sr.ReadToEnd();
                }
            }
        }
    }
}
