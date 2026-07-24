// Services/IDeviceService.cs
using PayrollSystem.API.DTOs;
using PayrollSystem.API.Models;

namespace PayrollSystem.API.Services;

public interface IDeviceService
{
    // Registration
    Task<DeviceRegistrationResponse> RegisterDeviceAsync(DeviceRegistrationRequest request);

    // Activation
    Task<ActivationResponse> ActivateDeviceAsync(ActivationRequest request);

    // Get Device by Activation Code
    Task<Device?> GetDeviceByActivationCodeAsync(string activationCode);

    // Get Device by Secret Key
    Task<Device?> GetDeviceBySecretKeyAsync(string secretKey);

    // Challenge-Response (activation)
    Task StoreChallengeAsync(int deviceId, string challenge);
    Task<string> GetStoredChallengeAsync(int deviceId);
    Task ClearChallengeAsync(int deviceId);
    bool VerifySignature(string challenge, string signature, string publicKey);
    Task UpdateDeviceCredentialsAsync(int deviceId, string deviceToken, string secretKey);
    Task MarkDeviceAsTrustedAsync(int deviceId);

    // OTP (deprecated)
    Task<OTPVerificationResponse> VerifyOTPAsync(OTPVerificationRequest request);
    Task<OTPGenerateResponse> GenerateOTPAsync(int userId, int deviceId);

    // Challenge-Response for actions (correct signature)
    Task<ChallengeResponse> CreateChallengeAsync(string actionType, int actionId, int employeeId);
    Task<bool> VerifyChallengeAsync(string installationId, string signature, string challenge);

    // Device Management
    Task<Device?> GetDeviceByAndroidIdAsync(string androidId);
    Task<Device?> GetDeviceByIdAsync(int deviceId);
    Task<List<Device>> GetUserDevicesAsync(int userId);
    Task<bool> IsDeviceActiveAsync(int deviceId);
    Task<bool> UpdateDeviceStatusAsync(int deviceId, string status);
    Task<bool> DeleteDeviceAsync(int deviceId);

    Task<Device?> GetActiveDeviceAsync();
    Task<List<Device>> GetAllActiveDevicesAsync();
}