// Models/SalaryPayment.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class SalaryPayment
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int EmployeeId { get; set; }  // User.Id of the employee being paid

    [Required]
    public decimal Amount { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedAt { get; set; }

    [MaxLength(10)]
    public string? OTP { get; set; }

    // Navigation property
    public User Employee { get; set; } = null!;
}