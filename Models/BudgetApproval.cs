// Models/BudgetApproval.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class BudgetApproval
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Department { get; set; } = string.Empty;
    
    [Required]
    public decimal Amount { get; set; }
    
    [Required]
    public string Description { get; set; } = string.Empty;
    
    [MaxLength(20)]
    public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED
    
    [MaxLength(10)]
    public string? OTP { get; set; }
    
    public DateTime? OTPExpiresAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation property
    public User User { get; set; } = null!;
}