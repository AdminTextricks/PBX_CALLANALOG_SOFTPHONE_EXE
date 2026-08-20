namespace CallAnalog.Softphone.Models;



public sealed class LoginResult

{

    public bool Success { get; init; }

    public int Code { get; init; }

    public string Message { get; init; } = string.Empty;

    public string Explanation { get; init; } = string.Empty;

    public string Extension { get; init; } = string.Empty;

    public string? LoginDomainName { get; init; }

    public int? LoginDomainPort { get; init; }
}

