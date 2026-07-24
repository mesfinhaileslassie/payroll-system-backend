// Models/AuditLog.cs
using System.ComponentModel.DataAnnotations;

namespace PayrollSystem.API.Models;

public class AuditLog
{
    [Key]
    public int Id { get; set; }
    
    public int? UserId { get; set; }
    
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;
    
    public string? Details { get; set; }
    
    [MaxLength(45)]
    public string? IpAddress { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}