using System.Linq;

namespace WebRtcPhoneDialer.Core.Utilities
{
    public static class PhoneNumberValidator
    {
        public static bool IsValidPhoneNumber(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Check for SIP URI format (user@domain)
            if (input.Contains("@"))
                return input.Count(c => c == '@') == 1 && input.Split('@')[0].Length > 0;

            // Check for standard phone number format
            string digits = new string(input.Where(char.IsDigit).ToArray());

            // Allow phone numbers with 7-15 digits (ITU-T E.164 standard allows up to 15)
            return digits.Length >= 7 && digits.Length <= 15;
        }

        public static string FormatPhoneNumber(string input)
        {
            if (input.Contains("@"))
                return input; // SIP URI format

            string digits = new string(input.Where(char.IsDigit).ToArray());

            if (digits.Length == 10)
                return $"({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";

            if (digits.Length == 11 && digits[0] == '1')
                return $"+{digits[0]} ({digits.Substring(1, 3)}) {digits.Substring(4, 3)}-{digits.Substring(7)}";

            return input;
        }
    }
}
