// Controllers/UsersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PayrollSystem.API.Data;
using PayrollSystem.API.DTOs;

namespace PayrollSystem.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<UsersController> _logger;

    public UsersController(AppDbContext context, ILogger<UsersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ✅ SPECIFIC ROUTE FIRST: GET api/users/employees
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        try
        {
            var employees = await _context.Users
                .Where(u => u.Role == "Employee")
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Phone,
                    u.IsActive
                })
                .ToListAsync();

            return Ok(new { success = true, data = employees });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting employees");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }

    // ✅ GENERIC ROUTE SECOND: GET api/users/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        try
        {
            var user = await _context.Users
                .Where(u => u.Id == id)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Phone = u.Phone,
                    Gender = u.Gender,
                    Department = u.Department,
                    Position = u.Position,
                    EmployeeId = u.EmployeeId,
                    IsActive = u.IsActive,
                    Role = u.Role
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound(new { success = false, message = "User not found" });

            return Ok(new { success = true, data = user });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user");
            return StatusCode(500, new { success = false, message = "Internal server error" });
        }
    }
}