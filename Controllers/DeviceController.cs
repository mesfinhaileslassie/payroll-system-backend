using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using PayrollSystem.API.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DeviceController> _logger;
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;

    public DeviceController(IDeviceService deviceService, ILogger<DeviceController> logger, AppDbContext context, IMemoryCache cache)
    {
        _deviceService = deviceService;
        _logger = logger;
        _context = context;
        _cache = cache;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("User ID not found in token.");
        return userId;
    }

    private async Task<bool> ValidateDeviceOwnership(int deviceId, int currentUserId)
    {
        var device = await _deviceService.GetDeviceByIdAsync(deviceId);
        if (device == null) return false;
        if (User.IsInRole("Admin")) return true;
        return device.UserId == currentUserId;
    }

    // ==================== PUBLIC ENDPOINTS (AllowAnonymous) ====================

    [Authorize(Roles = "Admin")]
    [HttpPost("register")]
    [EnableRateLimiting("DeviceRegistrationPolicy")] 
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceCode))
                return BadRequest(new { success = false, message = "Device code is required" });
            if (string.IsNullOrEmpty(request.EmployeeUsername))
                return BadRequest(new { success = false, message = "Employee username is required" });

            var result = await _deviceService.RegisterDeviceAsync(request);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [AllowAnonymous]
    [HttpGet("get-device-id/{activationCode}")]
    [EnableRateLimiting("ActivationCodePolicy")] 
    public async Task<IActionResult> GetDeviceIdByActivationCode(string activationCode)
    {
        try
        {
            _logger.LogInformation($"📱 Activation code received: '{activationCode}'");
            if (string.IsNullOrEmpty(activationCode) || activationCode.Length != 6)
                return BadRequest(new { success = false, message = "Invalid activation code format" });

            var device = await _deviceService.GetDeviceByActivationCodeAsync(activationCode);
            if (device == null)
            {
                _logger.LogWarning($"❌ No device found for activation code: '{activationCode}'");
                return NotFound(new { success = false, message = "Invalid activation code" });
            }

            _logger.LogInformation($" Device found: Id={device.Id}, Status={device.Status}, Expiry={device.ActivationCodeExpiry}");
            return Ok(new
            {
                success = true,
                deviceId = device.Id,
                status = device.Status,
                message = "Device found",
                data = new
                {
                    deviceId = device.Id,
                    status = device.Status,
                    message = "Device found"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting device by activation code: {activationCode}");
            return StatusCode(500, new { success = false, message = $"Server error: {ex.Message}" });
        }
    }

    [AllowAnonymous]
    [HttpGet("{id}/challenge")]
    [EnableRateLimiting("ActivationCodePolicy")]
    public async Task<IActionResult> GetChallenge(int id)
    {
        try
        {
            var device = await _deviceService.GetDeviceByIdAsync(id);
            if (device == null)
                return NotFound(new { success = false, message = "Device not found" });

            var challenge = GenerateChallenge();
            await _deviceService.StoreChallengeAsync(id, challenge);

            return Ok(new
            {
                success = true,
                challenge = challenge,
                expiresIn = 60
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating challenge");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [AllowAnonymous]
    [HttpPost("verify-signature")]
    [EnableRateLimiting("ActivationCodePolicy")]
    public async Task<IActionResult> VerifySignature([FromBody] SignatureVerificationRequest request)
    {
        try
        {
            if (request.DeviceId <= 0 || string.IsNullOrEmpty(request.Signature))
                return BadRequest(new { success = false, message = "Device ID and signature are required" });

            var storedChallenge = await _deviceService.GetStoredChallengeAsync(request.DeviceId);
            if (string.IsNullOrEmpty(storedChallenge))
                return BadRequest(new
                {
                    success = false,
                    message = "No challenge found or challenge expired. Please request a new challenge."
                });

            var device = await _deviceService.GetDeviceByIdAsync(request.DeviceId);
            if (device == null)
                return NotFound(new { success = false, message = "Device not found" });

            bool isValid = _deviceService.VerifySignature(storedChallenge, request.Signature, device.PublicKey);
            if (!isValid)
                return BadRequest(new { success = false, message = "Signature verification failed" });

            await _deviceService.UpdateDeviceStatusAsync(request.DeviceId, "ACTIVE");
            await _deviceService.MarkDeviceAsTrustedAsync(request.DeviceId);

           
            var deviceToken = GenerateDeviceToken();
            await _deviceService.UpdateDeviceCredentialsAsync(request.DeviceId, deviceToken, device.SecretKey);

            await _deviceService.ClearChallengeAsync(request.DeviceId);

            return Ok(new
            {
                success = true,
                message = "Device verified and activated successfully",
                deviceToken = deviceToken,
                secretKey = device.SecretKey, // ← return the stored Base32 secret
                status = "ACTIVE",
                trusted = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== PROTECTED ENDPOINTS ====================

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDevice(int id)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            if (!await ValidateDeviceOwnership(id, currentUserId))
                return Forbid();

            var device = await _deviceService.GetDeviceByIdAsync(id);
            if (device == null)
                return NotFound(new { success = false, message = "Device not found" });

            return Ok(new
            {
                success = true,
                data = new
                {
                    id = device.Id,
                    androidId = device.AndroidId,
                    deviceModel = device.DeviceModel,
                    deviceName = device.DeviceName,
                    status = device.Status,
                    deviceToken = device.DeviceToken,
                    installationId = device.InstallationId,
                    brand = device.Brand,
                    manufacturer = device.Manufacturer,
                    publicKey = device.PublicKey,
                    userId = device.UserId
                }
            });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllDevices()
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            IQueryable<Device> query = _context.Devices;
            if (!User.IsInRole("Admin"))
                query = query.Where(d => d.UserId == currentUserId);

            var devices = await query
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    d.Id,
                    d.DeviceName,
                    d.DeviceModel,
                    d.Status,
                    d.UserId,
                    d.InstallationId,
                    d.CreatedAt,
                    d.AndroidId
                })
                .ToListAsync();

            return Ok(new { success = true, data = devices });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all devices");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [HttpGet("by-installation/{installationId}")]
    public async Task<IActionResult> GetDeviceByInstallationId(string installationId)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            if (string.IsNullOrEmpty(installationId))
                return BadRequest(new { success = false, message = "Installation ID is required" });

            var device = await _deviceService.GetDeviceByInstallationIdAsync(installationId);
            if (device == null)
                return NotFound(new { success = false, message = "Device not found" });

            if (!User.IsInRole("Admin") && device.UserId != currentUserId)
                return Forbid();

            return Ok(new
            {
                success = true,
                data = new
                {
                    device.Id,
                    device.AndroidId,
                    device.DeviceModel,
                    device.DeviceName,
                    device.Status,
                    device.InstallationId,
                    device.UserId
                }
            });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device by installationId");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("check-registration")]
    public async Task<IActionResult> CheckDeviceRegistration([FromQuery] string installationId)
    {
        try
        {
            if (string.IsNullOrEmpty(installationId))
                return BadRequest(new { success = false, message = "Installation ID is required" });

            var device = await _deviceService.GetDeviceByInstallationIdAsync(installationId);
            if (device == null)
                return Ok(new { registered = false, status = (string?)null, deviceId = (int?)null });

            return Ok(new { registered = true, status = device.Status, deviceId = device.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking device registration");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDevice(int id, [FromBody] UpdateDeviceRequest request)
    {
        try
        {
            if (id <= 0 || string.IsNullOrEmpty(request.DeviceName))
                return BadRequest(new { success = false, message = "Invalid request" });

            var currentUserId = GetCurrentUserId();
            if (!await ValidateDeviceOwnership(id, currentUserId))
                return Forbid();

            var device = await _deviceService.GetDeviceByIdAsync(id);
            if (device == null)
                return NotFound(new { success = false, message = "Device not found" });

            device.DeviceName = request.DeviceName;
            if (!string.IsNullOrEmpty(request.Status) && (request.Status == "ACTIVE" || request.Status == "INACTIVE" || request.Status == "PENDING"))
                device.Status = request.Status;
            device.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Device updated successfully" });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDevice(int id)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            if (!await ValidateDeviceOwnership(id, currentUserId))
                return Forbid();

            var result = await _deviceService.DeleteDeviceAsync(id);
            if (!result)
                return NotFound(new { success = false, message = "Device not found" });

            return Ok(new { success = true, message = "Device deleted successfully" });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [Authorize]
    [HttpPost("store-counter")]
    public async Task<IActionResult> StoreCounter([FromBody] StoreCounterRequest request)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            if (string.IsNullOrEmpty(request.InstallationId))
                return BadRequest(new { success = false, message = "Installation ID is required" });

            var device = await _context.Devices
                .FirstOrDefaultAsync(d => d.InstallationId == request.InstallationId);
            if (device == null)
                return BadRequest(new { success = false, message = "Device not found for this installation ID" });

            if (!User.IsInRole("Admin") && device.UserId != currentUserId)
                return Forbid();

            var cacheKey = $"otp_counter_{request.InstallationId}";
            var cachedCounter = _cache.Get<long?>(cacheKey);
            if (cachedCounter.HasValue && request.Counter <= cachedCounter.Value)
                return BadRequest(new { success = false, message = "Counter must be greater than the last stored value" });

            _cache.Set(cacheKey, request.Counter, TimeSpan.FromMinutes(5));
            _logger.LogInformation($" Counter stored: InstallationId={request.InstallationId}, Counter={request.Counter}, CacheKey={cacheKey}");

            return Ok(new { success = true, message = "Counter stored successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing counter");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/regenerate-activation")]
    public async Task<IActionResult> RegenerateActivationCode(int id)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var device = await _deviceService.GetDeviceByIdAsync(id);
            if (device == null)
                return NotFound(new { success = false, message = "Device not found" });

            if (device.Status != "PENDING")
                return BadRequest(new { success = false, message = "Activation code can only be regenerated for devices in PENDING status." });

            var newActivationCode = GenerateActivationCode();
            device.ActivationCode = newActivationCode;
            device.ActivationCodeExpiry = DateTime.UtcNow.AddMinutes(3);
            device.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Activation code regenerated successfully.",
                activationCode = newActivationCode
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error regenerating activation code for device {DeviceId}", id);
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== HELPERS ====================

    private string GenerateChallenge()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string GenerateDeviceToken()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 32);
    }

    private string GenerateActivationCode()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        var value = BitConverter.ToUInt32(bytes, 0) % 900000 + 100000;
        return value.ToString("D6");
    }
}