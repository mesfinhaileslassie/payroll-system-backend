// Controllers/DeviceController.cs
using Microsoft.AspNetCore.Mvc;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Services;
using PayrollSystem.API.Data;
using Microsoft.EntityFrameworkCore;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DeviceController> _logger;
    private readonly AppDbContext _context;

    public DeviceController(IDeviceService deviceService, ILogger<DeviceController> logger, AppDbContext context)
    {
        _deviceService = deviceService;
        _logger = logger;
        _context = context;
    }

    // ==================== REGISTRATION ====================

    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceCode))
                return BadRequest(new { success = false, message = "Device code is required" });
            if (string.IsNullOrEmpty(request.EmployeeUsername))
                return BadRequest(new { success = false, message = "Employee username is required" });

            var result = await _deviceService.RegisterDeviceAsync(request);
            if (result.Success)
                return Ok(result);
            else
                return BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET DEVICE ====================

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDevice(int id)
    {
        try
        {
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
                    secretKey = device.SecretKey,
                    deviceToken = device.DeviceToken,
                    installationId = device.InstallationId,
                    brand = device.Brand,
                    manufacturer = device.Manufacturer,
                    publicKey = device.PublicKey,
                    userId = device.UserId
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET ALL DEVICES ====================

    [HttpGet("all")]
    public async Task<IActionResult> GetAllDevices()
    {
        try
        {
            var devices = await _context.Devices
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all devices");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== CHECK DEVICE REGISTRATION ====================

    [HttpGet("check-registration")]
    public async Task<IActionResult> CheckDeviceRegistration([FromQuery] string installationId)
    {
        if (string.IsNullOrEmpty(installationId))
            return BadRequest(new { success = false, message = "Installation ID is required" });

        var device = await _deviceService.GetDeviceByInstallationIdAsync(installationId);
        if (device == null)
            return Ok(new { registered = false, status = (string?)null, deviceId = (int?)null });

        return Ok(new { registered = true, status = device.Status, deviceId = device.Id });
    }

    // ==================== GET DEVICE BY INSTALLATION ID ====================

    [HttpGet("by-installation/{installationId}")]
    public async Task<IActionResult> GetDeviceByInstallationId(string installationId)
    {
        if (string.IsNullOrEmpty(installationId))
            return BadRequest(new { success = false, message = "Installation ID is required" });

        var device = await _deviceService.GetDeviceByInstallationIdAsync(installationId);
        if (device == null)
            return NotFound(new { success = false, message = "Device not found" });

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

    // ==================== ACTIVATION CODE LOOKUP ====================

    [HttpGet("get-device-id/{activationCode}")]
    public async Task<IActionResult> GetDeviceIdByActivationCode(string activationCode)
    {
        try
        {
            if (string.IsNullOrEmpty(activationCode) || activationCode.Length != 6)
                return BadRequest(new { success = false, message = "Invalid activation code format" });

            var device = await _deviceService.GetDeviceByActivationCodeAsync(activationCode);
            if (device == null)
                return NotFound(new { success = false, message = "Invalid activation code" });

            return Ok(new
            {
                success = true,
                deviceId = device.Id,
                status = device.Status,
                message = "Device found"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting device by activation code: {activationCode}");
            return StatusCode(500, new { success = false, message = $"Server error: {ex.Message}" });
        }
    }

    // ==================== ACTIVATE DEVICE (step 1) ====================

    [HttpPost("activate")]
    public async Task<IActionResult> ActivateDevice([FromBody] ActivationRequest request)
    {
        try
        {
            if (request.DeviceId <= 0 || string.IsNullOrEmpty(request.ActivationCode))
                return BadRequest(new { success = false, message = "Device ID and activation code are required" });

            var result = await _deviceService.ActivateDeviceAsync(request);
            if (!result.Success)
                return BadRequest(result);

            // Generate challenge for activation verification
            var challenge = GenerateChallenge();
            await _deviceService.StoreChallengeAsync(request.DeviceId, challenge);

            return Ok(new
            {
                success = true,
                message = "Activation verified. Please complete device verification.",
                data = result,
                challenge = challenge,
                expiresIn = 60
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET CHALLENGE (for activation) ====================

    [HttpGet("{id}/challenge")]
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

    // ==================== VERIFY SIGNATURE (activation) ====================

    [HttpPost("verify-signature")]
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

            var isValid = _deviceService.VerifySignature(storedChallenge, request.Signature, device.PublicKey);
            if (!isValid)
                return BadRequest(new { success = false, message = "Signature verification failed" });

            // Activate device
            await _deviceService.UpdateDeviceStatusAsync(request.DeviceId, "ACTIVE");
            await _deviceService.MarkDeviceAsTrustedAsync(request.DeviceId);

            // Generate new credentials (kept for compatibility)
            var deviceToken = GenerateDeviceToken();
            var secretKey = GenerateSecretKey();
            await _deviceService.UpdateDeviceCredentialsAsync(request.DeviceId, deviceToken, secretKey);

            await _deviceService.ClearChallengeAsync(request.DeviceId);

            return Ok(new
            {
                success = true,
                message = "Device verified and activated successfully",
                deviceToken = deviceToken,
                secretKey = secretKey,
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

    // ==================== DELETE DEVICE ====================

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDevice(int id)
    {
        try
        {
            var result = await _deviceService.DeleteDeviceAsync(id);
            if (!result)
                return NotFound(new { success = false, message = "Device not found" });

            return Ok(new { success = true, message = "Device deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting device");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== TEST ====================

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { message = "API is working!", timestamp = DateTime.Now, status = "online" });
    }



    [HttpPut("{id}")]
public async Task<IActionResult> UpdateDevice(int id, [FromBody] UpdateDeviceRequest request)
{
    try
    {
        if (id <= 0 || string.IsNullOrEmpty(request.DeviceName))
            return BadRequest(new { success = false, message = "Invalid request" });

        var device = await _deviceService.GetDeviceByIdAsync(id);
        if (device == null)
            return NotFound(new { success = false, message = "Device not found" });

        // Update allowed fields
        device.DeviceName = request.DeviceName;

        // Only allow valid status values
        if (!string.IsNullOrEmpty(request.Status) && (request.Status == "ACTIVE" || request.Status == "INACTIVE" || request.Status == "PENDING"))
        {
            device.Status = request.Status;
        }

        device.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Device updated successfully" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating device");
        return StatusCode(500, new { success = false, message = "Internal server error" });
    }
}

    // ==================== HELPERS ====================

    private string GenerateChallenge()
    {
        var random = new Random();
        var bytes = new byte[32];
        random.NextBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string GenerateDeviceToken()
    {
        return Guid.NewGuid().ToString().Replace("-", "").Substring(0, 32);
    }

    private string GenerateSecretKey()
    {
        return Guid.NewGuid().ToString().Replace("-", "").Substring(0, 32);
    }
}