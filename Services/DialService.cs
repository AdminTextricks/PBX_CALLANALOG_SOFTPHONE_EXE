using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public sealed class DialService
{
    private readonly SipService _sipService;
    private readonly SipLogService _log;
    private string _lastDialedNumber = string.Empty;

    public DialService(SipService sipService, SipLogService log)
    {
        _sipService = sipService;
        _log = log;
    }

    public string LastDialedNumber => _lastDialedNumber;

    public async Task<DialResult> PlaceCallAsync(string number, CancellationToken cancellationToken = default)
    {
        var validation = DialValidationHelper.ValidateNumber(number);
        number = number.Trim();
        if (!validation.Valid)
        {
            return Fail(number, validation.Code, validation.Message, validation.Reason);
        }

        if (_sipService.RegistrationState != SipRegistrationState.Registered)
        {
            return Fail(
                number,
                503,
                "Not registered",
                "SIP line is not registered. Sign out and sign in again to reconnect.");
        }

        if (_sipService.CallState != CallState.Idle)
        {
            return Fail(number, 486, "Line busy", "Finish or decline the current call before placing another.");
        }

        _lastDialedNumber = number;
        _log.Info(SipLogTag.Outbound, $"Dial pad request to {number}");

        try
        {
            await _sipService.CallAsync(number, cancellationToken);
            return new DialResult
            {
                Success = true,
                Number = number,
                Code = 200,
                Message = "Call connected",
                Reason = $"Connected to {number}."
            };
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _sipService.HangupAsync();
            }
            catch
            {
                // Best-effort cancel cleanup.
            }

            _log.Info(SipLogTag.Outbound, $"Outbound call to {number} cancelled by user.");
            return Fail(number, 499, "Call cancelled", "The call was cancelled.");
        }
        catch (SipCallFailedException ex)
        {
            LogOutboundFailure(number, ex.StatusCode, ex.Message);
            return Fail(number, ex.StatusCode, DialValidationHelper.GetFailureTitle(ex.StatusCode), ex.Message);
        }
        catch (Exception ex)
        {
            LogOutboundFailure(number, 503, ex.Message);
            return Fail(number, 503, "Call failed", ex.Message);
        }
    }

    private void LogOutboundFailure(string number, int statusCode, string message)
    {
        _log.Error(SipLogTag.Outbound, $"Outbound call to {number} failed: {message} ({statusCode})");
        _log.Info(SipLogTag.Outbound, $"What to try: {DialValidationHelper.GetFailureAdvice(statusCode)}");
    }

    public async Task<DialResult> RedialAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_lastDialedNumber))
        {
            return Fail(string.Empty, 404, "Nothing to redial", "There is no previous outbound number to redial.");
        }

        _log.Info(SipLogTag.Outbound, $"Redialing {_lastDialedNumber}");
        return await PlaceCallAsync(_lastDialedNumber, cancellationToken);
    }

    private static DialResult Fail(string number, int code, string message, string reason) =>
        new()
        {
            Success = false,
            Number = number,
            Code = code,
            Message = message,
            Reason = reason
        };
}
