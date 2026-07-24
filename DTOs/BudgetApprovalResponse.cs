// DTOs/BudgetApprovalResponse.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class BudgetApprovalResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("requiresOTP")]
    public bool RequiresOTP { get; set; }
    
    [JsonPropertyName("approvalId")]
    public int? ApprovalId { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}