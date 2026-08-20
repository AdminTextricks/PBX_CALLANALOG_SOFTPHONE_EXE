namespace CallAnalog.Softphone.Models;

public sealed class SipCallFailedException : InvalidOperationException
{
    public SipCallFailedException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
