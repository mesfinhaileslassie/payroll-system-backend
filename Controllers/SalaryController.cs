using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Helpers;
using PayrollSystem.API.Models;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.RateLimiting;
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

    [HttpPost("pay")]
    [Authorize(Roles = "FinanceManager")]
    [EnableRateLimiting("OtpValidationPolicy")]
    public async Task<IActionResult> PaySalary([FromBody] SalaryPayRequest request)
    {
        try
        {
            // 1. Validate request
            if (request.EmployeeId <= 0 || request.Amount <= 0)
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

            // 3. Get authenticated Finance Manager from JWT
            int currentUserId = GetCurrentUserId();
            var financeManager = await _context.Users.FindAsync(currentUserId);
            if (financeManager == null)
                return Unauthorized(new { success = false, message = "Authenticated user not found." });

            // 4. Get active devices
            var devices = await _context.Devices
                .Where(d => d.UserId == financeManager.Id && d.Status == "ACTIVE")
                .ToListAsync();
            if (devices.Count == 0)
                return BadRequest(new { success = false, message = "No active device found for this user" });

            // 5. TOTP validation
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

                if (!TotpHelper.IsBase32(device.SecretKey))
                {
                    _logger.LogWarning($"Device {device.Id} has invalid secret format (not Base32). Skipping.");
                    continue;
                }

                if (TotpHelper.ValidateTotp(device.SecretKey, request.Otp, out long timeStep))
                {
                    otpValid = true;
                    matchedCounter = timeStep;
                    matchedInstallationId = device.InstallationId;
                    _logger.LogInformation($"TOTP validated for device {device.Id}");
                    break;
                }
            }

            if (!otpValid)
                return BadRequest(new { success = false, message = "Invalid OTP. Please generate a new token using the Soft Token app." });

            // 6. Replay protection
            var usedKey = $"otp_used_{matchedInstallationId}_{matchedCounter}";
            if (_cache.TryGetValue(usedKey, out _))
            {
                _logger.LogWarning($"OTP reuse attempt for installation {matchedInstallationId}");
                return BadRequest(new { success = false, message = "OTP is already used. Please generate a new token." });
            }
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

            _logger.LogInformation($"Salary approved by FinanceManager {financeManager.Username} (ID: {financeManager.Id}) for employee {request.EmployeeId}");

            return Ok(new { success = true, message = "Salary paid successfully", salaryId = salaryPayment.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error paying salary");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== READ-ONLY ENDPOINTS ====================

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
}