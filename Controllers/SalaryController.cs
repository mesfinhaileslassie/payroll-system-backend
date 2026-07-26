// Controllers/SalaryController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using System.Security.Cryptography;
using System.Text;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SalaryController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<SalaryController> _logger;

    public SalaryController(AppDbContext context, ILogger<SalaryController> logger)
    {
        _context = context;
        _logger = logger;
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

            // 2. Find the Finance Manager (the one approving) by username
            var financeManager = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);
            if (financeManager == null)
                return BadRequest(new { success = false, message = "User not found" });

            // 3. Find active devices for the Finance Manager
            var devices = await _context.Devices
                .Where(d => d.UserId == financeManager.Id && d.Status == "ACTIVE")
                .ToListAsync();
            if (devices.Count == 0)
                return BadRequest(new { success = false, message = "No active device found for this user" });

            // 4. Validate OTP against all active devices
            bool otpValid = false;
            foreach (var device in devices)
            {
                if (string.IsNullOrEmpty(device.SecretKey)) continue;

                var serverOtp = GenerateTOTP(device.SecretKey, 0);
                var prevOtp = GenerateTOTP(device.SecretKey, -1);
                var nextOtp = GenerateTOTP(device.SecretKey, 1);

                if (serverOtp == request.Otp || prevOtp == request.Otp || nextOtp == request.Otp)
                {
                    otpValid = true;
                    break;
                }
            }

            if (!otpValid)
                return BadRequest(new { success = false, message = "Invalid OTP" });

            // 5. Create the salary payment record
            var salaryPayment = new SalaryPayment
            {
                EmployeeId = request.EmployeeId,
                Amount = request.Amount,
                Status = "APPROVED",
                ApprovedAt = DateTime.UtcNow,
                OTP = request.Otp
            };

            _context.SalaryPayments.Add(salaryPayment);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Salary paid successfully",
                salaryId = salaryPayment.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error paying salary");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET SALARY PAYMENTS FOR EMPLOYEE ====================

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

    // ==================== GET ALL SALARY PAYMENTS (for admin/reporting) ====================

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

    // ==================== HELPER: TOTP GENERATION ====================

    private string GenerateTOTP(string secretKey, int offset = 0)
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
        return tokenValue.Substring(0, 6);
    }
}