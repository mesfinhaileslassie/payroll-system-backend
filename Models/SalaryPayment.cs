// Models/SalaryPayment.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class SalaryPayment
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int EmployeeId { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(7)]
    public string PaymentMonth { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Status { get; set; } = "PENDING";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedAt { get; set; }

    [MaxLength(10)]
    public string? OTP { get; set; }


    public User Employee { get; set; } = null!;
}