// Services/DeviceService.cs
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
            {
                return new DeviceRegistrationResponse
                {
                    Success = false,
                    Message = "Invalid device code format"
                };
            }

            var androidId = deviceCodeData.GetValueOrDefault("android_id");
            var deviceModel = deviceCodeData.GetValueOrDefault("device_model");
            var serialNumber = deviceCodeData.GetValueOrDefault("serial_number");
            var installationId = deviceCodeData.GetValueOrDefault("installation_id");
            var publicKey = deviceCodeData.GetValueOrDefault("public_key");
            var brand = deviceCodeData.GetValueOrDefault("brand");
            var manufacturer = deviceCodeData.GetValueOrDefault("manufacturer");

            if (string.IsNullOrEmpty(androidId) || string.IsNullOrEmpty(installationId))
            {
                return new DeviceRegistrationResponse
                {
                    Success = false,
                    Message = "Missing required device information"
                };
            }

            // Employee association
            var employee = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.EmployeeUsername);
            if (employee == null)
            {
                employee = new User
                {
                    Username = request.EmployeeUsername,
                    Email = $"{request.EmployeeUsername}@example.com",
                    PasswordHash = "default",
                    FirstName = request.EmployeeUsername,
                    LastName = "",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Users.Add(employee);
                await _context.SaveChangesAsync();
            }

            // Check if device already exists
            var existingDevice = await GetDeviceByAndroidIdAsync(androidId);
            if (existingDevice != null)
            {
                return new DeviceRegistrationResponse
                {
                    Success = false,
                    Message = "Device already registered"
                };
            }

            var existingInstallation = await _context.Devices
                .FirstOrDefaultAsync(d => d.InstallationId == installationId);
            if (existingInstallation != null)
            {
                return new DeviceRegistrationResponse
                {
                    Success = false,
                    Message = "Installation ID already used"
                };
            }

            // Create device
            var device = new Device
            {
                UserId = employee.Id,
                AndroidId = androidId,
                DeviceModel = deviceModel,
                SerialNumber = serialNumber,
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

            // Generate activation code
            var activationCode = GenerateActivationCode();
            device.ActivationCode = activationCode;
            device.ActivationCodeExpiry = DateTime.UtcNow.AddMinutes(3);
            await _context.SaveChangesAsync();

            // Generate device token (for compatibility)
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
            return new DeviceRegistrationResponse
            {
                Success = false,
                Message = $"Error registering device: {ex.Message}"
            };
        }
    }

    // ==================== GET DEVICE BY ACTIVATION CODE ====================

    public async Task<Device?> GetDeviceByActivationCodeAsync(string activationCode)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.ActivationCode == activationCode);

        if (device != null && device.ActivationCodeExpiry.HasValue)
        {
            if (DateTime.UtcNow > device.ActivationCodeExpiry.Value)
            {
                _logger.LogWarning($"Activation code {activationCode} has expired");
                return null;
            }
        }

        return device;
    }

    // ==================== ACTIVATION ====================

    public async Task<ActivationResponse> ActivateDeviceAsync(ActivationRequest request)
    {
        try
        {
            var device = await GetDeviceByIdAsync(request.DeviceId);
            if (device == null)
            {
                return new ActivationResponse
                {
                    Success = false,
                    Message = "Device not found"
                };
            }

            if (device.Status == "ACTIVE")
            {
                return new ActivationResponse
                {
                    Success = false,
                    Message = "Device already active"
                };
            }

            if (device.ActivationCode != request.ActivationCode)
            {
                return new ActivationResponse
                {
                    Success = false,
                    Message = "Invalid activation code"
                };
            }

            if (device.ActivationCodeExpiry.HasValue && DateTime.UtcNow > device.ActivationCodeExpiry.Value)
            {
                return new ActivationResponse
                {
                    Success = false,
                    Message = "Activation code has expired. Please register again."
                };
            }

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
            return new ActivationResponse
            {
                Success = false,
                Message = $"Error activating device: {ex.Message}"
            };
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
        var challenge = _cache.Get<string>($"challenge_{deviceId}");
        return await Task.FromResult(challenge ?? string.Empty);
    }

    public async Task ClearChallengeAsync(int deviceId)
    {
        _cache.Remove($"challenge_{deviceId}");
        await Task.CompletedTask;
    }

    public bool VerifySignature(string challenge, string signature, string publicKey)
    {
        try
        {
            var decoded = Convert.FromBase64String(signature);
            var decodedString = Encoding.UTF8.GetString(decoded);
            return decodedString.Contains(challenge);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature");
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
            await _context.SaveChangesAsync();
        }
    }

    public async Task MarkDeviceAsTrustedAsync(int deviceId)
    {
        await Task.CompletedTask;
    }

    // ==================== GET DEVICE BY SECRET KEY (DEPRECATED) ====================

    public async Task<Device?> GetDeviceBySecretKeyAsync(string secretKey)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.SecretKey == secretKey);
    }

    // ==================== OTP VERIFICATION (DEPRECATED) ====================

    public async Task<OTPVerificationResponse> VerifyOTPAsync(OTPVerificationRequest request)
    {
        return await Task.FromResult(new OTPVerificationResponse
        {
            Success = false,
            Message = "OTP verification is deprecated. Use challenge-response.",
            Valid = false
        });
    }

    // ==================== CHALLENGE-RESPONSE FOR ACTIONS ====================

    public async Task<ChallengeResponse> CreateChallengeAsync(string actionType, int actionId, int employeeId)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.UserId == employeeId && d.Status == "ACTIVE");

        if (device == null)
            throw new InvalidOperationException("No active device found for this employee.");

        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        var challengeStr = Convert.ToBase64String(bytes);

        var deviceChallenge = new DeviceChallenge
        {
            Challenge = challengeStr,
            ActionType = actionType,
            ActionId = actionId,
            Expiry = DateTime.UtcNow.AddMinutes(5),
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow,
            DeviceId = device.Id
        };
        _context.DeviceChallenges.Add(deviceChallenge);
        await _context.SaveChangesAsync();

        return new ChallengeResponse
        {
            Challenge = challengeStr,
            ExpiresIn = 300
        };
    }

    public async Task<bool> VerifyChallengeAsync(string installationId, string signature, string challenge)
    {
        var device = await _context.Devices
            .FirstOrDefaultAsync(d => d.InstallationId == installationId);
        if (device == null)
            return false;

        var challengeEntity = await _context.DeviceChallenges
            .FirstOrDefaultAsync(c => c.Challenge == challenge && c.Status == "PENDING");
        if (challengeEntity == null)
            return false;

        if (challengeEntity.Expiry < DateTime.UtcNow)
        {
            challengeEntity.Status = "EXPIRED";
            await _context.SaveChangesAsync();
            return false;
        }

        var isValid = VerifySignature(challenge, signature, device.PublicKey);
        if (!isValid)
            return false;

        challengeEntity.Status = "COMPLETED";
        challengeEntity.CompletedAt = DateTime.UtcNow;
        challengeEntity.DeviceId = device.Id;
        await _context.SaveChangesAsync();

        if (challengeEntity.ActionType == "BudgetApproval")
        {
            var budget = await _context.BudgetApprovals.FindAsync(challengeEntity.ActionId);
            if (budget != null && budget.Status == "PENDING")
            {
                budget.Status = "APPROVED";
                budget.ApprovedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        return true;
    }

    // ==================== OTP GENERATION (DEPRECATED) ====================

    public async Task<OTPGenerateResponse> GenerateOTPAsync(int userId, int deviceId)
    {
        return await Task.FromResult(new OTPGenerateResponse
        {
            Success = false,
            Message = "OTP generation is deprecated. Use challenge-response."
        });
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
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }
}