using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AgentSyncConsole.CustomerBooks.Services
{
    public static class ValidationHelpers
    {
        // Same junk/placeholder values rejected in the original implementation.
        private static readonly HashSet<string> PhoneBlacklist = new()
        {
            "0", "00", "000", "91", "123", "0000000000", "1234567890"
        };

        private static readonly Regex EmailRegex = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);
        private static readonly Regex AllSameDigitRegex = new(@"^(\d)\1+$", RegexOptions.Compiled);
        private static readonly Regex NonDigitRegex = new(@"[^0-9]", RegexOptions.Compiled);

        public static bool IsNonEmpty(string? value) => !string.IsNullOrWhiteSpace(value);

        public static bool IsValidEmail(string? value)
        {
            if (!IsNonEmpty(value))
            {
                return false;
            }

            return EmailRegex.IsMatch(value!.Trim());
        }

        public static bool IsValidPhone(string? value)
        {
            if (!IsNonEmpty(value))
            {
                return false;
            }

            var raw = value!.Trim();
            var digitsOnly = NonDigitRegex.Replace(raw, string.Empty);

            if (string.IsNullOrEmpty(digitsOnly))
            {
                return false;
            }

            if (PhoneBlacklist.Contains(digitsOnly))
            {
                return false;
            }

            if (AllSameDigitRegex.IsMatch(digitsOnly))
            {
                return false;
            }

            if (digitsOnly.Length < 7 || digitsOnly.Length > 15)
            {
                return false;
            }

            return true;
        }
    }
}
