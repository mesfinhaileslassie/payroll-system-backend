// Models/DeviceCode.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class DeviceCode
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int DeviceId { get; set; }
    
    [Required]
    public string Code { get; set; } = string.Empty;
    
    [MaxLength(20)]
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, USED, EXPIRED
    
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation property
    public Device Device { get; set; } = null!;
}