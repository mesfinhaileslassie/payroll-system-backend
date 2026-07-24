// Services/OTPService.cs
using Microsoft.Extensions.Caching.Memory;

namespace PayrollSystem.API.Services;

public class OTPService : IOTPService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<OTPService> _logger;
    private const int OTP_EXPIRY_SECONDS = 30;

    public OTPService(IMemoryCache cache, ILogger<OTPService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GenerateOTPAsync(int userId)
    {
        try
        {
            var random = new Random();
            var otp = random.Next(100000, 999999).ToString();

            // Store OTP in cache with expiry
            var cacheKey = $"otp_{userId}_{otp}";
            _cache.Set(cacheKey, otp, TimeSpan.FromSeconds(OTP_EXPIRY_SECONDS));

            // Also store the OTP for validation
            var otpKey = $"otp_user_{userId}";
            _cache.Set(otpKey, otp, TimeSpan.FromSeconds(OTP_EXPIRY_SECONDS));

            return await Task.FromResult(otp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating OTP for user {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> ValidateOTPAsync(string otp, int userId)
    {
        try
        {
            var otpKey = $"otp_user_{userId}";
            var storedOtp = _cache.Get<string>(otpKey);

            if (string.IsNullOrEmpty(storedOtp))
            {
                return await Task.FromResult(false);
            }

            return await Task.FromResult(storedOtp == otp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating OTP for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> IsOTPExpiredAsync(string otp, int userId)
    {
        try
        {
            var otpKey = $"otp_user_{userId}";
            var storedOtp = _cache.Get<string>(otpKey);

            // If OTP doesn't exist in cache, it's expired
            return await Task.FromResult(string.IsNullOrEmpty(storedOtp) || storedOtp != otp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking OTP expiry for user {UserId}", userId);
            return true;
        }
    }

    public async Task InvalidateOTPAsync(string otp, int userId)
    {
        try
        {
            var otpKey = $"otp_user_{userId}";
            _cache.Remove(otpKey);

            var cacheKey = $"otp_{userId}_{otp}";
            _cache.Remove(cacheKey);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating OTP for user {UserId}", userId);
        }
    }
}