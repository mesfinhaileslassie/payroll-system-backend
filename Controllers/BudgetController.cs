// Controllers/BudgetController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;
using PayrollSystem.API.Services;
using System.Security.Cryptography;
using System.Text;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BudgetController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<BudgetController> _logger;
    private readonly IDeviceService _deviceService;

    public BudgetController(AppDbContext context, ILogger<BudgetController> logger, IDeviceService deviceService)
    {
        _context = context;
        _logger = logger;
        _deviceService = deviceService;
    }

    // ==================== SUBMIT BUDGET ====================

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitBudget([FromBody] BudgetApprovalRequest request)
    {
        try
        {
            if (request.Amount <= 0 || string.IsNullOrEmpty(request.Department))
                return BadRequest(new { success = false, message = "Valid amount and department are required" });

            var user = await _context.Users.FindAsync(request.UserId);
            if (user == null)
                return BadRequest(new { success = false, message = "User not found" });

            var budgetApproval = new BudgetApproval
            {
                UserId = request.UserId,
                Department = request.Department,
                Amount = request.Amount,
                Description = request.Description,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };

            _context.BudgetApprovals.Add(budgetApproval);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Budget submitted for approval",
                approvalId = budgetApproval.Id,
                status = budgetApproval.Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting budget");
            return StatusCode(500, new { success = false, message = $"Server error: {ex.Message}" });
        }
    }

    // ==================== APPROVE WITH OTP ====================

    [HttpPost("{id}/approve-with-otp")]
    public async Task<IActionResult> ApproveBudgetWithOtp(int id, [FromBody] ApproveWithOtpRequest request)
    {
        try
        {
            // 1. Find employee by username only
            var employee = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);
            if (employee == null)
                return BadRequest(new { success = false, message = "Employee not found" });

            // 2. Find active devices for this employee (only ACTIVE status)
            var devices = await _context.Devices
                .Where(d => d.UserId == employee.Id && d.Status == "ACTIVE")
                .ToListAsync();

            if (devices.Count == 0)
                return BadRequest(new { success = false, message = "No active device found for this employee" });

            // 3. Find the budget
            var budget = await _context.BudgetApprovals.FindAsync(id);
            if (budget == null)
                return NotFound(new { success = false, message = "Budget not found" });

            if (budget.Status != "PENDING")
                return BadRequest(new { success = false, message = $"Budget is already {budget.Status}" });

            // 4. Validate OTP against all active devices
            bool otpValid = false;
            Device? matchingDevice = null;

            foreach (var device in devices)
            {
                if (string.IsNullOrEmpty(device.SecretKey))
                    continue;

                _logger.LogInformation($"🔍 Checking device {device.Id}, SecretKey: {device.SecretKey}");
                _logger.LogInformation($"📥 Client OTP: {request.Otp}");

                var serverOtp = GenerateTOTP(device.SecretKey, 0);
                var prevOtp = GenerateTOTP(device.SecretKey, -1);
                var nextOtp = GenerateTOTP(device.SecretKey, 1);

                _logger.LogInformation($"🔑 Device {device.Id}: Current OTP={serverOtp}, Prev={prevOtp}, Next={nextOtp}");

                if (serverOtp == request.Otp || prevOtp == request.Otp || nextOtp == request.Otp)
                {
                    otpValid = true;
                    matchingDevice = device;
                    break;
                }
            }

            if (!otpValid)
            {
                _logger.LogWarning($"OTP validation failed for employee {employee.Id} ({employee.Username})");
                return BadRequest(new { success = false, message = "Invalid OTP" });
            }

            // 5. Approve the budget
            budget.Status = "APPROVED";
            budget.ApprovedAt = DateTime.UtcNow;
            budget.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // 6. Update device last used
            if (matchingDevice != null)
            {
                matchingDevice.LastUsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                success = true,
                message = "Budget approved successfully",
                status = budget.Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving budget with OTP");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== REJECT BUDGET ====================

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectBudget(int id)
    {
        try
        {
            var budget = await _context.BudgetApprovals.FindAsync(id);
            if (budget == null)
                return NotFound(new { success = false, message = "Budget not found" });

            if (budget.Status != "PENDING")
                return BadRequest(new { success = false, message = $"Budget is already {budget.Status}" });

            budget.Status = "REJECTED";
            budget.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Budget rejected",
                status = budget.Status
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting budget");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET USER BUDGETS ====================

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserBudgets(int userId)
    {
        try
        {
            var budgets = await _context.BudgetApprovals
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new BudgetDto
                {
                    Id = b.Id,
                    Department = b.Department,
                    Amount = b.Amount,
                    Description = b.Description,
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    ApprovedAt = b.ApprovedAt
                })
                .ToListAsync();

            return Ok(new { success = true, data = budgets });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user budgets");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ==================== GET ALL BUDGETS ====================

    [HttpGet("all")]
    public async Task<IActionResult> GetAllBudgets()
    {
        try
        {
            var budgets = await _context.BudgetApprovals
                .AsNoTracking()
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new BudgetDto
                {
                    Id = b.Id,
                    Department = b.Department,
                    Amount = b.Amount,
                    Description = b.Description,
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    ApprovedAt = b.ApprovedAt
                })
                .ToListAsync();

            return Ok(new { success = true, data = budgets });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all budgets");
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