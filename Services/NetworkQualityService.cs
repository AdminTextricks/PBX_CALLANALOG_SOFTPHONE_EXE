using System.Windows.Threading;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

public sealed class NetworkQualityService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _probeCts;
    private Func<SipRegistrationState>? _registrationStateProvider;
    private Func<Task<long?>>? _optionsRttProvider;
    private bool _isMonitoring;

    public NetworkQualityService(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _ = configuration;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) =>
        {
            if (_probeCts is not null)
            {
                _ = ProbeAsync(_probeCts.Token);
            }
        };
    }

    public event EventHandler<NetworkQualitySnapshot>? QualityUpdated;

    public NetworkQualitySnapshot Current { get; private set; } =
        new(0, "Unknown", null, false);

    public void ConfigureRegistrationProvider(Func<SipRegistrationState> provider) =>
        _registrationStateProvider = provider;

    public void ConfigureOptionsRttProvider(Func<Task<long?>> provider) =>
        _optionsRttProvider = provider;

    public void StartMonitoring()
    {
        if (_isMonitoring)
        {
            return;
        }

        _isMonitoring = true;
        _probeCts = new CancellationTokenSource();
        _timer.Start();
        _ = ProbeAsync(_probeCts.Token);
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring)
        {
            return;
        }

        _isMonitoring = false;
        _timer.Stop();
        _probeCts?.Cancel();
        _probeCts?.Dispose();
        _probeCts = null;
    }

    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var registrationState = _registrationStateProvider?.Invoke() ?? SipRegistrationState.Unregistered;
        var isRegistered = registrationState == SipRegistrationState.Registered;
        long? optionsRttMs = null;

        if (_optionsRttProvider is not null)
        {
            try
            {
                optionsRttMs = await _optionsRttProvider();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                optionsRttMs = null;
            }
        }

        Publish(NetworkQualityHelper.BuildSnapshot(optionsRttMs, registrationState, isRegistered));
    }

    private void Publish(NetworkQualitySnapshot snapshot)
    {
        Current = snapshot;
        QualityUpdated?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}
