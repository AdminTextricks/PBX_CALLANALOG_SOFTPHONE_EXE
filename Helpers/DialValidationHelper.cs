namespace CallAnalog.Softphone.Helpers;

internal static class DialValidationHelper
{
    private static readonly System.Text.RegularExpressions.Regex ValidNumberRegex = new(@"^[0-9+*#]+$");

    internal const int MinDialLength = 3;

    internal static (bool Valid, int Code, string Message, string Reason) ValidateNumber(string number)
    {
        number = number.Trim();

        if (string.IsNullOrWhiteSpace(number))
        {
            return (false, 400, "No number entered", "Enter a phone number or extension before placing a call.");
        }

        if (!ValidNumberRegex.IsMatch(number))
        {
            return (false, 400, "Invalid number format", "Only digits, * and # are allowed in the dialed number.");
        }

        if (number.Length < MinDialLength)
        {
            return (false, 400, "Number too short", "The dialed number must be at least 3 digits.");
        }

        return (true, 0, string.Empty, string.Empty);
    }

    internal static string GetFailureTitle(int statusCode) =>
        statusCode switch
        {
            486 => "Line busy",
            603 => "Call declined",
            404 => "Not found",
            487 or 499 => "Call cancelled",
            480 => "Unavailable",
            403 => "Forbidden",
            _ => "Call failed"
        };

    internal static string GetFailureAdvice(int statusCode) =>
        statusCode switch
        {
            486 => "Wait for the other party to become available, then try again.",
            603 => "The remote party declined the call.",
            404 => "Verify the number or extension and try again.",
            403 => "Your account may not be allowed to dial this destination.",
            _ => "Check registration status and network connectivity, then try again."
        };
}
