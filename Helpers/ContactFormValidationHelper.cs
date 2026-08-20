using System.Text.RegularExpressions;

namespace CallAnalog.Softphone.Helpers;

internal static class ContactFormValidationHelper
{
    internal static readonly Regex PhoneInputRegex = new(@"^[0-9+\*#\-() ]$");

    internal static bool IsAllowedPhoneCharacter(char ch) =>
        PhoneInputRegex.IsMatch(ch.ToString());

    internal static bool IsAllowedPhoneText(string text) =>
        text.All(ch => PhoneInputRegex.IsMatch(ch.ToString()));

    internal static (bool Valid, string ErrorMessage) Validate(string? name, string? number)
    {
        var nameValid = !string.IsNullOrWhiteSpace(name);
        var numberValid = !string.IsNullOrWhiteSpace(number);

        if (nameValid && numberValid)
        {
            return (true, string.Empty);
        }

        return (false, "Name and number are required.");
    }
}
