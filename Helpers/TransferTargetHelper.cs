namespace CallAnalog.Softphone.Helpers;

internal static class TransferTargetHelper
{
    internal static string? ValidateTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "Transfer target is required.";
        }

        var trimmed = target.Trim();
        foreach (var character in trimmed)
        {
            if (!char.IsDigit(character) && character is not '+' and not '*' and not '#')
            {
                return "Transfer target may only contain digits, * and #.";
            }
        }

        return null;
    }
}
