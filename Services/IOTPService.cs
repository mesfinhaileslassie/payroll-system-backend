// Services/IOTPService.cs
namespace PayrollSystem.API.Services;

public interface IOTPService
{
    Task<string> GenerateOTPAsync(int userId);
    Task<bool> ValidateOTPAsync(string otp, int userId);
    Task<bool> IsOTPExpiredAsync(string otp, int userId);
    Task InvalidateOTPAsync(string otp, int userId);
}