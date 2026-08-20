using Microsoft.Extensions.Configuration;

namespace CallAnalog.Softphone.Services;

public sealed class CallNoteService
{
    private readonly PbxApiClient _apiClient;
    private readonly string _callNotePath;

    public CallNoteService(PbxApiClient apiClient, IConfiguration configuration)
    {
        _apiClient = apiClient;
        _callNotePath = configuration["PbxApi:CallNotePath"] ?? "/public/api/callNote";
    }

    public async Task SaveCallNoteAsync(string callId, string note, int? rating = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            throw new ArgumentException("Call id is required.", nameof(callId));
        }

        // Same callNote API: note + optional "rating" column (1–5).
        var payload = PbxPayloadBuilder.BuildCallNote(callId.Trim(), note ?? string.Empty, rating);
        await _apiClient.PostJsonAsync(_callNotePath, payload, cancellationToken);
    }
}
