namespace CallAnalog.Softphone.Services;

internal static class LoginErrorCatalog
{
    public static string Explain(int code, string? apiMessage)
    {
        if (!string.IsNullOrWhiteSpace(apiMessage))
        {
            var normalized = apiMessage.Trim();
            if (TryExplainFromMessage(normalized, out var fromMessage))
            {
                return fromMessage;
            }
        }

        return code switch
        {
            200 => "Your extension credentials were accepted by the CallAnalog server.",
            400 => "The login request was incomplete. Enter a valid extension number and password.",
            404 => "This extension is not registered on the CallAnalog PBX. Check the number and try again.",
            409 => "The extension exists, but the password is incorrect. Verify your password and try again.",
            401 or 403 => "The server rejected these credentials. Contact your administrator if the problem continues.",
            408 => "The login request timed out before the server responded. Check your internet connection and try again.",
            500 or 502 or 503 => "The CallAnalog server is temporarily unavailable. Please try again in a few minutes.",
            -1 => "Could not reach the CallAnalog server. Check your internet connection and firewall settings.",
            -2 => "The server returned an unexpected response. Try again or contact support.",
            _ => "Login could not be completed. Verify your extension, password, and network connection."
        };
    }

    private static bool TryExplainFromMessage(string message, out string explanation)
    {
        explanation = string.Empty;

        if (message.Contains("Extension dose not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Extension does not exist", StringComparison.OrdinalIgnoreCase))
        {
            explanation = "This extension number is not registered on the CallAnalog PBX.";
            return true;
        }

        if (message.Contains("Invalid password", StringComparison.OrdinalIgnoreCase))
        {
            explanation = "The extension exists, but the password you entered is wrong.";
            return true;
        }

        if (message.Contains("Extension number is required", StringComparison.OrdinalIgnoreCase))
        {
            explanation = "Enter your extension number before logging in.";
            return true;
        }

        if (message.Contains("password", StringComparison.OrdinalIgnoreCase)
            && message.Contains("required", StringComparison.OrdinalIgnoreCase))
        {
            explanation = "Enter your extension password before logging in.";
            return true;
        }

        return false;
    }
}
