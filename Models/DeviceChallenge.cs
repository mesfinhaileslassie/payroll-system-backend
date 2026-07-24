// Models/DeviceChallenge.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class DeviceChallenge
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Challenge { get; set; } = string.Empty;

    [Required]
    public string ActionType { get; set; } = string.Empty; // e.g., "BudgetApproval"

    public int ActionId { get; set; }

    public DateTime Expiry { get; set; }

    public string Status { get; set; } = "PENDING"; // PENDING, COMPLETED, EXPIRED

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public int? DeviceId { get; set; }

    public Device? Device { get; set; }
}