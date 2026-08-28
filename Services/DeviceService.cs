using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PayrollSystem.API.Services;

public class DeviceService : IDeviceService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(AppDbContext context, IMemoryCache cache, ILogger<DeviceService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    // ==================== REGISTRATION ====================

    public async Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request)
    {
        try
        {
            var deviceCodeData = JsonSerializer.Deserialize<Dictionary<string, string>>(request.DeviceCode);
            if (deviceCodeData == null)
                return new DeviceRegistrationResponse { Success = false, Message = "Invalid device code format" };

            var androidId = deviceCodeData.GetValueOrDefault("android_id");
            var deviceModel = deviceCodeData.GetValueOrDefault("device_model");
            var installationId = deviceCodeData.GetValueOrDefault("installation_id");
            var publicKey = deviceCodeData.GetValueOrDefault("public_key");
            var brand = deviceCodeData.GetValueOrDefault("brand");
            var manufacturer = deviceCodeData.GetValueOrDefault("manufacturer");

            if (string.IsNullOrEmpty(androidId) || string.IsNullOrEmpty(installationId))
                return new DeviceRegistrationResponse { Success = false, Message = "Missing required device information" };

            // ✅ PUBLIC KEY is the primary identity – enforce uniqueness
            var existingByPublicKey = await _context.Devices
                .FirstOrDefaultAsync(d => d.PublicKey == publicKey);
            if (existingByPublicKey != null)
                return new DeviceRegistrationResponse { Success = false, Message = "Public key already exists." };

            // Supplementary checks (not security-critical, but help with duplicate detection)
            var existingByAndroid = await GetDeviceByAndroidIdAsync(androidId);
            if (existingByAndroid != null)
                return new DeviceRegistrationResponse { Success = false, Message = "This device (Android ID) is already registered." };

            var existingByInstallation = await GetDeviceByInstallationIdAsync(installationId);
            if (existingByInstallation != null)
                return new DeviceRegistrationResponse { Success = false, Message = "This installation ID is already registered." };

            // Employee association – must exist
            var employee = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.EmployeeUsername);

            if (employee == null)
                return new DeviceRegistrationResponse { Success = false, Message = "Employee not found." };

            var device = new Device
            {
                UserId = employee.Id,
                AndroidId = androidId,
                DeviceModel = deviceModel,
                InstallationId = installationId,
                PublicKey = publicKey,
                Brand = brand,
                Manufacturer = manufacturer,
                DeviceName = request.DeviceName ?? deviceModel,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            _context.Devices.Add(device);
            await _context.SaveChangesAsync();

            var activationCode = GenerateActivationCode();
            device.ActivationCode = activationCode;
            device.ActivationCodeExpiry = DateTime.UtcNow.AddMinutes(3);
            await _context.SaveChangesAsync();

            var deviceToken = Guid.NewGuid().ToString();
            device.DeviceToken = deviceToken;
            await _context.SaveChangesAsync();

            return new DeviceRegistrationResponse
            {
                Success = true,
                Message = "Device registered successfully",
                ActivationCode = activationCode,
                DeviceToken = deviceToken,
                DeviceId = device.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return new DeviceRegistrationResponse { Success = false, Message = $"Error registering device: {ex.Message}" };
        }
    }

    // ==================== GET DEVICE BY ACTIVATION CODE ====================

    public async Task<Device?> GetDeviceByActivationCodeAsync(string activationCode)
{
    var device = await _context.Devices
        .FirstOrDefaultAsync(d => d.ActivationCode == activationCode);

    if (device != null && device.ActivationCodeExpiry.HasValue && DateTime.UtcNow > device.ActivationCodeExpiry.Value)
    {
        _logger.LogWarning($"Activation code {activationCode} has expired");
        return null;
    }

    return device;
}

    // ==================== GET DEVICE BY SECRET KEY ====================

    public async Task<Device?> GetDeviceBySecretKeyAsync(string secretKey)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.SecretKey == secretKey);
    }

    // ==================== GET DEVICE BY INSTALLATION ID ====================

    public async Task<Device?> GetDeviceByInstallationIdAsync(string installationId)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.InstallationId == installationId);
    }

    // ==================== ACTIVATION ====================

    public async Task<ActivationResponse> ActivateDeviceAsync(ActivationRequest request)
    {
        try
        {
            var device = await GetDeviceByIdAsync(request.DeviceId);
            if (device == null)
                return new ActivationResponse { Success = false, Message = "Device not found" };

            if (device.Status == "ACTIVE")
                return new ActivationResponse { Success = false, Message = "Device already active" };

            if (device.ActivationCode != request.ActivationCode)
                return new ActivationResponse { Success = false, Message = "Invalid activation code" };

            if (device.ActivationCodeExpiry.HasValue && DateTime.UtcNow > device.ActivationCodeExpiry.Value)
                return new ActivationResponse { Success = false, Message = "Activation code has expired. Please register again." };

            device.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return new ActivationResponse
            {
                Success = true,
                Message = "Activation code verified. Please complete device verification.",
                Status = "VERIFYING"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating device");
            return new ActivationResponse { Success = false, Message = $"Error activating device: {ex.Message}" };
        }
    }

    // ==================== CHALLENGE-RESPONSE (activation) ====================

    public async Task StoreChallengeAsync(int deviceId, string challenge)
    {
        _cache.Set($"challenge_{deviceId}", challenge, TimeSpan.FromSeconds(60));
        await Task.CompletedTask;
    }

    public async Task<string> GetStoredChallengeAsync(int deviceId)
    {
        return await Task.FromResult(_cache.Get<string>($"challenge_{deviceId}") ?? string.Empty);
    }

    public async Task ClearChallengeAsync(int deviceId)
    {
        _cache.Remove($"challenge_{deviceId}");
        await Task.CompletedTask;
    }

    // ==================== SIGNATURE VERIFICATION (RSA ONLY – NO FALLBACK) ====================

    public bool VerifySignature(string challenge, string signature, string publicKey)
    {
        try
        {
            if (string.IsNullOrEmpty(publicKey))
                return false;

            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);

            var challengeBytes = Encoding.UTF8.GetBytes(challenge);
            var sigBytes = Convert.FromBase64String(signature);

            return rsa.VerifyData(
                challengeBytes,
                sigBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
        }
        catch
        {
            // ❌ FALLBACK REMOVED – only real RSA verification is accepted
            return false;
        }
    }

    public async Task UpdateDeviceCredentialsAsync(int deviceId, string deviceToken, string secretKey)
    {
        var device = await GetDeviceByIdAsync(deviceId);
        if (device != null)
        {
            device.DeviceToken = deviceToken;
            device.SecretKey = secretKey;
            device.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("Duplicate entry") == true)
            {
                _logger.LogError(ex, "Duplicate secret key or device token");
                throw new InvalidOperationException("Device token or secret key already exists. Please try activating again.");
            }
        }
    }

    public async Task MarkDeviceAsTrustedAsync(int deviceId)
    {
        await Task.CompletedTask;
    }

    // ==================== DEVICE MANAGEMENT ====================

    public async Task<Device?> GetDeviceByAndroidIdAsync(string androidId)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.AndroidId == androidId);
    }

    public async Task<Device?> GetDeviceByIdAsync(int deviceId)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId);
    }

    public async Task<List<Device>> GetUserDevicesAsync(int userId)
    {
        return await _context.Devices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<Device?> GetActiveDeviceAsync()
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.Status == "ACTIVE");
    }

    public async Task<List<Device>> GetAllActiveDevicesAsync()
    {
        return await _context.Devices
            .Where(d => d.Status == "ACTIVE")
            .ToListAsync();
    }

    public async Task<bool> IsDeviceActiveAsync(int deviceId)
    {
        var device = await GetDeviceByIdAsync(deviceId);
        return device != null && device.Status == "ACTIVE";
    }

    public async Task<bool> UpdateDeviceStatusAsync(int deviceId, string status)
    {
        var device = await GetDeviceByIdAsync(deviceId);
        if (device == null) return false;

        device.Status = status;
        device.UpdatedAt = DateTime.UtcNow;
        if (status == "ACTIVE")
            device.ActivatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteDeviceAsync(int deviceId)
    {
        var device = await GetDeviceByIdAsync(deviceId);
        if (device == null) return false;

        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();
        return true;
    }

    // ==================== HELPERS ====================

    private string GenerateActivationCode()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        var value = BitConverter.ToUInt32(bytes, 0) % 900000 + 100000;
        return value.ToString("D6");
    }
}