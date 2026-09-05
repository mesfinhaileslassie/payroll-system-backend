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
using BCrypt.Net;
using Microsoft.AspNetCore.RateLimiting;
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

    // ==================== PASSWORD HELPER METHODS ====================

    private static bool IsBcryptHash(string hash)
    {
        return !string.IsNullOrEmpty(hash) &&
               (hash.StartsWith("$2a$") || hash.StartsWith("$2b$") ||
                hash.StartsWith("$2y$") || hash.StartsWith("$2x$"));
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }


    private async Task<bool> VerifyAndMigratePasswordAsync(User user, string providedPassword, bool saveChanges = true)
    {
        if (IsBcryptHash(user.PasswordHash))
        {
            return VerifyPassword(providedPassword, user.PasswordHash);
        }

        // Legacy plaintext
        if (user.PasswordHash == providedPassword)
        {
            user.PasswordHash = HashPassword(providedPassword);
            if (saveChanges)
            {
                await _context.SaveChangesAsync();
            }
            return true;
        }

        return false;
    }

    // ==================== LOGIN ====================

    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
                return Unauthorized(new { success = false, message = "Invalid username or password" });

            // Verify and migrate if needed
            bool passwordValid = await VerifyAndMigratePasswordAsync(user, request.Password, saveChanges: true);

            if (!passwordValid)
                return Unauthorized(new { success = false, message = "Invalid username or password" });

            if (!user.IsActive)
                return Unauthorized(new { success = false, message = "Account is inactive" });

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
            if (string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Username, email, and password are required." });
            }

            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest(new { success = false, message = "Username already exists." });

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                return BadRequest(new { success = false, message = "Email already exists." });

            string role = "Employee";
            bool requiresDevice = false;

            if (request.Position == "Finance Manager")
            {
                role = "FinanceManager";
                requiresDevice = true;
            }

            if (requiresDevice && string.IsNullOrWhiteSpace(request.DeviceCode))
            {
                return BadRequest(new { success = false, message = "Device code is required for Finance Manager role." });
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password), // ✅ Hashed
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
                        _context.Users.Remove(user);
                        await _context.SaveChangesAsync();
                        return BadRequest(new { success = false, message = deviceResult.Message });
                    }

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
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();
                    _logger.LogError(ex, "Device registration failed for user {Username}", request.Username);
                    return StatusCode(500, new { success = false, message = $"Device registration failed: {ex.Message}" });
                }
            }

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

    // ==================== CHANGE PASSWORD ====================

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            if (request.UserId <= 0)
                return BadRequest(new { success = false, message = "Invalid user ID." });

            if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { success = false, message = "Current password and new password are required." });
            }

            if (request.NewPassword.Length < 6)
                return BadRequest(new { success = false, message = "New password must be at least 6 characters." });

            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
                return NotFound(new { success = false, message = "User not found." });

            // Verify current password (handles both legacy and hashed, migrates if needed)
            bool currentPasswordValid = await VerifyAndMigratePasswordAsync(user, request.CurrentPassword, saveChanges: true);
            if (!currentPasswordValid)
                return BadRequest(new { success = false, message = "Current password is incorrect." });

            // Hash and store the new password
            user.PasswordHash = HashPassword(request.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Password changed successfully."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user {UserId}", request.UserId);
            return StatusCode(500, new { success = false, message = "Internal server error." });
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