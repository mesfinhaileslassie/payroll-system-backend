// Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using System.Text;
using System.Text.Json;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext context, ILogger<AuthController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);
            if (user == null)
                return Unauthorized(new { success = false, message = "Invalid username or password" });

            if (user.PasswordHash != request.Password)
                return Unauthorized(new { success = false, message = "Invalid username or password" });

            if (!user.IsActive)
                return Unauthorized(new { success = false, message = "Account is inactive" });

            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "Login successful",
                Token = token,
                Username = user.Username,
                UserId = user.Id,
                Role = user.Role,
                IsActive = user.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== ADMIN: REGISTER EMPLOYEE ====================

    [HttpPost("register-employee")]
    public async Task<IActionResult> RegisterEmployee([FromBody] RegisterEmployeeRequest request)
    {
        try
        {
            // 1. Check username and email
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest(new { success = false, message = "Username already exists" });

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest(new { success = false, message = "Email already exists" });

            // 2. Validate and check device duplication BEFORE creating user
            if (!string.IsNullOrEmpty(request.DeviceCode))
            {
                var deviceCodeData = JsonSerializer.Deserialize<Dictionary<string, string>>(request.DeviceCode);
                if (deviceCodeData == null)
                    return BadRequest(new { success = false, message = "Invalid device code format" });

                var androidId = deviceCodeData.GetValueOrDefault("android_id");
                var installationId = deviceCodeData.GetValueOrDefault("installation_id");
                var publicKey = deviceCodeData.GetValueOrDefault("public_key");

                if (string.IsNullOrEmpty(androidId) || string.IsNullOrEmpty(installationId))
                    return BadRequest(new { success = false, message = "Missing required device information" });

                // Check duplicates
                if (await _context.Devices.AnyAsync(d => d.AndroidId == androidId))
                    return BadRequest(new { success = false, message = "This device (Android ID) is already registered." });

                if (await _context.Devices.AnyAsync(d => d.InstallationId == installationId))
                    return BadRequest(new { success = false, message = "This installation ID is already registered." });

                if (await _context.Devices.AnyAsync(d => d.PublicKey == publicKey))
                    return BadRequest(new { success = false, message = "Public key already exists." });
            }

            // 3. Determine role
            string role = "Employee";
            if (request.Position == "Payroll Officer")
                role = "PayrollOfficer";
            else if (request.Position == "Finance Manager")
                role = "FinanceManager";

            // 4. Create user
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = request.Password,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Phone = request.Phone,
                Gender = request.Gender,
                Position = request.Position,
                Role = role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // 5. Register device (if provided)
            if (!string.IsNullOrEmpty(request.DeviceCode))
            {
                try
                {
                    var deviceCodeData = JsonSerializer.Deserialize<Dictionary<string, string>>(request.DeviceCode);
                    if (deviceCodeData != null)
                    {
                        var androidId = deviceCodeData.GetValueOrDefault("android_id");
                        var installationId = deviceCodeData.GetValueOrDefault("installation_id");
                        var publicKey = deviceCodeData.GetValueOrDefault("public_key");
                        var deviceModel = deviceCodeData.GetValueOrDefault("device_model");
                        var brand = deviceCodeData.GetValueOrDefault("brand");
                        var manufacturer = deviceCodeData.GetValueOrDefault("manufacturer");
                        var serialNumber = deviceCodeData.GetValueOrDefault("serial_number");

                        var device = new Device
                        {
                            UserId = user.Id,
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

                        var activationCode = GenerateActivationCode();
                        device.ActivationCode = activationCode;
                        device.ActivationCodeExpiry = DateTime.UtcNow.AddMinutes(3);
                        await _context.SaveChangesAsync();

                        var deviceToken = Guid.NewGuid().ToString();
                        device.DeviceToken = deviceToken;
                        await _context.SaveChangesAsync();

                        return Ok(new
                        {
                            success = true,
                            message = "Employee registered with device. Please activate the device using the activation code.",
                            userId = user.Id,
                            deviceId = device.Id,
                            activationCode = activationCode
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error registering device during employee creation");
                    // Device registration failed – rollback user creation?
                    // For consistency, we'll delete the user and return error.
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();
                    return StatusCode(500, new { success = false, message = "Failed to register device. Please try again." });
                }
            }

            return Ok(new
            {
                success = true,
                message = "Employee registered successfully (no device registered)",
                userId = user.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering employee");
            return StatusCode(500, new { success = false, message = $"Server error: {ex.Message}" });
        }
    }

    private string GenerateActivationCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }
}