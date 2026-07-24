// DTOs/ChallengeRequest.cs
using System.Text.Json.Serialization;

namespace PayrollSystem.API.DTOs;

public class ChallengeRequest
{
    [JsonPropertyName("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [JsonPropertyName("actionId")]
    public int ActionId { get; set; }

    [JsonPropertyName("employeeId")]
    public int EmployeeId { get; set; } // NEW: employee ID to identify device
}