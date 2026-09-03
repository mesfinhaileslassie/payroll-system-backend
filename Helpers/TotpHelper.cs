using OtpNet;
using System;

namespace PayrollSystem.API.Helpers
{
    public static class TotpHelper
    {
        private const int Step = 30;
        private const int Digits = 6;
        private const int Window = 2; // ±2 steps (60 seconds) to accommodate reasonable clock drift

        public static string GenerateTotp(string secretBase32)
        {
            var key = Base32Encoding.ToBytes(secretBase32);
            var totp = new Totp(key, step: Step, totpSize: Digits);
            return totp.ComputeTotp();
        }

        public static bool ValidateTotp(string secretBase32, string otp, out long timeStepMatched)
        {
            timeStepMatched = 0;
            if (string.IsNullOrEmpty(secretBase32) || string.IsNullOrEmpty(otp))
                return false;

            try
            {
                var key = Base32Encoding.ToBytes(secretBase32);
                var totp = new Totp(key, step: Step, totpSize: Digits);
                var result = totp.VerifyTotp(
                    otp,
                    out long timeStep,
                    window: new VerificationWindow(previous: Window, future: Window)
                );
                timeStepMatched = timeStep;
                return result;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsBase32(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            foreach (char c in input.ToUpperInvariant())
            {
                if (!((c >= 'A' && c <= 'Z') || (c >= '2' && c <= '7')))
                    return false;
            }
            return input.Length >= 8 && input.Length % 8 == 0;
        }
    }
}