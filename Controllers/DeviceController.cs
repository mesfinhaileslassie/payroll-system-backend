// Controllers/DeviceController.cs
using Microsoft.AspNetCore.Mvc;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Services;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DeviceController> _logger;

    public DeviceController(IDeviceService deviceService, ILogger<DeviceController> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    // ==================== REGISTRATION ====================

    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceCode))
            {
                return BadRequest(new { success = false, message = "Device code is required" });
            }
            if (string.IsNullOrEmpty(request.EmployeeUsername))
            {
                return BadRequest(new { success = false, message = "Employee username is required" });
            }

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

    // ==================== OTP VERIFICATION (DEPRECATED) ====================

    [HttpPost("verify-otp")]
    public IActionResult VerifyOTP()
    {
        return BadRequest(new
        {
            success = false,
            message = "OTP verification is deprecated. Please use the challenge-response flow.",
            valid = false
        });
    }

    // ==================== CHALLENGE-RESPONSE FOR ACTIONS ====================

    [HttpPost("challenge")]
    public async Task<IActionResult> CreateChallenge([FromBody] ChallengeRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.ActionType) || request.ActionId <= 0 || request.EmployeeId <= 0)
                return BadRequest(new { success = false, message = "ActionType, ActionId, and EmployeeId are required" });

            var result = await _deviceService.CreateChallengeAsync(request.ActionType, request.ActionId, request.EmployeeId);
            if (string.IsNullOrEmpty(result.Challenge))
                return BadRequest(new { success = false, message = "Failed to create challenge" });

            return Ok(new
            {
                success = true,
                challenge = result.Challenge,
                expiresIn = result.ExpiresIn
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating challenge");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [HttpPost("verify-challenge")]
    public async Task<IActionResult> VerifyChallenge([FromBody] VerifyChallengeRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.InstallationId) ||
                string.IsNullOrEmpty(request.Signature) ||
                string.IsNullOrEmpty(request.Challenge))
            {
                return BadRequest(new { success = false, message = "InstallationId, signature, and challenge are required" });
            }

            var isValid = await _deviceService.VerifyChallengeAsync(
                request.InstallationId,
                request.Signature,
                request.Challenge
            );

            if (isValid)
                return Ok(new { success = true, message = "Challenge verified and action approved successfully" });
            else
                return BadRequest(new { success = false, message = "Challenge verification failed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying challenge");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== TEST ====================

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { message = "API is working!", timestamp = DateTime.Now, status = "online" });
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