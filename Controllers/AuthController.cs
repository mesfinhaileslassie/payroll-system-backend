// Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using System.Security.Cryptography;
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

            // For demo, compare plaintext; in production use BCrypt
            if (user.PasswordHash != request.Password)
                return Unauthorized(new { success = false, message = "Invalid username or password" });

            if (!user.IsActive)
                return Unauthorized(new { success = false, message = "Account is inactive" });

            // Generate a simple token (for demo, not JWT)
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
            // Check if username already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);
            if (existingUser != null)
                return BadRequest(new { success = false, message = "Username already exists" });

            // Check if email exists
            var existingEmail = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == request.Email);
            if (existingEmail != null)
                return BadRequest(new { success = false, message = "Email already exists" });

            // Create user (Employee)
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = request.Password, // In production, hash with BCrypt
                FirstName = request.FirstName,
                LastName = request.LastName,
                Phone = request.Phone,
                Gender = request.Gender,
                Department = request.Department,
                Position = request.Position,
                EmployeeId = request.EmployeeId,
                Role = "Employee",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // ==================== DEVICE REGISTRATION ====================
            // If deviceCode is provided, register the device and associate it with the employee
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

                        if (!string.IsNullOrEmpty(androidId) && !string.IsNullOrEmpty(installationId))
                        {
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

                            // Generate activation code (expires in 3 minutes)
                            var activationCode = GenerateActivationCode();
                            device.ActivationCode = activationCode;
                            device.ActivationCodeExpiry = DateTime.UtcNow.AddMinutes(3);
                            await _context.SaveChangesAsync();

                            // Generate device token (for compatibility)
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
                        else
                        {
                            _logger.LogWarning("Device registration skipped: missing required fields");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error registering device during employee creation");
                    // Continue – employee created, but device registration failed
                }
            }

            // If no device or device registration failed, return success without device
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