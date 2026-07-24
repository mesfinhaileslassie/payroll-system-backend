// Models/Device.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class Device
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string AndroidId { get; set; } = string.Empty;
    
    [MaxLength(100)]
    public string? DeviceModel { get; set; }
    
    [MaxLength(100)]
    public string? SerialNumber { get; set; }
    
    [Required]
    [MaxLength(36)]
    public string InstallationId { get; set; } = string.Empty;
    
    [Required]
    public string PublicKey { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string? DeviceName { get; set; }
    
    [MaxLength(50)]
    public string? Brand { get; set; }
    
    [MaxLength(50)]
    public string? Manufacturer { get; set; }
    
    [MaxLength(36)]
    public string? DeviceToken { get; set; }
    
    [MaxLength(255)]
    public string? SecretKey { get; set; }
    
    [MaxLength(20)]
    public string Status { get; set; } = "PENDING";
    
    [MaxLength(10)]
    public string? ActivationCode { get; set; }
    
    // ✅ ONLY ONE - Keep this one
    public DateTime? ActivationCodeExpiry { get; set; }
    
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation property
    public User User { get; set; } = null!;
}