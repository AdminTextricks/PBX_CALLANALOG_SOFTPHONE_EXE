namespace CallAnalog.Softphone.Models;

public sealed class DialResult
{
    public bool Success { get; init; }
    public string Number { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int Code { get; init; }
}
