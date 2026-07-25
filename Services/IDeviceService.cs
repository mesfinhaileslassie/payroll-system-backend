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

    // Get Device by Installation ID
    Task<Device?> GetDeviceByInstallationIdAsync(string installationId);

    // Challenge-Response (activation only)
    Task StoreChallengeAsync(int deviceId, string challenge);
    Task<string> GetStoredChallengeAsync(int deviceId);
    Task ClearChallengeAsync(int deviceId);
    bool VerifySignature(string challenge, string signature, string publicKey);
    Task UpdateDeviceCredentialsAsync(int deviceId, string deviceToken, string secretKey);
    Task MarkDeviceAsTrustedAsync(int deviceId);

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