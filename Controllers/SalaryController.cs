// Controllers/SalaryController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalaryController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<SalaryController> _logger;
    private readonly IMemoryCache _cache;

    public SalaryController(AppDbContext context, ILogger<SalaryController> logger, IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            throw new UnauthorizedAccessException("User ID not found in token.");
        return userId;
    }

    // ==================== PAY SALARY ====================

    [HttpPost("pay")]
    public async Task<IActionResult> PaySalary([FromBody] SalaryPayRequest request)
    {
        try
        {
            // 1. Validate request
            if (request.EmployeeId <= 0 || request.Amount <= 0 || string.IsNullOrEmpty(request.Username))
                return BadRequest(new { success = false, message = "Invalid request" });

            if (string.IsNullOrEmpty(request.PaymentMonth) || !Regex.IsMatch(request.PaymentMonth, @"^\d{4}-\d{2}$"))
                return BadRequest(new { success = false, message = "Invalid payment month format (YYYY-MM)" });

            // 2. Check duplicate payment
            var existing = await _context.SalaryPayments
                .FirstOrDefaultAsync(sp => sp.EmployeeId == request.EmployeeId
                    && sp.PaymentMonth == request.PaymentMonth
                    && sp.Status == "APPROVED");

            if (existing != null)
                return BadRequest(new { success = false, message = $"Salary for {request.PaymentMonth} has already been paid to this employee." });

            // 3. Find Finance Manager
            var financeManager = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (financeManager == null)
                return BadRequest(new { success = false, message = "User not found" });

            // 4. Get active devices
            var devices = await _context.Devices
                .Where(d => d.UserId == financeManager.Id && d.Status == "ACTIVE")
                .ToListAsync();

            if (devices.Count == 0)
                return BadRequest(new { success = false, message = "No active device found for this user" });

            // 5. OTP validation using cached counter
            bool otpValid = false;
            long matchedCounter = 0;
            string matchedInstallationId = "";

            foreach (var device in devices)
            {
                if (string.IsNullOrEmpty(device.SecretKey))
                {
                    _logger.LogWarning($"Device {device.Id} has no SecretKey");
                    continue;
                }

                var cacheKeyCounter = $"otp_counter_{device.InstallationId}";
                var cachedCounter = _cache.Get<long?>(cacheKeyCounter);

                _logger.LogInformation($"🔍 Checking cache for key: {cacheKeyCounter}, found: {cachedCounter.HasValue}");

                if (!cachedCounter.HasValue)
                {
                    _logger.LogWarning($"No cached counter found for device {device.Id} (InstallationId: {device.InstallationId})");
                    continue;
                }

                long counterUsed = cachedCounter.Value;
                string generatedOtp = GenerateOTPFromCounter(device.SecretKey, counterUsed);

                if (generatedOtp == request.Otp)
                {
                    otpValid = true;
                    matchedCounter = counterUsed;
                    matchedInstallationId = device.InstallationId;
                    _logger.LogInformation($"✅ OTP validated with counter {counterUsed} for device {device.Id}");
                    break;
                }
                else
                {
                    _logger.LogWarning($"❌ OTP mismatch for device {device.Id}: expected {generatedOtp}, got {request.Otp}");
                }
            }

            if (!otpValid)
                return BadRequest(new { success = false, message = "Invalid OTP. Please generate a new token using the Soft Token app." });

            // 6. Replay protection: mark the counter as used
            var usedKey = $"otp_used_{matchedInstallationId}_{matchedCounter}";
            if (_cache.TryGetValue(usedKey, out _))
            {
                _logger.LogWarning($"OTP reuse attempt for installation {matchedInstallationId}, counter {matchedCounter}");
                return BadRequest(new { success = false, message = "OTP is already used. Please generate a new token." });
            }
            // ✅ Also increase replay TTL to 5 minutes
            _cache.Set(usedKey, true, TimeSpan.FromMinutes(5));

            // 7. Create payment record
            var salaryPayment = new SalaryPayment
            {
                EmployeeId = request.EmployeeId,
                Amount = request.Amount,
                PaymentMonth = request.PaymentMonth,
                Status = "APPROVED",
                ApprovedAt = DateTime.UtcNow,
                OTP = request.Otp
            };

            _context.SalaryPayments.Add(salaryPayment);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Salary paid successfully", salaryId = salaryPayment.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error paying salary");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET PAID MONTHS ====================

    [HttpGet("paid-months/{employeeId}")]
    public async Task<IActionResult> GetPaidMonths(int employeeId)
    {
        try
        {
            var paidMonths = await _context.SalaryPayments
                .Where(sp => sp.EmployeeId == employeeId && sp.Status == "APPROVED")
                .Select(sp => sp.PaymentMonth)
                .Distinct()
                .ToListAsync();

            return Ok(new { success = true, data = paidMonths });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paid months");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET SALARY PAYMENTS ====================

    [HttpGet("employee/{employeeId}")]
    public async Task<IActionResult> GetSalaryPaymentsForEmployee(int employeeId)
    {
        try
        {
            var payments = await _context.SalaryPayments
                .Where(sp => sp.EmployeeId == employeeId)
                .OrderByDescending(sp => sp.CreatedAt)
                .ToListAsync();

            return Ok(new { success = true, data = payments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting salary payments");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllSalaryPayments()
    {
        try
        {
            var payments = await _context.SalaryPayments
                .Include(sp => sp.Employee)
                .OrderByDescending(sp => sp.CreatedAt)
                .ToListAsync();

            return Ok(new { success = true, data = payments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all salary payments");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== HELPER ====================

    private string GenerateOTPFromCounter(string secretKey, long counter)
    {
        var combined = $"{secretKey}:{counter}";
        var combinedBytes = Encoding.UTF8.GetBytes(combined);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(combinedBytes);
        var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();

        var tokenValue = "";
        foreach (char c in hashString)
        {
            if (char.IsDigit(c) && tokenValue.Length < 6)
                tokenValue += c;
        }
        while (tokenValue.Length < 6)
            tokenValue = "0" + tokenValue;

        return tokenValue.Substring(0, 6);
    }
}