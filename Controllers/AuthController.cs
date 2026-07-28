// Controllers/AuthController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using PayrollSystem.API.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuthController> _logger;
    private readonly IDeviceService _deviceService;
    private readonly IConfiguration _configuration;

    public AuthController(
        AppDbContext context,
        ILogger<AuthController> logger,
        IDeviceService deviceService,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _deviceService = deviceService;
        _configuration = configuration;
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

            // In production, use BCrypt to verify password
            if (user.PasswordHash != request.Password)
                return Unauthorized(new { success = false, message = "Invalid username or password" });

            if (!user.IsActive)
                return Unauthorized(new { success = false, message = "Account is inactive" });

            // ✅ Generate JWT token
            var token = GenerateJwtToken(user);

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
            // 1. Validate input
            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Username, email, and password are required." });
            }

            // 2. Check if user exists
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest(new { success = false, message = "Username already exists." });
            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest(new { success = false, message = "Email already exists." });

            // 3. Determine role
            string role = "Employee";
            bool requiresDevice = false;
            if (request.Position == "Payroll Officer")
            {
                role = "PayrollOfficer";
                requiresDevice = true;
            }
            else if (request.Position == "Finance Manager")
            {
                role = "FinanceManager";
                requiresDevice = true;
            }

            // 4. If role requires device, ensure device code is provided
            if (requiresDevice && string.IsNullOrWhiteSpace(request.DeviceCode))
            {
                return BadRequest(new { success = false, message = "Device code is required for Payroll Officer and Finance Manager roles." });
            }

            // 5. Create user
            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = request.Password, // In production, hash this
                FirstName = request.FirstName ?? "",
                LastName = request.LastName ?? "",
                Phone = request.Phone ?? "",
                Gender = request.Gender ?? "",
                Position = request.Position,
                Role = role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // 6. Register device (only if required)
            if (requiresDevice)
            {
                try
                {
                    var deviceResult = await _deviceService.RegisterDeviceAsync(
                        new DeviceRegistrationRequest
                        {
                            DeviceCode = request.DeviceCode,
                            DeviceName = request.DeviceName,
                            EmployeeUsername = request.Username
                        }
                    );

                    if (!deviceResult.Success)
                    {
                        // Rollback user creation
                        _context.Users.Remove(user);
                        await _context.SaveChangesAsync();
                        return BadRequest(new { success = false, message = deviceResult.Message });
                    }

                    // Return success with activation code
                    return Ok(new
                    {
                        success = true,
                        message = "Employee registered with device.",
                        userId = user.Id,
                        deviceId = deviceResult.DeviceId,
                        activationCode = deviceResult.ActivationCode
                    });
                }
                catch (Exception ex)
                {
                    // Rollback user creation
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();
                    _logger.LogError(ex, "Device registration failed for user {Username}", request.Username);
                    return StatusCode(500, new { success = false, message = $"Device registration failed: {ex.Message}" });
                }
            }

            // No device registration – return success without activation code
            return Ok(new
            {
                success = true,
                message = "Employee registered successfully (no device required).",
                userId = user.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee registration failed");
            return StatusCode(500, new { success = false, message = $"Registration failed: {ex.Message}" });
        }
    }

    // ==================== JWT TOKEN GENERATION ====================

    private string GenerateJwtToken(User user)
    {
        var secretKey = _configuration["JwtSettings:SecretKey"]
            ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
        var key = Encoding.UTF8.GetBytes(secretKey);
        var signingKey = new SymmetricSecurityKey(key);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role ?? "Employee"),
            new Claim("UserId", user.Id.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["JwtSettings:Issuer"] ?? "your-issuer",
            audience: _configuration["JwtSettings:Audience"] ?? "your-audience",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateActivationCode()
    {
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[4];
        rng.GetBytes(bytes);
        var value = BitConverter.ToUInt32(bytes, 0) % 900000 + 100000;
        return value.ToString("D6");
    }
}