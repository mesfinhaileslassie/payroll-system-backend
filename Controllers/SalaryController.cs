// Controllers/SalaryController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Requires authentication
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

            // 3. Find Finance Manager (the acting user)
            var financeManager = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);
            if (financeManager == null)
                return BadRequest(new { success = false, message = "User not found" });

            // 4. Get active devices for the Finance Manager
            var devices = await _context.Devices
                .Where(d => d.UserId == financeManager.Id && d.Status == "ACTIVE")
                .ToListAsync();
            if (devices.Count == 0)
                return BadRequest(new { success = false, message = "No active device found for this user" });

            // 5. OTP validation with fixed order: check match, then replay, with fallback
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

                // Try cached counter first
                var cacheKey = $"otp_counter_{device.InstallationId}";
                var cachedCounter = _cache.Get<long?>(cacheKey);

                string? generatedOtp = null;
                long counterUsed = 0;
                bool matched = false;

                if (cachedCounter.HasValue)
                {
                    counterUsed = cachedCounter.Value;
                    generatedOtp = GenerateOTPFromCounter(device.SecretKey, counterUsed);
                    if (generatedOtp == request.Otp)
                    {
                        matched = true;
                        _logger.LogInformation($"✅ Using cached counter {counterUsed} for device {device.Id}");
                    }
                }

                // If cached counter didn't match, fallback to time-based
                if (!matched)
                {
                    var (current, currCounter) = GenerateTOTP(device.SecretKey, 0);
                    var (prev, prevCounter) = GenerateTOTP(device.SecretKey, -1);
                    var (next, nextCounter) = GenerateTOTP(device.SecretKey, 1);
                    _logger.LogInformation($"⚠️ Time-based fallback: current={current}, prev={prev}, next={next}");

                    if (current == request.Otp)
                    {
                        matched = true;
                        counterUsed = currCounter;
                    }
                    else if (prev == request.Otp)
                    {
                        matched = true;
                        counterUsed = prevCounter;
                    }
                    else if (next == request.Otp)
                    {
                        matched = true;
                        counterUsed = nextCounter;
                    }
                }

                if (!matched)
                    continue; // try next device

                // Only now check replay protection
                var usedKey = $"otp_used_{device.InstallationId}_{counterUsed}";
                if (_cache.TryGetValue(usedKey, out _))
                {
                    _logger.LogWarning($"OTP reuse attempt for device {device.Id}, counter {counterUsed}");
                    // Return error for the whole request – the user must generate a new OTP
                    return BadRequest(new { success = false, message = "OTP is already used. Please generate a new token." });
                }

                // Valid OTP found
                otpValid = true;
                matchedCounter = counterUsed;
                matchedInstallationId = device.InstallationId;
                break;
            }

            if (!otpValid)
                return BadRequest(new { success = false, message = "Invalid OTP. Please generate a new token." });

            // 6. Mark counter as used
            var usedKeyFinal = $"otp_used_{matchedInstallationId}_{matchedCounter}";
            _cache.Set(usedKeyFinal, true, TimeSpan.FromSeconds(60));

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

    // GET endpoints – you may want to also protect them
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

    // ==================== HELPERS ====================

    private (string otp, long counter) GenerateTOTP(string secretKey, int offset = 0)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var counter = (timestamp / 30) + offset;
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
        return (tokenValue.Substring(0, 6), counter);
    }

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