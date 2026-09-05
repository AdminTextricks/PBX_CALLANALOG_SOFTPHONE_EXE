using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace CallAnalog.Softphone.Services;

public sealed class SipService : IDisposable
{
    private static string UserAgent => $"CallAnalog-Softphone/{BuildInfo.Version}";

    private const int OutboundRingTimeoutSeconds = 90;

    private readonly UserSettingsService _settingsService;
    private readonly SipLogService _log;
    private readonly SipRegistrationAuthCacheService _registrationAuthCache;
    private readonly StickyPbxIpCache _stickyPbxIpCache;
    private readonly bool _preferWasapiAudio;
    private readonly bool _enableDscpMarking;
    private readonly bool _enableMidCallMediaRecovery;
    private readonly bool _enableSipReinviteRecovery;
    private readonly CallQualityMonitor _callQualityMonitor = new();
    private readonly object _sync = new();
    private bool _sipLoggingConfigured;

    private SIPTransport? _transport;
    private SIPRegistrationUserAgent? _registrationAgent;
    private SIPUserAgent? _userAgent;
    private MutingAudioEndPoint? _audioEndPoint;
    private VoIPMediaSession? _mediaSession;
    private ProvisionConfig? _config;
    private SIPRequest? _pendingIncomingRequest;
    private SIPServerUserAgent? _pendingIncomingUas;
    private string? _remoteParty;
    private TaskCompletionSource<OutboundCallOutcome>? _outboundCallCompletion;
    private Timer? _keepAliveTimer;
    private Timer? _reconnectTimer;
    private Timer? _mediaRecoveryTimer;
    private int _keepAliveSendFailures;
    private DateTimeOffset? _lastSipActivityUtc;
    private DateTimeOffset? _lastRtpUtc;
    private DateTimeOffset? _lastMediaRecoveryUtc;
    private int _localMediaRecoveryAttemptsSinceRtp;
    private int _reconnectAttempt;
    private bool _isMuted;
    private bool _isSpeakerMuted;
    private bool _isOnHold;
    private bool _isRecording;
    private MixedCallRecorder? _mixedRecorder;
    private Action<byte[]>? _playbackTapHandler;
    private EventHandler<WaveInEventArgs>? _captureTapHandler;
    private WaveOutEvent? _holdMusicPlayer;
    private WaveStream? _holdMusicReader;
    private bool _holdMusicMutedCallSpeaker;
    private string? _activeCallId;
    private bool _wasConnected;
    private bool _isOutboundCall;
    private DateTimeOffset? _activeSegmentStartedAt;
    private TimeSpan _accumulatedActiveDuration;
    private SipRegistrationDigestCacheEntry? _lastRegisterAuthSent;
    private SIPRequest? _waitingIncomingRequest;
    private SIPServerUserAgent? _waitingIncomingUas;
    private string? _waitingCallerNumber;
    private string? _waitingCallerName;
    private string? _heldRemoteParty;
    private string? _heldCallId;
    private bool _hasHeldCall;
    private SIPUserAgent? _waitingCallUserAgent;
    private VoIPMediaSession? _waitingMediaSession;
    private MutingAudioEndPoint? _waitingAudioEndPoint;
    private ActiveCallLeg _activeCallLeg = ActiveCallLeg.Primary;
    private bool _replacingPrimaryWithWaitingCall;
    private LegHangupIntent _legHangupIntent;
    private string? _recordingFilePath;
    private SIPUserAgent? _wiredPrimaryEventsAgent;
    private SIPUserAgent? _wiredWaitingEventsAgent;
    private bool _suppressMissedCallBadge;
    private EventHandler<StoppedEventArgs>? _holdMusicStoppedHandler;

    private readonly record struct OutboundCallOutcome(bool Success, string Message, int StatusCode);

    private enum ActiveCallLeg
    {
        Primary,
        Waiting
    }

    private enum LegHangupIntent
    {
        None,
        PromoteWaitingAfterPrimaryEnd,
        ResumePrimaryAfterWaitingEnd
    }

    public SipService(IConfiguration configuration, UserSettingsService settingsService, SipLogService log)
    {
        _settingsService = settingsService;
        _log = log;
        var storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CallAnalog");
        _registrationAuthCache = new SipRegistrationAuthCacheService(storageDirectory);
        _stickyPbxIpCache = new StickyPbxIpCache(storageDirectory);
        _preferWasapiAudio = bool.TryParse(configuration["Audio:PreferWasapi"], out var preferWasapi) && preferWasapi;
        _enableDscpMarking = !bool.TryParse(configuration["Network:EnableDscp"], out var enableDscp) || enableDscp;
        _enableMidCallMediaRecovery = bool.TryParse(configuration["Media:EnableMidCallRecovery"], out var midCall) && midCall;
        _enableSipReinviteRecovery = bool.TryParse(configuration["Media:EnableSipReinviteRecovery"], out var reinvite) && reinvite;
        SipNatHelper.ConfigureTurn(configuration);
    }

    public SipRegistrationState RegistrationState { get; private set; } = SipRegistrationState.Unregistered;

    public CallState CallState { get; private set; } = CallState.Idle;

    public DateTimeOffset? ConnectedAt { get; private set; }

    public DateTimeOffset? IncomingStartedAt { get; private set; }

    public TimeSpan ActiveCallDuration
    {
        get
        {
            if (IncomingStartedAt is DateTimeOffset ringingSince
                && CallState is CallState.Incoming or CallState.InCall or CallState.OnHold or CallState.CallWaitingRinging)
            {
                return DateTimeOffset.Now - ringingSince;
            }

            var total = _accumulatedActiveDuration;
            if (_activeSegmentStartedAt is not null && CallState is CallState.InCall)
            {
                total += DateTimeOffset.Now - _activeSegmentStartedAt.Value;
            }

            return total;
        }
    }

    public string? RemoteParty => _remoteParty;

    public bool IsOutboundCall => _isOutboundCall;

    public bool IsMuted => _isMuted;
    public bool IsSpeakerMuted => _isSpeakerMuted;
    public bool IsOnHold => _isOnHold;
    public bool IsRecording => _isRecording;
    public bool CanRecordLocally => _settingsService.Settings.CallRecordingEnabled;

    public string? ActiveCallId => _activeCallId;

    public bool HasWaitingCall => _waitingIncomingRequest is not null || _waitingIncomingUas is not null;
    public string? WaitingCallerNumber => _waitingCallerNumber;
    public string? WaitingCallerName => _waitingCallerName;
    public bool HasHeldCall => _hasHeldCall;
    public string? HeldRemoteParty => _heldRemoteParty;
    public bool IsWaitingCallLegActive => _hasHeldCall && _activeCallLeg == ActiveCallLeg.Waiting;

    public bool ConsumeMissedCallBadgeSuppression()
    {
        lock (_sync)
        {
            var suppress = _suppressMissedCallBadge;
            _suppressMissedCallBadge = false;
            return suppress;
        }
    }

    public ProvisionConfig? CurrentConfig => _config;

    public event EventHandler<SipRegistrationState>? RegistrationStateChanged;
    public event EventHandler<CallState>? CallStateChanged;
    public event EventHandler<IncomingCallEventArgs>? IncomingCall;
    public event EventHandler<IncomingCallEventArgs>? IncomingCallWaiting;
    public event EventHandler<IncomingCallEventArgs>? IncomingCallRejectedWhileBusy;
    public event EventHandler<bool>? RecordingStateChanged;
    public event EventHandler<CallEndedEventArgs>? CallEnded;

    public CallQualityMonitor CallQuality => _callQualityMonitor;
    public event Action<byte[], int>? IncomingPlaybackPcm;
    public event Action<byte[], int>? OutgoingCapturePcm;

    public Task RegisterAsync(
        ProvisionConfig config,
        CancellationToken cancellationToken = default,
        TimeSpan? registrationWaitTimeout = null)
    {
        EnsureSipLogging();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task.Run(async () =>
        {
            lock (_sync)
            {
                UnregisterInternal();

                _config = config;
                SetRegistrationState(SipRegistrationState.Registering);
                _log.BeginSection("SIP REGISTER");
            }

            await ApplyStickyPbxIpAsync(config, cancellationToken);

            lock (_sync)
            {
                if (_config?.Extension != config.Extension)
                {
                    return;
                }

                var registrar = GetRegistrarServer(config);
                _log.Info(
                    SipLogTag.Register,
                    $"Registering extension {config.Extension} as sip:{config.Extension}@{config.SipServer}:{config.SipPort} via {config.Transport.ToUpperInvariant()} to {registrar}");

                _transport = new SIPTransport();
                AddTransportChannel(config);
                WireTransportTracing(_transport);
                ApplyTransportPublicAddress();
            }

            _ = WarmUpPublicIpAsync();

            await PreconnectRegistrarAsync(config);

            lock (_sync)
            {
                if (_config?.Extension != config.Extension)
                {
                    return;
                }

                var registrar = GetRegistrarServer(config);
                var registrationExpirySeconds = GetRegistrationExpirySeconds();
                _registrationAgent = new SIPRegistrationUserAgent(
                    _transport!,
                    config.Extension,
                    config.Password,
                    registrar,
                    registrationExpirySeconds,
                    maxRegistrationAttemptTimeout: 45,
                    maxRegisterAttempts: 3,
                    exitOnUnequivocalFailure: true,
                    sendUsernameInContactHeader: false);

                _registrationAgent.UserAgent = UserAgent;
                _registrationAgent.AdjustRegister = request => AdjustRegisterRequest(request, config);

                _registrationAgent.RegistrationSuccessful += (_, response) =>
                {
                    _reconnectAttempt = 0;
                    _keepAliveSendFailures = 0;
                    _lastSipActivityUtc = DateTimeOffset.UtcNow;
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = null;
                    SetRegistrationState(SipRegistrationState.Registered);
                    PersistRegistrationAuthCache();
                    RememberStickyPbxIpOnSuccess(config);
                    _log.Info(SipLogTag.Register, $"Registered extension {config.Extension} ({response.StatusCode}) — line is online.");
                    _log.EndSection("SIP REGISTER");
                    _settingsService.SaveCachedPublicIp();
                    StartKeepAlive();
                    completion.TrySetResult();
                };

                _registrationAgent.RegistrationFailed += (_, response, message) =>
                {
                    if (response?.Status is SIPResponseStatusCodesEnum.Forbidden)
                    {
                        _registrationAuthCache.Clear(config.Extension);
                        _log.Warn(SipLogTag.Register, $"Cleared cached REGISTER digest for extension {config.Extension} after 403 Forbidden.");
                    }

                    if (!completion.Task.IsCompleted && OpenSipsAuthHelper.IsRegistrationAuthChallenge(response))
                    {
                        _log.Info(
                            SipLogTag.Register,
                            $"Registration auth challenge ({response?.StatusCode}); waiting for authenticated retry.");
                        return;
                    }

                    var detail = response is null ? message : $"{message} ({response.StatusCode})";
                    if (!completion.Task.IsCompleted)
                    {
                        InvalidateStickyPbxIpOnFailure(config);
                        SetRegistrationState(SipRegistrationState.Failed);
                        LogRegistrationFailure(config, detail);
                        _log.EndSection("SIP REGISTER");
                        completion.TrySetException(new InvalidOperationException(detail));
                        return;
                    }

                    _log.Error(SipLogTag.Register, $"Registration lost: {detail}");
                    InvalidateStickyPbxIpOnFailure(config);
                    ScheduleRegistrationReconnect(detail);
                };

                _registrationAgent.RegistrationTemporaryFailure += (_, response, message) =>
                {
                    if (!completion.Task.IsCompleted && OpenSipsAuthHelper.IsRegistrationAuthChallenge(response))
                    {
                        _log.Info(
                            SipLogTag.Register,
                            $"Registration auth challenge ({response?.StatusCode}); waiting for authenticated retry.");
                        return;
                    }

                    if (!completion.Task.IsCompleted)
                    {
                        InvalidateStickyPbxIpOnFailure(config);
                        SetRegistrationState(SipRegistrationState.Failed);
                        var detail = response is null ? message : $"{message} ({response.StatusCode})";
                        LogRegistrationFailure(config, detail);
                        _log.EndSection("SIP REGISTER");
                        completion.TrySetException(new InvalidOperationException(detail));
                        return;
                    }

                    var tempDetail = response is null ? message : $"{message} ({response.StatusCode})";
                    _log.Warn(SipLogTag.Register, $"Registration temporary failure: {tempDetail}");
                    InvalidateStickyPbxIpOnFailure(config);
                    ScheduleRegistrationReconnect(tempDetail);
                };

                // isTransportExclusive=false so out-of-dialog OPTIONS are not auto-rejected with 405;
                // WireTransportTracing answers those probes with 200 OK for OpenSIPS qualify.
                EnsureCallUserAgentInitialized();
                _registrationAgent.Start();
            }
        }, cancellationToken);

        return WaitForRegistrationAsync(
            completion.Task,
            cancellationToken,
            registrationWaitTimeout ?? TimeSpan.FromSeconds(90));
    }

    public Task RefreshRegistrationTimingAsync()
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                if (_config is null || _transport is null)
                {
                    return;
                }

                if (RegistrationState != SipRegistrationState.Registered)
                {
                    return;
                }

                _log.Info("Applying updated SIP register/keep-alive timing via re-REGISTER.");
                StopKeepAlive();
                _registrationAgent?.Stop();

                var registrationExpirySeconds = GetRegistrationExpirySeconds();
                _registrationAgent = new SIPRegistrationUserAgent(
                    _transport,
                    _config.Extension,
                    _config.Password,
                    GetRegistrarServer(_config),
                    registrationExpirySeconds,
                    maxRegistrationAttemptTimeout: 45,
                    maxRegisterAttempts: 3,
                    exitOnUnequivocalFailure: true,
                    sendUsernameInContactHeader: false);

                _registrationAgent.UserAgent = UserAgent;
                _registrationAgent.AdjustRegister = request => AdjustRegisterRequest(request, _config);

                _registrationAgent.RegistrationSuccessful += (_, response) =>
                {
                    _reconnectAttempt = 0;
                    _keepAliveSendFailures = 0;
                    _lastSipActivityUtc = DateTimeOffset.UtcNow;
                    SetRegistrationState(SipRegistrationState.Registered);
                    PersistRegistrationAuthCache();
                    _log.Info($"Re-registered extension {_config.Extension} ({response.StatusCode})");
                    StartKeepAlive();
                };

                _registrationAgent.RegistrationFailed += (_, response, message) =>
                {
                    if (response?.Status is SIPResponseStatusCodesEnum.Forbidden)
                    {
                        _registrationAuthCache.Clear(_config.Extension);
                        _log.Warn($"Cleared cached REGISTER digest for extension {_config.Extension} after 403 Forbidden.");
                    }

                    if (OpenSipsAuthHelper.IsRegistrationAuthChallenge(response))
                    {
                        _log.Info(
                            $"Re-registration auth challenge ({response?.StatusCode}); waiting for authenticated retry.");
                        return;
                    }

                    var detail = response is null ? message : $"{message} ({response.StatusCode})";
                    _log.Error($"Re-registration failed: {detail}");
                    ScheduleRegistrationReconnect(detail);
                };

                _registrationAgent.RegistrationTemporaryFailure += (_, response, message) =>
                {
                    if (OpenSipsAuthHelper.IsRegistrationAuthChallenge(response))
                    {
                        _log.Info(
                            $"Re-registration auth challenge ({response?.StatusCode}); waiting for authenticated retry.");
                        return;
                    }

                    var tempDetail = response is null ? message : $"{message} ({response.StatusCode})";
                    _log.Warn($"Re-registration temporary failure: {tempDetail}");
                    ScheduleRegistrationReconnect(tempDetail);
                };

                if (_userAgent is null)
                {
                    EnsureCallUserAgentInitialized();
                }

                _registrationAgent.Start();
            }
        });
    }

    public Task UnregisterAsync()
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                UnregisterInternal();
            }
        });
    }

    public async Task CallAsync(string number, CancellationToken cancellationToken = default)
    {
        SIPUserAgent? userAgent;

        lock (_sync)
        {
            if (_config is null || _userAgent is null)
            {
                throw new InvalidOperationException("SIP is not registered.");
            }

            if (RegistrationState != SipRegistrationState.Registered)
            {
                throw new InvalidOperationException("Offline — register to place calls.");
            }

            if (CallState != CallState.Idle)
            {
                throw new InvalidOperationException("A call is already in progress.");
            }

            number = number.Trim();
            if (string.IsNullOrWhiteSpace(number))
            {
                throw new InvalidOperationException("Enter a number to call.");
            }

            userAgent = _userAgent;
        }

        var destination = BuildDestinationUri(number);
        _log.BeginSection("OUTBOUND CALL");
        _log.Info(SipLogTag.Outbound, $"Placing call to {destination}");

        _remoteParty = number;
        _isOutboundCall = true;
        _activeCallId = Guid.NewGuid().ToString("N");
        SetCallState(CallState.Outgoing);

        var mediaSession = CreateMediaSession();
        var completion = new TaskCompletionSource<OutboundCallOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _outboundCallCompletion = completion;

        var callDescriptor = CreateOutboundCallDescriptor(destination);

        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            lock (_sync)
            {
                _userAgent?.Cancel();
            }

            completion.TrySetCanceled(cancellationToken);
        });

        try
        {
            await userAgent.InitiateCallAsync(callDescriptor, mediaSession, OutboundRingTimeoutSeconds);

            using var callTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            callTimeoutCts.CancelAfter(TimeSpan.FromSeconds(OutboundRingTimeoutSeconds + 5));

            OutboundCallOutcome outcome;
            try
            {
                outcome = await completion.Task.WaitAsync(callTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                userAgent.Cancel();
                ResetCallState();
                throw new InvalidOperationException("Call timed out before an answer or failure response.");
            }

            if (!outcome.Success)
            {
                ResetCallState();
                throw new SipCallFailedException(outcome.Message, outcome.StatusCode);
            }

            MarkConnected();
            SetCallState(CallState.InCall);
            _activeCallId = SipCallIdHelper.Normalize(userAgent.Dialogue?.CallId) ?? _activeCallId;
            ApplyCallAudioLevels();
            await EnsurePlaybackReadyAsync();
            _log.Info(SipLogTag.Outbound, $"Call connected to {number}");
            _log.EndSection("OUTBOUND CALL");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResetCallState();
            throw;
        }
        finally
        {
            _outboundCallCompletion = null;
        }
    }

    public async Task AnswerAsync()
    {
        SIPUserAgent? userAgent;
        SIPRequest? pendingRequest;
        SIPServerUserAgent? pendingUas;

        lock (_sync)
        {
            userAgent = _userAgent;
            pendingRequest = _pendingIncomingRequest;
            pendingUas = _pendingIncomingUas;
        }

        if (userAgent is null || pendingRequest is null)
        {
            throw new InvalidOperationException("No incoming call to answer.");
        }

        IncomingCallLog.Marker("ANSWER_START", _remoteParty);
        IncomingCallLog.Marker("MEDIA_INIT_START");
        IncomingCallLog.Marker("AUDIO_DEVICE_ENUMERATION_START");
        var mediaSession = CreateMediaSession();
        IncomingCallLog.Marker("AUDIO_DEVICE_ENUMERATION_END");
        IncomingCallLog.Marker("MEDIA_START");
        var serverAgent = pendingUas ?? userAgent.AcceptCall(pendingRequest);
        var answered = await userAgent.Answer(
            serverAgent,
            mediaSession,
            SipNatHelper.GetConnectionAddressForSdp());

        if (!answered)
        {
            throw new InvalidOperationException("Failed to answer the call.");
        }

        ApplyCallAudioLevels();

        lock (_sync)
        {
            _pendingIncomingRequest = null;
            _pendingIncomingUas = null;
        }

        MarkConnected();
        SetCallState(CallState.InCall);
        _isOutboundCall = false;
        _activeCallId = SipCallIdHelper.Normalize(userAgent.Dialogue?.CallId ?? pendingRequest.Header.CallId);
        await EnsurePlaybackReadyAsync();
        IncomingCallLog.Marker("CALL_CONNECTED", _remoteParty);
        _log.Info($"Connected to {_remoteParty}");
    }

    private static bool IsActiveCallStateForNetworkHangup(CallState state) =>
        NetworkLossHelper.ShouldHangupOnNetworkLoss(state);

    public Task DeclineIncomingAsync()
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                if (_userAgent is null || _pendingIncomingRequest is null)
                {
                    throw new InvalidOperationException("No incoming call to decline.");
                }

                _log.Info(SipLogTag.Inbound, $"Declining call from {_remoteParty}");
                _suppressMissedCallBadge = true;
                if (_pendingIncomingUas is not null)
                {
                    _pendingIncomingUas.Reject(SIPResponseStatusCodesEnum.Decline, "Declined");
                }
                else
                {
                    var serverAgent = _userAgent.AcceptCall(_pendingIncomingRequest);
                    serverAgent.Reject(SIPResponseStatusCodesEnum.Decline, "Declined");
                }

                _pendingIncomingRequest = null;
                _pendingIncomingUas = null;
                ResetCallState();
            }
        });
    }

    public void NotifyNetworkLost()
    {
        var shouldHangup = false;

        lock (_sync)
        {
            if (_config is null || RegistrationState is SipRegistrationState.Unregistered)
            {
                return;
            }

            shouldHangup = IsActiveCallStateForNetworkHangup(CallState);

            _log.Warn(SipLogTag.Network, "Network connection lost — SIP line will attempt to reconnect.");
            _log.CustomerError(
                SipLogTag.Network,
                "Internet or LAN connectivity was lost.",
                "Check your network cable/Wi-Fi, then wait for the softphone to reconnect automatically.");
            SetRegistrationState(SipRegistrationState.Reconnecting);
        }

        if (shouldHangup)
        {
            _log.Warn(SipLogTag.Network, "Network lost during active call — sending BYE to remote party.");
            _ = HangupAsync();
        }
    }

    public void NotifyNetworkRestored()
    {
        lock (_sync)
        {
            if (_config is null)
            {
                return;
            }

            _log.Info(SipLogTag.Network, "Network connection restored — attempting SIP re-registration.");
            if (RegistrationState is SipRegistrationState.Reconnecting
                or SipRegistrationState.Failed
                or SipRegistrationState.Registered)
            {
                _reconnectAttempt = 0;
                TryRestartRegistration();
            }
        }
    }

    public Task HangupAsync()
    {
        return Task.Run(async () =>
        {
            var resumedHeldCall = false;
            var declinedWaitingOnly = false;

            lock (_sync)
            {
                _log.Info("Hanging up call");

                if (CallState == CallState.CallWaitingRinging
                    && (_waitingIncomingRequest is not null || _waitingIncomingUas is not null))
                {
                    DeclineWaitingCallLocked();
                    declinedWaitingOnly = true;
                }
                else
                {
                    StopRecordingInternal();
                    StopHoldMusicInternal();

                    if (_hasHeldCall && _waitingCallUserAgent is not null)
                    {
                        resumedHeldCall = TryHangupActiveLegAndResumeHeld();
                    }

                    if (!resumedHeldCall)
                    {
                        if (CallState == CallState.Outgoing)
                        {
                            _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(false, "Call cancelled", 487));
                            _userAgent?.Cancel();
                        }
                        else
                        {
                            _waitingCallUserAgent?.Hangup();
                            _userAgent?.Hangup();
                        }

                        _pendingIncomingRequest = null;
                        _pendingIncomingUas = null;
                        CleanupWaitingCallInternal();
                    }
                }
            }

            if (declinedWaitingOnly)
            {
                await RestorePrimaryCallPlaybackAsync();
                return;
            }

            if (resumedHeldCall)
            {
                await FinalizeLegSwapPlaybackAsync();
                return;
            }

            ResetCallState();
        });
    }

    /// <summary>
    /// Ends every active SIP leg without resuming held calls. Used on sign-out so the remote party is released.
    /// </summary>
    public Task HangupAllLegsForSignOutAsync()
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                if (CallState == CallState.Idle
                    && _userAgent?.IsCallActive != true
                    && _waitingCallUserAgent?.IsCallActive != true
                    && _pendingIncomingRequest is null
                    && _outboundCallCompletion is null)
                {
                    return;
                }

                _log.Info(SipLogTag.Inbound, "Hanging up all call legs for sign-out");
                StopRecordingInternal();
                StopHoldMusicInternal();

                if (CallState == CallState.Outgoing)
                {
                    _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(false, "Call cancelled", 487));
                    _userAgent?.Cancel();
                }
                else
                {
                    _waitingCallUserAgent?.Hangup();
                    _userAgent?.Hangup();
                }

                _pendingIncomingRequest = null;
                _pendingIncomingUas = null;
                _heldRemoteParty = null;
                _heldCallId = null;
                _hasHeldCall = false;
                _activeCallLeg = ActiveCallLeg.Primary;
                CleanupWaitingCallInternal();
                DisposeMediaSession();
            }

            ResetCallState();
        });
    }

    public Task ToggleHoldAsync()
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                if (_userAgent is null || CallState is not CallState.InCall and not CallState.OnHold)
                {
                    return;
                }

                var activeAgent = _hasHeldCall && _activeCallLeg == ActiveCallLeg.Waiting
                    ? _waitingCallUserAgent
                    : _userAgent;
                if (activeAgent is null)
                {
                    return;
                }

                if (CallState == CallState.OnHold)
                {
                    activeAgent.TakeOffHold();
                    _isOnHold = false;
                    _activeSegmentStartedAt = DateTimeOffset.Now;
                    SetCallState(CallState.InCall);
                    StopHoldMusicInternal();
                    _log.Info("Call resumed");
                    _ = EnsureActiveLegPlaybackReadyAsync();
                }
                else
                {
                    activeAgent.PutOnHold();
                    _isOnHold = true;
                    AccumulateActiveDuration();
                    _activeSegmentStartedAt = null;
                    SetCallState(CallState.OnHold);
                    PlayHoldMusicInternal();
                    _log.Info("Call on hold");
                }
            }
        });
    }

    public Task ToggleMuteAsync()
    {
        var audioEndPoint = GetActiveAudioEndPoint();
        if (audioEndPoint is null)
        {
            return Task.CompletedTask;
        }

        _isMuted = !_isMuted;
        audioEndPoint.SetMuted(_isMuted);
        _log.Info(_isMuted ? "Microphone muted (sending silence)" : "Microphone unmuted");
        return Task.CompletedTask;
    }

    public async Task ToggleSpeakerMuteAsync()
    {
        var audioEndPoint = GetActiveAudioEndPoint();
        if (audioEndPoint is null)
        {
            return;
        }

        _isSpeakerMuted = !_isSpeakerMuted;
        await audioEndPoint.SetSpeakerMuted(_isSpeakerMuted);
        _log.Info(_isSpeakerMuted ? "Speaker muted" : "Speaker unmuted");
    }

    public Task StartRecordingAsync()
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                if (_isRecording)
                {
                    return;
                }

                if (!_settingsService.Settings.CallRecordingEnabled)
                {
                    throw new InvalidOperationException("Enable local call recording in Settings before recording.");
                }

                if (CallState is not CallState.InCall and not CallState.OnHold and not CallState.CallWaitingRinging)
                {
                    throw new InvalidOperationException("No active call to record.");
                }

                var directory = _settingsService.Settings.CallRecordingDirectory
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CallAnalog",
                        "recordings");
                Directory.CreateDirectory(directory);

                var fileName = $"call_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeFileName(_remoteParty ?? "unknown")}.wav";
                _recordingFilePath = Path.Combine(directory, fileName);

                _mixedRecorder = new MixedCallRecorder();
                _mixedRecorder.Start(
                    _recordingFilePath,
                    _settingsService.Settings.MicrophoneDevice,
                    _settingsService.Settings.MicrophoneDeviceId);
                AttachPlaybackTap();

                _isRecording = true;
                RecordingStateChanged?.Invoke(this, true);
                _log.Info($"Recording both call legs (mic + remote) to {_recordingFilePath}");
            }
        });
    }

    public Task StopRecordingAsync()
    {
        return Task.Run(StopRecordingInternal);
    }

    public async Task DeclineWaitingCallAsync()
    {
        lock (_sync)
        {
            DeclineWaitingCallLocked();
        }

        await RestorePrimaryCallPlaybackAsync();
    }

    private void DeclineWaitingCallLocked()
    {
        if (_userAgent is null || (_waitingIncomingRequest is null && _waitingIncomingUas is null))
        {
            throw new InvalidOperationException("No waiting call to decline.");
        }

        _log.Info($"Declining waiting call from {_waitingCallerNumber}");
        if (_waitingIncomingUas is not null)
        {
            _waitingIncomingUas.Reject(SIPResponseStatusCodesEnum.Decline, "Declined");
        }
        else if (_waitingIncomingRequest is not null)
        {
            EnsureWaitingCallUserAgent();
            var serverAgent = _waitingCallUserAgent?.AcceptCall(_waitingIncomingRequest);
            serverAgent?.Reject(SIPResponseStatusCodesEnum.Decline, "Declined");
        }

        ClearWaitingCallState(restorePreviousCallState: true);
    }

    /// <summary>
    /// Re-arms primary call WinMM playback after a second WaveOut (ringtone/hold music) may have disrupted it.
    /// </summary>
    public Task RestorePrimaryCallPlaybackAsync()
    {
        if (CallState is not (CallState.InCall or CallState.OnHold))
        {
            return Task.CompletedTask;
        }

        return FinalizeLegSwapPlaybackAsync();
    }

    public async Task HoldAndAnswerWaitingCallAsync()
    {
        SIPUserAgent? userAgent;
        SIPRequest? waitingRequest;
        SIPServerUserAgent? waitingUas;
        string? waitingNumber;

        lock (_sync)
        {
            userAgent = _userAgent;
            waitingRequest = _waitingIncomingRequest;
            waitingUas = _waitingIncomingUas;
            waitingNumber = _waitingCallerNumber;

            if (userAgent is null || (waitingRequest is null && waitingUas is null))
            {
                throw new InvalidOperationException("No waiting call to answer.");
            }

            if (CallState is not CallState.InCall and not CallState.OnHold and not CallState.CallWaitingRinging)
            {
                throw new InvalidOperationException("Call waiting is only available during an active call.");
            }
        }

        _log.Info($"Answering waiting call from {waitingNumber} (holding current call)");

        lock (_sync)
        {
            if (!_isOnHold && userAgent!.IsCallActive)
            {
                userAgent.PutOnHold();
                _isOnHold = true;
                AccumulateActiveDuration();
                _activeSegmentStartedAt = null;
                StopHoldMusicInternal();
            }

            _heldRemoteParty = _remoteParty;
            _heldCallId = _activeCallId;
            _hasHeldCall = true;
        }

        EnsureWaitingCallUserAgent();
        var waitingMedia = CreateWaitingMediaSession();
        SIPServerUserAgent serverAgent;
        lock (_sync)
        {
            serverAgent = _waitingIncomingUas
                ?? (_waitingIncomingRequest is not null
                    ? _waitingCallUserAgent!.AcceptCall(_waitingIncomingRequest)
                    : null)
                ?? throw new InvalidOperationException("Waiting call is no longer available.");

            _waitingIncomingRequest = null;
            _waitingIncomingUas = null;
            _waitingCallerNumber = null;
            _waitingCallerName = null;
        }

        var answered = await _waitingCallUserAgent!.Answer(
            serverAgent,
            waitingMedia,
            SipNatHelper.GetConnectionAddressForSdp());

        if (!answered)
        {
            lock (_sync)
            {
                _remoteParty = _heldRemoteParty;
                _activeCallId = _heldCallId;
                _heldRemoteParty = null;
                _heldCallId = null;
                _hasHeldCall = false;
                _activeCallLeg = ActiveCallLeg.Primary;
                _isOnHold = false;
                CleanupWaitingCallInternal();
            }

            throw new InvalidOperationException("Failed to answer the waiting call.");
        }

        lock (_sync)
        {
            _isOnHold = false;
            _activeSegmentStartedAt = DateTimeOffset.Now;
            _activeCallLeg = ActiveCallLeg.Waiting;
            _remoteParty = waitingNumber;
            _activeCallId = SipCallIdHelper.Normalize(
                _waitingCallUserAgent.Dialogue?.CallId ?? waitingRequest?.Header.CallId);
        }

        MarkConnected();
        SetCallState(CallState.InCall);
        await FinalizeLegSwapPlaybackAsync();
        _log.Info($"Connected to waiting caller {_remoteParty}; held party {_heldRemoteParty}");
    }

    public async Task EndAndAnswerWaitingCallAsync()
    {
        SIPUserAgent? primaryAgent;
        SIPRequest? waitingRequest;
        string? waitingNumber;
        string? endedRemoteParty;
        string? endedCallId;
        var endedWasOutbound = false;
        var endedWasConnected = false;

        lock (_sync)
        {
            primaryAgent = _userAgent;
            waitingRequest = _waitingIncomingRequest;
            waitingNumber = _waitingCallerNumber;

            if (primaryAgent is null || (waitingRequest is null && _waitingIncomingUas is null))
            {
                throw new InvalidOperationException("No waiting call to answer.");
            }

            if (CallState is not CallState.InCall and not CallState.OnHold and not CallState.CallWaitingRinging)
            {
                throw new InvalidOperationException("Call waiting is only available during an active call.");
            }

            endedRemoteParty = _remoteParty;
            endedCallId = _activeCallId;
            endedWasOutbound = _isOutboundCall;
            endedWasConnected = _wasConnected;
            StopHoldMusicInternal();
            _replacingPrimaryWithWaitingCall = true;
            _hasHeldCall = false;
            _heldRemoteParty = null;
            _heldCallId = null;
            _activeCallLeg = ActiveCallLeg.Primary;
        }

        _log.Info($"Answering waiting call from {waitingNumber} (ending current call with {endedRemoteParty})");

        primaryAgent?.Hangup();

        if (endedWasConnected && !string.IsNullOrWhiteSpace(endedRemoteParty))
        {
            CallEnded?.Invoke(
                this,
                new CallEndedEventArgs(
                    endedRemoteParty,
                    endedWasOutbound,
                    SipCallIdHelper.Normalize(endedCallId),
                    true));
        }

        EnsureWaitingCallUserAgent();
        var waitingMedia = CreateWaitingMediaSession();
        SIPServerUserAgent serverAgent;
        lock (_sync)
        {
            serverAgent = _waitingIncomingUas
                ?? (_waitingIncomingRequest is not null
                    ? _waitingCallUserAgent!.AcceptCall(_waitingIncomingRequest)
                    : null)
                ?? throw new InvalidOperationException("Waiting call is no longer available.");

            _waitingIncomingRequest = null;
            _waitingIncomingUas = null;
            _waitingCallerNumber = null;
            _waitingCallerName = null;
        }

        var answered = await _waitingCallUserAgent!.Answer(
            serverAgent,
            waitingMedia,
            SipNatHelper.GetConnectionAddressForSdp());

        if (!answered)
        {
            lock (_sync)
            {
                _replacingPrimaryWithWaitingCall = false;
                CleanupWaitingCallInternal();
            }

            ResetCallState();
            throw new InvalidOperationException("Failed to answer the waiting call.");
        }

        lock (_sync)
        {
            PromoteWaitingCallToPrimary(waitingNumber, waitingRequest);
            _replacingPrimaryWithWaitingCall = false;
        }

        SetCallState(CallState.InCall);
        await FinalizeLegSwapPlaybackAsync();
        _log.Info($"Connected to waiting caller {_remoteParty} after ending previous call");
    }

    public Task SwitchCallsAsync()
    {
        return Task.Run(async () =>
        {
            lock (_sync)
            {
                if (_userAgent is null || _waitingCallUserAgent is null || !_hasHeldCall)
                {
                    throw new InvalidOperationException("No held call to switch to.");
                }

                if (CallState is not CallState.InCall and not CallState.OnHold)
                {
                    throw new InvalidOperationException("Switch is only available during a call.");
                }

                if (_activeCallLeg == ActiveCallLeg.Waiting)
                {
                    if (_waitingCallUserAgent.IsCallActive)
                    {
                        _waitingCallUserAgent.PutOnHold();
                    }

                    _userAgent.TakeOffHold();
                }
                else
                {
                    if (_userAgent.IsCallActive)
                    {
                        _userAgent.PutOnHold();
                    }

                    _waitingCallUserAgent.TakeOffHold();
                }

                (_remoteParty, _heldRemoteParty) = (_heldRemoteParty, _remoteParty);
                (_activeCallId, _heldCallId) = (_heldCallId, _activeCallId);
                _activeCallLeg = _activeCallLeg == ActiveCallLeg.Waiting
                    ? ActiveCallLeg.Primary
                    : ActiveCallLeg.Waiting;
                _isOnHold = false;
                _activeSegmentStartedAt = DateTimeOffset.Now;
                _audioEndPoint?.SetMuted(_isMuted);
                _waitingAudioEndPoint?.SetMuted(_isMuted);
                SetCallState(CallState.InCall);
                _log.Info(
                    $"Switched active call to {_remoteParty}; other party on hold: {_heldRemoteParty}");
            }

            await FinalizeLegSwapPlaybackAsync();
        });
    }

    public async Task<long?> ProbeOptionsRttAsync()
    {
        SIPTransport? transport;
        ProvisionConfig? config;

        lock (_sync)
        {
            transport = _transport;
            config = _config;
        }

        if (transport is null || config is null || RegistrationState != SipRegistrationState.Registered)
        {
            return null;
        }

        var serverUri = SIPURI.ParseSIPURI(SipUriBuilder.BuildServerUri(config));
        var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);
        optionsRequest.Header.UserAgent = UserAgent;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await transport.SendRequestAsync(optionsRequest).WaitAsync(timeoutCts.Token);
            stopwatch.Stop();
            if (result != SocketError.Success)
            {
                return null;
            }

            TouchSipActivity();
            return stopwatch.ElapsedMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    public async Task ApplyAudioDeviceHotSwapAsync()
    {
        if (CallState is not CallState.InCall and not CallState.OnHold and not CallState.CallWaitingRinging)
        {
            return;
        }

        _log.Info("Applying in-call audio device change.");
        ApplyCallAudioLevels();

        var audioEndPoint = GetActiveAudioEndPoint();
        if (audioEndPoint is null)
        {
            return;
        }

        await audioEndPoint.Close();
        var enabled = CodecConfiguration.BuildEnabledCodecs(
            _settingsService.Settings.EnabledCodecs,
            _settingsService.Settings.VoicePreferOpus);
        var encoder = CodecConfiguration.CreateEncoder(enabled);
        var outputDevice = AudioDeviceHelper.FindOutputDeviceIndexForSip(
            _settingsService.Settings.SpeakerDevice,
            _settingsService.Settings.SpeakerDeviceId);
        var inputDevice = AudioDeviceHelper.FindInputDeviceIndexForSip(
            _settingsService.Settings.MicrophoneDevice,
            _settingsService.Settings.MicrophoneDeviceId);

        var innerAudio = CreateConfiguredAudioEndPoint(encoder, outputDevice, inputDevice);
        _audioEndPoint = new MutingAudioEndPoint(innerAudio);
        _audioEndPoint.SetMuted(_isMuted);
        if (_isSpeakerMuted)
        {
            await _audioEndPoint.SetSpeakerMuted(true);
        }

        AttachPlaybackTap();

        await EnsurePlaybackReadyAsync();
    }

    public Task ToggleRecordingAsync()
    {
        return _isRecording ? StopRecordingAsync() : StartRecordingAsync();
    }

    public async Task BlindTransferAsync(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("Transfer target is required.");
        }

        SIPUserAgent? userAgent;

        lock (_sync)
        {
            userAgent = _userAgent;
            if (userAgent is null || !userAgent.IsCallActive)
            {
                throw new InvalidOperationException("No active call.");
            }
        }

        var uri = BuildTransferUri(target.Trim());
        var result = await userAgent.BlindTransfer(uri, TimeSpan.FromSeconds(20), CancellationToken.None);
        if (!result)
        {
            throw new InvalidOperationException($"Blind transfer to {target} failed.");
        }

        _log.Info($"Blind transferred to {target}");

        lock (_sync)
        {
            ResetCallState();
        }
    }

    public Task ConferenceCallAsync()
    {
        var extension = _settingsService.Settings.ConferenceExtension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException("Set a conference extension in settings.");
        }

        return BlindTransferAsync(extension);
    }

    public Task SendDtmfAsync(char tone)
    {
        return Task.Run(() =>
        {
            lock (_sync)
            {
                if (_userAgent is null || !_userAgent.IsCallActive)
                {
                    throw new InvalidOperationException("No active call.");
                }

                if (CallState is not CallState.InCall and not CallState.OnHold)
                {
                    throw new InvalidOperationException("DTMF is only available during a call.");
                }

                _userAgent.SendDtmf(MapDtmfTone(tone));
                _log.Info($"Sent DTMF '{tone}'");
            }
        });
    }

    private static byte MapDtmfTone(char tone) =>
        tone switch
        {
            '0' => 0,
            '1' => 1,
            '2' => 2,
            '3' => 3,
            '4' => 4,
            '5' => 5,
            '6' => 6,
            '7' => 7,
            '8' => 8,
            '9' => 9,
            '*' => 10,
            '#' => 11,
            _ => throw new ArgumentException($"Unsupported DTMF tone '{tone}'.", nameof(tone))
        };

    private void EnsureCallUserAgentInitialized()
    {
        if (_transport is null || _userAgent is not null)
        {
            return;
        }

        _userAgent = new SIPUserAgent(_transport, null, false);
        WireUserAgentEvents(_userAgent);
        _log.Info(SipLogTag.Register, "Created SIP call user agent for incoming/outbound calls.");
    }

    private void WireUserAgentEvents(SIPUserAgent userAgent)
    {
        if (ReferenceEquals(_wiredPrimaryEventsAgent, userAgent))
        {
            return;
        }

        if (_wiredPrimaryEventsAgent is not null)
        {
            UnwirePrimaryUserAgentEvents(_wiredPrimaryEventsAgent);
        }

        userAgent.OnIncomingCall += OnIncomingCall;
        userAgent.OnCallHungup += OnPrimaryUserAgentCallHungup;
        userAgent.ClientCallFailed += OnPrimaryUserAgentClientCallFailed;
        userAgent.ClientCallAnswered += OnPrimaryUserAgentClientCallAnswered;
        _wiredPrimaryEventsAgent = userAgent;
    }

    private void UnwirePrimaryUserAgentEvents(SIPUserAgent userAgent)
    {
        userAgent.OnIncomingCall -= OnIncomingCall;
        userAgent.OnCallHungup -= OnPrimaryUserAgentCallHungup;
        userAgent.ClientCallFailed -= OnPrimaryUserAgentClientCallFailed;
        userAgent.ClientCallAnswered -= OnPrimaryUserAgentClientCallAnswered;
        if (ReferenceEquals(_wiredPrimaryEventsAgent, userAgent))
        {
            _wiredPrimaryEventsAgent = null;
        }
    }

    private void OnPrimaryUserAgentCallHungup(SIPDialogue? dialogue)
    {
        var endedCallId = dialogue?.CallId;
        var durationSeconds = ConnectedAt.HasValue
            ? (DateTimeOffset.Now - ConnectedAt.Value).TotalSeconds
            : 0;
        _log.Info(
            $"Call hung up after {durationSeconds:F0}s (Call-ID: {endedCallId ?? _activeCallId ?? "unknown"})");
        _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(false, "Call ended", 487));
        lock (_sync)
        {
            if (_replacingPrimaryWithWaitingCall)
            {
                _log.Info("Primary call ended to answer waiting call");
                DisposeMediaSession();
                return;
            }

            if (_legHangupIntent == LegHangupIntent.PromoteWaitingAfterPrimaryEnd
                && _hasHeldCall
                && _waitingCallUserAgent?.IsCallActive == true)
            {
                _legHangupIntent = LegHangupIntent.None;
                var heldParty = _heldRemoteParty;
                _log.Info("Primary call ended; resuming held call");
                PromoteWaitingLegAfterPrimaryEnded(heldParty);
                return;
            }

            if (TryHandleDualCallRemoteLegEnded(endedCallId))
            {
                return;
            }

            if (IsStaleCallSignaling(endedCallId))
            {
                _log.Info(
                    SipLogTag.Inbound,
                    $"Ignoring stale hangup for Call-ID: {SipCallIdHelper.Normalize(endedCallId) ?? "unknown"}");
                return;
            }

            ResetCallState();
        }
    }

    private void OnPrimaryUserAgentClientCallFailed(
        ISIPClientUserAgent _,
        string errorMessage,
        SIPResponse? response)
    {
        if (OpenSipsAuthHelper.IsAuthenticationChallenge(response))
        {
            return;
        }

        var statusCode = (int)(response?.Status ?? SIPResponseStatusCodesEnum.InternalServerError);
        var message = SipFailureMessageHelper.FormatFailureMessage(errorMessage, response);
        _log.Info(SipLogTag.Outbound, $"Outbound call failed: {message} ({statusCode})");
        _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(false, message, statusCode));
        lock (_sync)
        {
            if (CallState == CallState.Outgoing)
            {
                ResetCallState();
            }
        }
    }

    private void OnPrimaryUserAgentClientCallAnswered(ISIPClientUserAgent _, SIPResponse response)
    {
        _activeCallId = SipCallIdHelper.Normalize(_userAgent?.Dialogue?.CallId) ?? _activeCallId;
        _wasConnected = true;
        _log.Info($"Outbound call answered ({response.StatusCode})");
        _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(true, "Call connected", (int)response.Status));
        SchedulePlaybackEnsureRetry();
    }

    private void HandleOutboundInviteFailure(SIPResponse response)
    {
        if (CallState != CallState.Outgoing)
        {
            return;
        }

        if (response.Header.CSeqMethod != SIPMethodsEnum.INVITE || (int)response.Status < 400)
        {
            return;
        }

        if (OpenSipsAuthHelper.IsAuthenticationChallenge(response))
        {
            return;
        }

        var message = SipFailureMessageHelper.FormatFailureMessage(response.ReasonPhrase, response);
        _log.Info($"Outbound INVITE failed: {message} ({response.StatusCode})");
        _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(false, message, (int)response.Status));
        lock (_sync)
        {
            if (CallState == CallState.Outgoing)
            {
                ResetCallState();
            }
        }
    }

    private static string FormatFailureMessage(string errorMessage, SIPResponse? response) =>
        SipFailureMessageHelper.FormatFailureMessage(errorMessage, response);

    private int GetRegistrationExpirySeconds() =>
        RegistrationTimingHelper.ClampRegistrationExpiry(_settingsService.Settings.RegistrationExpirySeconds);

    private void AddTransportChannel(ProvisionConfig config)
    {
        if (_transport is null)
        {
            throw new InvalidOperationException("SIP transport is not initialized.");
        }

        var localEndPoint = new IPEndPoint(IPAddress.Any, 0);
        if (config.UseTcp)
        {
            _transport.AddSIPChannel(new SIPTCPChannel(localEndPoint));
            if (!string.IsNullOrWhiteSpace(config.SipConnectHost))
            {
                _log.Info($"TCP registration will connect via {config.SipConnectHost} (SIP domain {config.SipServer}).");
            }
        }
        else
        {
            _transport.AddSIPChannel(new SIPUDPChannel(localEndPoint));
        }

        TryMarkSipTransportDscp();
    }

    private void TryMarkSipTransportDscp()
    {
        if (!_enableDscpMarking || _transport is null)
        {
            return;
        }

        try
        {
            var sockets = SipSocketReflectionHelper.FindSockets(_transport);
            if (sockets.Count == 0)
            {
                var transportType = _transport.GetType();
                var prop = transportType.GetProperty("SIPChannels");
                if (prop?.GetValue(_transport) is System.Collections.IEnumerable propList)
                {
                    foreach (var channel in propList)
                    {
                        sockets = SipSocketReflectionHelper.FindSockets(channel).Concat(sockets).ToList();
                    }
                }
                else
                {
                    var method = transportType.GetMethod("GetSIPChannels");
                    if (method?.Invoke(_transport, null) is System.Collections.IEnumerable methodList)
                    {
                        foreach (var channel in methodList)
                        {
                            sockets = SipSocketReflectionHelper.FindSockets(channel).Concat(sockets).ToList();
                        }
                    }
                }
            }

            if (sockets.Count == 0)
            {
                _log.Warn("SIP DSCP: no sockets discovered on transport (non-fatal).");
                return;
            }

            foreach (var socket in sockets.Distinct())
            {
                if (DscpSocketHelper.TryMarkExpeditedForwarding(socket, out var detail))
                {
                    _log.Info($"SIP socket DSCP marked: {detail} ({socket.LocalEndPoint}).");
                }
                else
                {
                    _log.Warn($"SIP socket DSCP mark skipped: {detail}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"SIP DSCP marking failed (non-fatal): {ex.Message}");
        }
    }

    private void TryMarkMediaSessionDscp(VoIPMediaSession? session)
    {
        if (!_enableDscpMarking || session is null)
        {
            return;
        }

        try
        {
            var marked = 0;
            foreach (var socket in SipSocketReflectionHelper.FindSockets(session))
            {
                if (DscpSocketHelper.TryMarkExpeditedForwarding(socket, out var detail))
                {
                    marked++;
                    _log.Info($"RTP/RTCP socket DSCP marked: {detail} ({socket.LocalEndPoint}).");
                }
                else
                {
                    _log.Warn($"RTP/RTCP socket DSCP mark skipped: {detail}");
                }
            }

            if (marked == 0)
            {
                _log.Warn("RTP/RTCP DSCP: no sockets discovered on media session (non-fatal).");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"RTP DSCP marking failed (non-fatal): {ex.Message}");
        }
    }

    private async Task ApplyStickyPbxIpAsync(ProvisionConfig config, CancellationToken cancellationToken)
    {
        var host = config.SipServer?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        // Explicit connect host from API/settings wins; still remember it on success.
        if (!string.IsNullOrWhiteSpace(config.SipConnectHost)
            && IPAddress.TryParse(config.SipConnectHost.Trim(), out _))
        {
            return;
        }

        var cached = _stickyPbxIpCache.TryGetCachedIp(host);
        if (cached is not null)
        {
            config.SipConnectHost = cached;
            _log.Info($"Using sticky PBX IP cache {cached} for {host} (fail-open on REGISTER failure).");
            return;
        }

        var resolved = await StickyPbxIpCache.ResolveHostAsync(host, cancellationToken);
        if (resolved is not null)
        {
            config.SipConnectHost = resolved;
            _log.Info($"Resolved PBX host {host} → {resolved} (will stick after successful REGISTER).");
        }
    }

    private void RememberStickyPbxIpOnSuccess(ProvisionConfig config)
    {
        var host = config.SipServer?.Trim();
        var ip = config.SipConnectHost?.Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        if (!IPAddress.TryParse(ip, out _))
        {
            return;
        }

        _stickyPbxIpCache.RememberSuccess(host, ip);
        _ = _settingsService.SaveCarrierAsync(
            config.SipServer,
            config.Transport,
            config.SipPort,
            ip);
    }

    private void InvalidateStickyPbxIpOnFailure(ProvisionConfig config)
    {
        var host = config.SipServer?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        _stickyPbxIpCache.Invalidate(host);
        // Clear connect-host override so the next attempt re-resolves DNS.
        if (!string.IsNullOrWhiteSpace(config.SipConnectHost)
            && IPAddress.TryParse(config.SipConnectHost, out _))
        {
            config.SipConnectHost = null;
            _ = _settingsService.SaveCarrierAsync(
                config.SipServer,
                config.Transport,
                config.SipPort,
                connectHost: null);
            _log.Warn($"Cleared sticky PBX IP for {host} after REGISTER failure — next attempt will re-resolve DNS.");
        }
    }

    private static string GetRegistrarServer(ProvisionConfig config)
    {
        if (config.UseTcp && !string.IsNullOrWhiteSpace(config.SipConnectHost))
        {
            return $"{config.SipConnectHost.Trim()}:{config.SipPort};transport=tcp";
        }

        if (!config.UseTcp && !string.IsNullOrWhiteSpace(config.SipConnectHost))
        {
            return $"{config.SipConnectHost.Trim()}:{config.SipPort}";
        }

        return config.RegistrarServer;
    }

    private void StartKeepAlive()
    {
        StopKeepAlive();

        var intervalSeconds = RegistrationTimingHelper.ClampKeepAliveSeconds(_settingsService.Settings.KeepAliveSeconds);
        _keepAliveTimer = new Timer(
            _ => SendKeepAlive(),
            null,
            TimeSpan.FromSeconds(intervalSeconds),
            TimeSpan.FromSeconds(intervalSeconds));
    }

    private void StopKeepAlive()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;
    }

    private void SendKeepAlive()
    {
        if (_transport is null || _config is null || RegistrationState != SipRegistrationState.Registered)
        {
            return;
        }

        var intervalSeconds = RegistrationTimingHelper.ClampKeepAliveSeconds(_settingsService.Settings.KeepAliveSeconds);
        if (_lastSipActivityUtc is not null
            && RegistrationTimingHelper.ShouldReconnectForInactivity(
                _lastSipActivityUtc,
                _settingsService.Settings.KeepAliveSeconds))
        {
            _log.Warn("No SIP server activity detected; scheduling re-register.");
            ScheduleRegistrationReconnect("SIP activity timeout");
            return;
        }

        try
        {
            var serverUri = SIPURI.ParseSIPURI(SipUriBuilder.BuildServerUri(_config));
            var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);
            optionsRequest.Header.UserAgent = UserAgent;
            _ = _transport.SendRequestAsync(optionsRequest);
            _keepAliveSendFailures = 0;
        }
        catch (Exception ex)
        {
            _keepAliveSendFailures++;
            _log.Error($"Keep-alive failed: {ex.Message}");
            if (RegistrationTimingHelper.ShouldScheduleReconnectAfterKeepAliveFailures(_keepAliveSendFailures))
            {
                _keepAliveSendFailures = 0;
                ScheduleRegistrationReconnect("Keep-alive send failed");
            }
        }
    }

    public void EnsureCallStateConsistentWithSession()
    {
        CallStateRecoveryAction action;
        CallState staleState;

        lock (_sync)
        {
            action = CallStateConsistencyHelper.Evaluate(BuildCallStateConsistencyInput());
            staleState = CallState;
        }

        switch (action)
        {
            case CallStateRecoveryAction.ResetCallState:
                _log.Warn(
                    SipLogTag.Inbound,
                    $"Recovering stale call state {staleState} — no matching SIP session");
                ResetCallState();
                break;

            case CallStateRecoveryAction.ClearWaitingCallState:
                _log.Warn(
                    SipLogTag.Inbound,
                    "Recovering stale call-waiting state — clearing waiting call without active invite");
                lock (_sync)
                {
                    ClearWaitingCallState(restorePreviousCallState: true);
                }

                break;

            case CallStateRecoveryAction.PromoteToInCall:
                lock (_sync)
                {
                    if (CallState == CallState.Outgoing && (_userAgent?.IsCallActive ?? false))
                    {
                        _log.Info(
                            SipLogTag.Inbound,
                            "Promoting stale Outgoing state to InCall — primary SIP session is active");
                        MarkConnected();
                        SetCallState(CallState.InCall);
                    }
                }

                break;
        }
    }

    private bool IsEligibleForCallWaiting() =>
        SipIncomingCallHelper.IsEligibleForCallWaiting(CallState, _userAgent?.IsCallActive ?? false);

    private CallStateConsistencyInput BuildCallStateConsistencyInput() =>
        new(
            CallState,
            _pendingIncomingRequest is not null,
            _outboundCallCompletion is not null,
            _userAgent?.IsCallActive ?? false,
            _waitingIncomingRequest is not null || _waitingIncomingUas is not null,
            _wasConnected || !string.IsNullOrWhiteSpace(_remoteParty));

    private void OnIncomingCall(SIPUserAgent _, SIPRequest sipRequest) =>
        ProcessIncomingInvite(sipRequest);

    private void TryHandleConcurrentInviteAtTransport(SIPRequest sipRequest)
    {
        lock (_sync)
        {
            var incomingCallId = SipCallIdHelper.Normalize(sipRequest.Header.CallId);
            var pendingCallId = _pendingIncomingRequest is not null
                ? SipCallIdHelper.Normalize(_pendingIncomingRequest.Header.CallId)
                : null;

            if (!SipIncomingCallHelper.ShouldHandleConcurrentInviteAtTransport(
                    CallState,
                    _userAgent?.IsCallActive ?? false,
                    incomingCallId,
                    SipCallIdHelper.Normalize(_activeCallId),
                    pendingCallId))
            {
                return;
            }
        }

        _log.Info(
            SipLogTag.Inbound,
            $"Handling concurrent INVITE at transport layer while {CallState} (Call-ID: {SipCallIdHelper.Normalize(sipRequest.Header.CallId) ?? "unknown"})");
        ProcessIncomingInvite(sipRequest);
    }

    private void ProcessIncomingInvite(SIPRequest sipRequest)
    {
        EnsureCallStateConsistentWithSession();

        var callerNumber = sipRequest.Header.From.FromURI.User ?? "Unknown";
        var callerName = sipRequest.Header.From.FromName;
        if (string.IsNullOrWhiteSpace(callerName))
        {
            callerName = null;
        }

        var incomingCallId = SipCallIdHelper.Normalize(sipRequest.Header.CallId);

        lock (_sync)
        {
            if (IsEligibleForCallWaiting())
            {
                if (_waitingIncomingRequest is not null || _waitingIncomingUas is not null)
                {
                    var waitingCallId = _waitingIncomingRequest is not null
                        ? SipCallIdHelper.Normalize(_waitingIncomingRequest.Header.CallId)
                        : null;
                    if (SipCallIdHelper.IsRetransmittedInvite(incomingCallId, waitingCallId))
                    {
                        _log.Info(
                            SipLogTag.Inbound,
                            $"Ignoring retransmitted waiting INVITE for {callerNumber} (Call-ID: {incomingCallId})");
                        return;
                    }

                    _log.Info(SipLogTag.Inbound, $"Rejecting second waiting call from {callerNumber} — already have call waiting");
                    var busyAgent = _userAgent?.AcceptCall(sipRequest);
                    busyAgent?.Reject(SIPResponseStatusCodesEnum.BusyHere, "Busy");
                    return;
                }

                var waitingUas = EnsureWaitingCallUserAgent()
                    ? _waitingCallUserAgent!.AcceptCall(sipRequest)
                    : null;
                if (waitingUas is null)
                {
                    _log.Warn(SipLogTag.Inbound, $"Unable to accept waiting call from {callerNumber}");
                    return;
                }

                _log.Info(SipLogTag.Inbound, $"Call waiting from {callerNumber} while on active call");
                _waitingIncomingRequest = sipRequest;
                _waitingIncomingUas = waitingUas;
                _waitingCallerNumber = callerNumber;
                _waitingCallerName = callerName;
                SetCallState(CallState.CallWaitingRinging);
                var waitingArgs = new IncomingCallEventArgs(callerNumber, callerName, IsQueueCall(sipRequest));
                IncomingCallWaiting?.Invoke(this, waitingArgs);
                return;
            }

            if (CallState == CallState.Incoming
                && (SipCallIdHelper.IsRetransmittedInvite(incomingCallId, _activeCallId)
                    || (_pendingIncomingRequest is not null
                        && SipCallIdHelper.IsRetransmittedInvite(
                            incomingCallId,
                            _pendingIncomingRequest.Header.CallId))))
            {
                _log.Info(
                    SipLogTag.Inbound,
                    $"Ignoring retransmitted INVITE for {callerNumber} (Call-ID: {incomingCallId})");
                return;
            }

            if (CallState != CallState.Idle)
            {
                _log.Info(SipLogTag.Inbound, $"Rejecting call from {callerNumber} — line busy");
                var busyAgent = _userAgent?.AcceptCall(sipRequest);
                busyAgent?.Reject(SIPResponseStatusCodesEnum.BusyHere, "Busy");
                IncomingCallRejectedWhileBusy?.Invoke(
                    this,
                    new IncomingCallEventArgs(callerNumber, callerName, IsQueueCall(sipRequest)));
                return;
            }

            if (_settingsService.Settings.DndEnabled)
            {
                _log.Info(SipLogTag.Inbound, $"Rejecting call from {callerNumber} — DND enabled");
                var dndAgent = _userAgent?.AcceptCall(sipRequest);
                dndAgent?.Reject(SIPResponseStatusCodesEnum.BusyHere, "Do Not Disturb");
                return;
            }

            var forwardTarget = _settingsService.Settings.CallForwardNumber?.Trim();
            if (!string.IsNullOrWhiteSpace(forwardTarget))
            {
                _log.Info($"Forwarding call from {callerNumber} to {forwardTarget}");
                var forwardAgent = _userAgent?.AcceptCall(sipRequest);
                if (forwardAgent is not null)
                {
                    var forwardUri = BuildTransferUri(forwardTarget);
                    forwardAgent.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, forwardUri);
                }

                return;
            }

            _remoteParty = callerNumber;
            _pendingIncomingRequest = sipRequest;
            _isOutboundCall = false;
            _activeCallId = incomingCallId;
            var incomingUas = _userAgent?.AcceptCall(sipRequest);
            if (incomingUas is null)
            {
                _log.Warn(SipLogTag.Inbound, $"Unable to accept incoming call from {callerNumber}");
                _pendingIncomingRequest = null;
                _activeCallId = null;
                _remoteParty = null;
                return;
            }

            _pendingIncomingUas = incomingUas;
            IncomingStartedAt ??= DateTimeOffset.Now;
            SetCallState(CallState.Incoming);
        }

        var isQueueCall = IsQueueCall(sipRequest);
        if (_settingsService.Settings.AgentQueueModeEnabled && isQueueCall)
        {
            _log.Info($"Agent queue mode enabled — queue call from {callerName ?? callerNumber}");
        }

        _log.BeginSection("INCOMING CALL");
        _log.Info(SipLogTag.Inbound, $"Incoming call from {callerName ?? callerNumber}");
        IncomingCallLog.Marker("INVITE_RECEIVED", callerName ?? callerNumber);
        IncomingCall?.Invoke(this, new IncomingCallEventArgs(callerNumber, callerName, isQueueCall));
    }

    private void ClearWaitingCallState(bool restorePreviousCallState)
    {
        CleanupWaitingCallInternal();

        if (restorePreviousCallState)
        {
            SetCallState(_isOnHold ? CallState.OnHold : CallState.InCall);
        }
    }

    private static bool IsQueueCall(SIPRequest sipRequest) =>
        SipIncomingCallHelper.IsQueueCall(sipRequest.Header.UnknownHeaders);

    private void ResetCallState()
    {
        CallEndedEventArgs? endedArgs = null;

        lock (_sync)
        {
            if (CallState == CallState.Idle
                && !_wasConnected
                && _remoteParty is null
                && _pendingIncomingRequest is null
                && _activeCallId is null)
            {
                return;
            }

            var wasConnected = _wasConnected;
            var remoteParty = _remoteParty;
            var isOutbound = _isOutboundCall;
            var callId = SipCallIdHelper.Normalize(_activeCallId);

            var durationSeconds = (int)Math.Max(0, ActiveCallDuration.TotalSeconds);
            StopRecordingInternal();
            StopHoldMusicInternal();
            CleanupWaitingCallInternal();
            _remoteParty = null;
            _pendingIncomingRequest = null;
            _pendingIncomingUas = null;
            _isMuted = false;
            _isSpeakerMuted = false;
            _isOnHold = false;
            _wasConnected = false;
            _isOutboundCall = false;
            _activeCallId = null;
            ConnectedAt = null;
            IncomingStartedAt = null;
            _activeSegmentStartedAt = null;
            _accumulatedActiveDuration = TimeSpan.Zero;
            _heldRemoteParty = null;
            _heldCallId = null;
            _hasHeldCall = false;
            _activeCallLeg = ActiveCallLeg.Primary;
            SetCallState(CallState.Idle);

            if (!string.IsNullOrWhiteSpace(remoteParty) || !string.IsNullOrWhiteSpace(callId))
            {
                endedArgs = new CallEndedEventArgs(remoteParty, isOutbound, callId, wasConnected, durationSeconds);
            }
        }

        if (endedArgs is not null)
        {
            CallEnded?.Invoke(this, endedArgs);
        }
    }

    private void MarkConnected()
    {
        if (ConnectedAt is null)
        {
            ConnectedAt = DateTimeOffset.Now;
            _accumulatedActiveDuration = TimeSpan.Zero;
            _activeSegmentStartedAt = ConnectedAt;
        }

        _wasConnected = true;
    }

    private void AccumulateActiveDuration()
    {
        if (_activeSegmentStartedAt is null)
        {
            return;
        }

        _accumulatedActiveDuration += DateTimeOffset.Now - _activeSegmentStartedAt.Value;
        _activeSegmentStartedAt = null;
    }

    private void SetCallState(CallState state)
    {
        CallState = state;
        CallStateChanged?.Invoke(this, state);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            UnregisterInternal();
        }
    }

    private void WireTransportTracing(SIPTransport transport)
    {
        transport.SIPTransportRequestReceived += HandleInboundTransportRequestAsync;

        transport.SIPResponseInTraceEvent += (_, _, response) =>
        {
            TouchSipActivity();
            OpenSipsAuthHelper.NormalizeAuthenticationHeaders(response);
            HandleRegisterAuthResponse(response);
            HandleOutboundInviteFailure(response);
            HandleIncomingInviteTerminated(response);
            SipNatHelper.TryCapturePublicIpFromSipMessage(response.ToString(), _log);
            ApplyTransportPublicAddress();
        };

        transport.SIPRequestOutTraceEvent += (_, _, request) =>
        {
            TrackRegisterAuthRequest(request);
            _log.LogWireOut(request.StatusLine, request.ToString());
        };

        transport.SIPResponseInTraceEvent += (_, _, response) =>
        {
            _log.LogWireIn(response.ShortDescription, response.ToString());
        };

        transport.SIPRequestInTraceEvent += (_, _, request) =>
        {
            TouchSipActivity();

            if (request.Method == SIPMethodsEnum.INVITE)
            {
                TryHandleConcurrentInviteAtTransport(request);
            }

            if (request.Method == SIPMethodsEnum.CANCEL)
            {
                HandleRemoteCallCancellation(request.Header.CallId, "Remote party cancelled call");
                return;
            }

            if (request.Method == SIPMethodsEnum.BYE)
            {
                var durationSeconds = ConnectedAt.HasValue
                    ? (DateTimeOffset.Now - ConnectedAt.Value).TotalSeconds
                    : 0;
                var reason = request.Header.Reason
                    ?? request.Header.UnknownHeaders.FirstOrDefault(header =>
                        header.StartsWith("Reason:", StringComparison.OrdinalIgnoreCase))
                    ?? "unspecified";
                _log.Info(
                    $"BYE received after {durationSeconds:F0}s (Call-ID: {request.Header.CallId}, reason: {reason})");

                lock (_sync)
                {
                    if (CallState is CallState.InCall or CallState.OnHold)
                    {
                        if (TryHandleDualCallRemoteLegEnded(request.Header.CallId))
                        {
                            return;
                        }

                        if (IsStaleCallSignaling(request.Header.CallId))
                        {
                            _log.Info(
                                $"Ignoring stale BYE for Call-ID: {SipCallIdHelper.Normalize(request.Header.CallId) ?? "unknown"}");
                            return;
                        }

                        _outboundCallCompletion?.TrySetResult(new OutboundCallOutcome(false, "Call ended", 487));
                        ResetCallState();
                    }
                }
            }
        };

        transport.EnableTraceLogs();
    }

    /// <summary>
    /// Answers out-of-dialog OPTIONS (PBX qualify probes) with 200 OK.
    /// Must not block the SIPSorcery request-dispatch thread.
    /// </summary>
    private Task HandleInboundTransportRequestAsync(
        SIPEndPoint localEndPoint,
        SIPEndPoint remoteEndPoint,
        SIPRequest request)
    {
        var hasToTag = !string.IsNullOrWhiteSpace(request.Header.To?.ToTag);
        if (!SipOptionsProbeHelper.ShouldAnswerOutOfDialogOptions(
                request.Method == SIPMethodsEnum.OPTIONS,
                hasToTag))
        {
            return Task.CompletedTask;
        }

        var transport = _transport;
        if (transport is null)
        {
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var response = SIPResponse.GetResponse(request, SIPResponseStatusCodesEnum.Ok, null);
                response.Header.Allow = SipOptionsProbeHelper.AllowedMethods;
                _log.Info(
                    SipLogTag.Network,
                    $"Inbound OPTIONS {localEndPoint}<-{remoteEndPoint} → 200 OK (Allow: {SipOptionsProbeHelper.AllowedMethods})");
                await transport.SendResponseAsync(response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn(SipLogTag.Network, $"Failed to answer inbound OPTIONS from {remoteEndPoint}: {ex.Message}");
            }
        });

        return Task.CompletedTask;
    }

    private void TouchSipActivity()
    {
        if (RegistrationState == SipRegistrationState.Registered)
        {
            _lastSipActivityUtc = DateTimeOffset.UtcNow;
            _keepAliveSendFailures = 0;
        }
    }

    private void HandleRemoteCallCancellation(string? callId, string reason)
    {
        lock (_sync)
        {
            var normalized = SipCallIdHelper.Normalize(callId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (CallState == CallState.Incoming)
            {
                var pendingId = _pendingIncomingRequest is not null
                    ? SipCallIdHelper.Normalize(_pendingIncomingRequest.Header.CallId)
                    : SipCallIdHelper.Normalize(_activeCallId);

                if (!string.IsNullOrWhiteSpace(pendingId) && normalized == pendingId)
                {
                    _log.Info($"{reason} (Call-ID: {normalized})");
                    _pendingIncomingRequest = null;
                    ResetCallState();
                }

                return;
            }

            if (CallState == CallState.CallWaitingRinging)
            {
                var waitingId = _waitingIncomingRequest is not null
                    ? SipCallIdHelper.Normalize(_waitingIncomingRequest.Header.CallId)
                    : null;

                if (!string.IsNullOrWhiteSpace(waitingId) && normalized == waitingId)
                {
                    _log.Info($"{reason} for waiting call (Call-ID: {normalized})");
                    ClearWaitingCallState(restorePreviousCallState: true);
                }
            }
        }
    }

    private void HandleIncomingInviteTerminated(SIPResponse response)
    {
        if (response.Header.CSeqMethod != SIPMethodsEnum.INVITE)
        {
            return;
        }

        if (response.Status is not SIPResponseStatusCodesEnum.RequestTerminated
            and not SIPResponseStatusCodesEnum.TemporarilyUnavailable
            and not SIPResponseStatusCodesEnum.Decline)
        {
            return;
        }

        lock (_sync)
        {
            if (CallState == CallState.Incoming && _pendingIncomingRequest is not null)
            {
                var responseCallId = SipCallIdHelper.Normalize(response.Header.CallId);
                var pendingCallId = SipCallIdHelper.Normalize(_pendingIncomingRequest.Header.CallId);
                if (!string.IsNullOrWhiteSpace(responseCallId)
                    && !string.IsNullOrWhiteSpace(pendingCallId)
                    && responseCallId == pendingCallId)
                {
                    _log.Info($"Incoming call ended by remote party ({response.StatusCode})");
                    _pendingIncomingRequest = null;
                    ResetCallState();
                }

                return;
            }

            if (CallState == CallState.CallWaitingRinging && _waitingIncomingRequest is not null)
            {
                var responseCallId = SipCallIdHelper.Normalize(response.Header.CallId);
                var waitingCallId = SipCallIdHelper.Normalize(_waitingIncomingRequest.Header.CallId);
                if (!string.IsNullOrWhiteSpace(responseCallId)
                    && !string.IsNullOrWhiteSpace(waitingCallId)
                    && responseCallId == waitingCallId)
                {
                    _log.Info($"Waiting call ended by remote party ({response.StatusCode})");
                    ClearWaitingCallState(restorePreviousCallState: true);
                }
            }
        }
    }

    private async Task PreconnectRegistrarAsync(ProvisionConfig config)
    {
        if (_transport is null || !config.UseTcp)
        {
            return;
        }

        try
        {
            var serverUri = SIPURI.ParseSIPURI(SipUriBuilder.BuildServerUri(config));
            var optionsRequest = SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS, serverUri);
            optionsRequest.Header.UserAgent = UserAgent;

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            _log.Info($"Pre-connecting TCP to registrar {serverUri}");
            await _transport.SendRequestAsync(optionsRequest).WaitAsync(timeoutCts.Token);
            TouchSipActivity();
        }
        catch (OperationCanceledException)
        {
            _log.Info("Registrar pre-connect timed out after 3 seconds; proceeding with REGISTER.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Registrar pre-connect failed: {ex.Message}");
        }
    }

    private async Task WarmUpPublicIpAsync()
    {
        if (SipNatHelper.CachedPublicIp is not null)
        {
            ApplyTransportPublicAddress();
            return;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await SipNatHelper.ResolvePublicIpAsync(_log, timeoutCts.Token);
            ApplyTransportPublicAddress();
        }
        catch (OperationCanceledException)
        {
            _log.Info("Public IP discovery skipped after 2 seconds; REGISTER will learn Via address.");
        }
        catch (Exception ex)
        {
            _log.Warn($"Public IP discovery failed: {ex.Message}");
        }
    }

    private void ApplyTransportPublicAddress()
    {
        if (_transport is null)
        {
            return;
        }

        var publicIp = SipNatHelper.CachedPublicIp;
        if (publicIp is not null)
        {
            _transport.ContactHost = publicIp.ToString();
        }
    }

    private SIPRequest AdjustRegisterRequest(SIPRequest request, ProvisionConfig config)
    {
        request.Header.UserAgent = UserAgent;

        if (config.UseTcp && request.Header.Contact is { Count: > 0 })
        {
            foreach (var contact in request.Header.Contact)
            {
                contact.ContactURI.Protocol = SIPProtocolsEnum.tcp;
            }
        }

        var cachedAuth = _registrationAuthCache.TryGet(config.Extension);
        if (SipRegistrationAuthHelper.TryApplyPreemptiveAuth(request, config.Extension, config.Password, cachedAuth))
        {
            _lastRegisterAuthSent = SipRegistrationAuthHelper.CaptureFromAuthenticatedRequest(request, config.Extension);
            _log.Info(
                $"REGISTER preemptive Authorization applied for extension {config.Extension} (realm {cachedAuth?.Realm}).");
        }

        return request;
    }

    private void HandleRegisterAuthResponse(SIPResponse response)
    {
        if (_config is null || response.Header.CSeqMethod != SIPMethodsEnum.REGISTER)
        {
            return;
        }

        if (OpenSipsAuthHelper.IsAuthenticationChallenge(response))
        {
            var challenge = SipRegistrationAuthHelper.CaptureFromChallenge(response, _config.Extension);
            if (challenge is not null)
            {
                _registrationAuthCache.Save(challenge);
                _log.Info($"Cached REGISTER digest challenge for extension {_config.Extension}.");
            }

            return;
        }

        if (SipRegistrationAuthHelper.IsRegisterForbidden(response))
        {
            _registrationAuthCache.Clear(_config.Extension);
            _log.Warn($"Cleared cached REGISTER digest for extension {_config.Extension} after 403 Forbidden.");
        }
    }

    private void TrackRegisterAuthRequest(SIPRequest request)
    {
        if (_config is null || request.Method != SIPMethodsEnum.REGISTER)
        {
            return;
        }

        _lastRegisterAuthSent = SipRegistrationAuthHelper.CaptureFromAuthenticatedRequest(request, _config.Extension)
            ?? _lastRegisterAuthSent;
    }

    private void PersistRegistrationAuthCache()
    {
        if (_config is null)
        {
            return;
        }

        var entry = _lastRegisterAuthSent ?? _registrationAuthCache.TryGet(_config.Extension);
        if (entry is not null)
        {
            _registrationAuthCache.Save(entry);
        }
    }

    private void EnsureSipLogging()
    {
        if (_sipLoggingConfigured)
        {
            return;
        }

        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new SipLogProvider(_log));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        SIPSorcery.LogFactory.Set(factory);
        _sipLoggingConfigured = true;
    }

    private static async Task WaitForRegistrationAsync(
        Task registrationTask,
        CancellationToken cancellationToken,
        TimeSpan timeoutDuration)
    {
        var timeout = Task.Delay(timeoutDuration, cancellationToken);
        var completed = await Task.WhenAny(registrationTask, timeout);

        if (completed == timeout)
        {
            throw new TimeoutException(
                $"SIP registration timed out after {timeoutDuration.TotalSeconds:F0} seconds.");
        }

        await registrationTask;
    }

    private bool _loggedFirstIncomingRtp;
    private bool _loggedFirstPlaybackFrame;

    private VoIPMediaSession CreateMediaSession()
    {
        DisposeMediaSession();

        var enabled = CodecConfiguration.BuildEnabledCodecs(
            _settingsService.Settings.EnabledCodecs,
            _settingsService.Settings.VoicePreferOpus);
        var encoder = CodecConfiguration.CreateEncoder(enabled);
        var outputDevice = AudioDeviceHelper.FindOutputDeviceIndexForSip(
            _settingsService.Settings.SpeakerDevice,
            _settingsService.Settings.SpeakerDeviceId);
        var inputDevice = AudioDeviceHelper.FindInputDeviceIndexForSip(
            _settingsService.Settings.MicrophoneDevice,
            _settingsService.Settings.MicrophoneDeviceId);
        _log.Info(
            $"Creating media session: WinMM output={(outputDevice < 0 ? "default (-1)" : outputDevice.ToString())}, " +
            $"input={(inputDevice < 0 ? "default (-1)" : inputDevice.ToString())}, " +
            $"codecs=[{string.Join(", ", enabled)}], " +
            $"voiceProfile={_settingsService.Settings.VoiceQualityProfile}.");

        _callQualityMonitor.Reset();
        var innerAudio = CreateConfiguredAudioEndPoint(encoder, outputDevice, inputDevice);
        _audioEndPoint = new MutingAudioEndPoint(innerAudio);
        _audioEndPoint.OnAudioSinkError += message => _log.Warn($"Audio playback: {message}");
        AttachPlaybackTap();
        if (_isMuted)
        {
            _audioEndPoint.SetMuted(true);
        }

        var allowedPayloadIds = CodecConfiguration.GetNegotiableRtpPayloadIds(enabled);
        _audioEndPoint.RestrictFormats(format => CodecConfiguration.IsFormatAllowed(format, allowedPayloadIds));

        ApplyCallAudioLevels();

        _loggedFirstIncomingRtp = false;
        _loggedFirstPlaybackFrame = false;
        _lastRtpUtc = null;
        _localMediaRecoveryAttemptsSinceRtp = 0;
        _mediaSession = new CallAnalogVoIPMediaSession(
            _audioEndPoint.ToMediaEndPoints(),
            SipNatHelper.CachedPublicIp);
        _mediaSession.OnAudioFrameReceived += frame =>
        {
            _lastRtpUtc = DateTimeOffset.UtcNow;
            _localMediaRecoveryAttemptsSinceRtp = 0;

            if (!_loggedFirstIncomingRtp)
            {
                _loggedFirstIncomingRtp = true;
                _log.Info(
                    $"First RTP audio frame received ({frame.EncodedAudio.Length} bytes, {frame.AudioFormat.FormatName}, PT {frame.AudioFormat.FormatID}).");
                // RTP sockets exist after the first media exchange — mark DSCP now.
                TryMarkMediaSessionDscp(_mediaSession);
                StartMediaRecoveryMonitor();
            }

            if (!_loggedFirstPlaybackFrame)
            {
                _loggedFirstPlaybackFrame = true;
                _log.Info(
                    $"First playback frame queued ({frame.EncodedAudio.Length} bytes, {frame.AudioFormat.FormatName}).");
            }
        };
        return _mediaSession;
    }

    private void StartMediaRecoveryMonitor()
    {
        if (!_enableMidCallMediaRecovery)
        {
            return;
        }

        _mediaRecoveryTimer?.Dispose();
        _mediaRecoveryTimer = new Timer(
            _ => CheckMidCallMediaRecovery(),
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
        _log.Info("Mid-call media recovery monitor started (feature flag on).");
    }

    private void StopMediaRecoveryMonitor()
    {
        _mediaRecoveryTimer?.Dispose();
        _mediaRecoveryTimer = null;
    }

    private void CheckMidCallMediaRecovery()
    {
        try
        {
            if (CallState is not CallState.InCall || _isOnHold)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (!MidCallMediaRecoveryHelper.ShouldAttemptLocalRecovery(
                    _enableMidCallMediaRecovery,
                    _lastRtpUtc,
                    now,
                    _lastMediaRecoveryUtc))
            {
                return;
            }

            _lastMediaRecoveryUtc = now;
            _localMediaRecoveryAttemptsSinceRtp++;
            _log.Warn(
                $"Mid-call media recovery: no RTP for {MidCallMediaRecoveryHelper.DefaultNoRtpThreshold.TotalSeconds:0}s — restarting local audio sink (attempt {_localMediaRecoveryAttemptsSinceRtp}).");

            _ = EnsureActiveLegPlaybackReadyAsync();

            if (MidCallMediaRecoveryHelper.ShouldAttemptSipReinvite(
                    _enableMidCallMediaRecovery,
                    _enableSipReinviteRecovery,
                    _localMediaRecoveryAttemptsSinceRtp))
            {
                _ = TrySipMediaRefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"Mid-call media recovery check failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Best-effort media refresh via hold/resume re-INVITE cycle. Feature-flagged and off by default.
    /// </summary>
    private async Task TrySipMediaRefreshAsync()
    {
        try
        {
            var agent = _activeCallLeg == ActiveCallLeg.Waiting ? _waitingCallUserAgent : _userAgent;
            if (agent is null || !agent.IsCallActive || _isOnHold)
            {
                return;
            }

            _log.Warn("Mid-call media recovery: attempting SIP media refresh (hold/resume re-INVITE).");
            agent.PutOnHold();
            await Task.Delay(400);
            if (CallState is CallState.InCall or CallState.OnHold)
            {
                agent.TakeOffHold();
                await EnsureActiveLegPlaybackReadyAsync();
                _log.Info("Mid-call media recovery: SIP media refresh completed.");
            }
        }
        catch (Exception ex)
        {
            _log.Warn($"SIP media refresh failed (non-fatal): {ex.Message}");
        }
    }

    public void SetCallOutputVolume(double volume)
    {
        _settingsService.Settings.OutputVolume = volume;
        CallAudioMeterService.SetOutputVolume(
            _settingsService.Settings.SpeakerDevice,
            volume,
            _settingsService.Settings.SpeakerDeviceId);
    }

    private async Task EnsurePlaybackReadyAsync()
    {
        if (_audioEndPoint is null)
        {
            return;
        }

        await _audioEndPoint.Start();

        if (_isSpeakerMuted)
        {
            await _audioEndPoint.SetSpeakerMuted(true);
            _log.Info("Audio playback sink paused (speaker muted).");
        }
        else
        {
            await _audioEndPoint.SetSpeakerMuted(false);
            // ResumeAudioSink always calls WaveOut.Play(); StartAudioSink alone may no-op if already started.
            await _audioEndPoint.Inner.StartAudioSink();
            await _audioEndPoint.Inner.ResumeAudioSink();
            _log.Info("Audio playback sink started (WinMM).");
        }

        SchedulePlaybackEnsureRetry();
    }

    private void SchedulePlaybackEnsureRetry()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500);
                if (_audioEndPoint is null || CallState is not (CallState.InCall or CallState.OnHold) || _isSpeakerMuted)
                {
                    return;
                }

                await _audioEndPoint.Inner.ResumeAudioSink();
                _log.Info("Audio playback sink re-armed after connect delay.");
            }
            catch
            {
                // Best-effort after SDP renegotiation recreates WaveOut.
            }
        });
    }

    private void ApplyCallAudioLevels()
    {
        var settings = _settingsService.Settings;
        AudioDeviceHelper.SetCaptureVolume(
            settings.MicrophoneDevice,
            settings.MicrophoneDeviceId,
            settings.InputVolume);
        CallAudioMeterService.SetOutputVolume(
            settings.SpeakerDevice,
            settings.OutputVolume,
            settings.SpeakerDeviceId);
    }

    private MutingAudioEndPoint? GetActiveAudioEndPoint()
    {
        if (_hasHeldCall && _activeCallLeg == ActiveCallLeg.Waiting)
        {
            return _waitingAudioEndPoint ?? _audioEndPoint;
        }

        return _audioEndPoint;
    }

    private bool IsStaleCallSignaling(string? signalingCallId)
    {
        var pendingCallId = _pendingIncomingRequest?.Header.CallId;
        return SipCallIdHelper.IsStaleCallSignaling(
            signalingCallId,
            _activeCallId,
            pendingCallId,
            CallState);
    }

    private static bool MatchesTrackedCallId(string? callId, string? trackedId)
    {
        var normalizedCallId = SipCallIdHelper.Normalize(callId);
        var normalizedTrackedId = SipCallIdHelper.Normalize(trackedId);
        return !string.IsNullOrWhiteSpace(normalizedCallId)
            && !string.IsNullOrWhiteSpace(normalizedTrackedId)
            && string.Equals(normalizedCallId, normalizedTrackedId, StringComparison.Ordinal);
    }

    private bool TryHandleDualCallRemoteLegEnded(string? endedCallId)
    {
        if (!_hasHeldCall || CallState is not CallState.InCall and not CallState.OnHold)
        {
            return false;
        }

        if (MatchesTrackedCallId(endedCallId, _activeCallId))
        {
            HandleActiveLegRemoteEnded();
            return true;
        }

        if (MatchesTrackedCallId(endedCallId, _heldCallId))
        {
            HandleHeldLegRemoteEnded();
            return true;
        }

        return false;
    }

    private void HandleActiveLegRemoteEnded()
    {
        _legHangupIntent = LegHangupIntent.None;
        var endedParty = _remoteParty;

        if (_activeCallLeg == ActiveCallLeg.Waiting)
        {
            _log.Info(
                SipLogTag.Inbound,
                $"Active call party {endedParty} disconnected; resuming held call {_heldRemoteParty}");
            ResumePrimaryLegAfterOtherEnded();
            return;
        }

        _log.Info(
            SipLogTag.Inbound,
            $"Active call party {endedParty} disconnected; resuming held call {_heldRemoteParty}");
        PromoteWaitingLegAfterPrimaryEnded(_heldRemoteParty);
    }

    private void HandleHeldLegRemoteEnded()
    {
        _log.Info(SipLogTag.Inbound, $"Held call party {_heldRemoteParty} disconnected");
        _heldRemoteParty = null;
        _heldCallId = null;
        _hasHeldCall = false;
        _legHangupIntent = LegHangupIntent.None;

        if (_activeCallLeg == ActiveCallLeg.Waiting)
        {
            DisposeMediaSession();
            _userAgent = null;
            return;
        }

        if (_waitingCallUserAgent is not null)
        {
            UnwireWaitingCallUserAgentEvents(_waitingCallUserAgent);
        }

        DisposeWaitingMediaSession();
        _waitingCallUserAgent = null;
        SetCallState(_isOnHold ? CallState.OnHold : CallState.InCall);
    }

    private void ResumePrimaryLegAfterOtherEnded()
    {
        DisposeWaitingMediaSession();
        if (_waitingCallUserAgent is not null)
        {
            UnwireWaitingCallUserAgentEvents(_waitingCallUserAgent);
            _waitingCallUserAgent = null;
        }

        _userAgent?.TakeOffHold();
        _remoteParty = _heldRemoteParty;
        _activeCallId = _heldCallId;
        _heldRemoteParty = null;
        _heldCallId = null;
        _hasHeldCall = false;
        _activeCallLeg = ActiveCallLeg.Primary;
        _isOnHold = false;
        _activeSegmentStartedAt = DateTimeOffset.Now;
        SetCallState(CallState.InCall);
        _log.Info($"Resumed active call with {_remoteParty}");
        _ = FinalizeLegSwapPlaybackAsync();
    }

    private void PromoteWaitingLegAfterPrimaryEnded(string? heldParty)
    {
        DisposeMediaSession();
        _userAgent = null;
        _waitingCallUserAgent?.TakeOffHold();
        PromoteWaitingCallToPrimary(heldParty, null);
        SetCallState(CallState.InCall);
        _log.Info($"Resumed active call with {_remoteParty}");
        _ = FinalizeLegSwapPlaybackAsync();
    }

    private bool EnsureWaitingCallUserAgent()
    {
        if (_waitingCallUserAgent is not null)
        {
            return true;
        }

        if (_transport is null)
        {
            return false;
        }

        _waitingCallUserAgent = new SIPUserAgent(_transport, null, false);
        WireWaitingCallUserAgentEvents(_waitingCallUserAgent);
        return true;
    }

    private void WireWaitingCallUserAgentEvents(SIPUserAgent waitingAgent)
    {
        if (ReferenceEquals(_wiredWaitingEventsAgent, waitingAgent))
        {
            return;
        }

        if (_wiredWaitingEventsAgent is not null)
        {
            UnwireWaitingCallUserAgentEvents(_wiredWaitingEventsAgent);
        }

        waitingAgent.OnCallHungup += OnWaitingUserAgentCallHungup;
        _wiredWaitingEventsAgent = waitingAgent;
    }

    private void UnwireWaitingCallUserAgentEvents(SIPUserAgent waitingAgent)
    {
        waitingAgent.OnCallHungup -= OnWaitingUserAgentCallHungup;
        if (ReferenceEquals(_wiredWaitingEventsAgent, waitingAgent))
        {
            _wiredWaitingEventsAgent = null;
        }
    }

    private void OnWaitingUserAgentCallHungup(SIPDialogue? dialogue)
    {
        lock (_sync)
        {
            if (_waitingCallUserAgent is null)
            {
                return;
            }

            _log.Info("Waiting call leg ended");

            if (_legHangupIntent == LegHangupIntent.ResumePrimaryAfterWaitingEnd)
            {
                _legHangupIntent = LegHangupIntent.None;
                ResumePrimaryLegAfterOtherEnded();
                return;
            }

            if (TryHandleDualCallRemoteLegEnded(dialogue?.CallId))
            {
                return;
            }

            CleanupWaitingCallInternal();
        }
    }

    private VoIPMediaSession CreateWaitingMediaSession()
    {
        DisposeWaitingMediaSession();

        var enabled = CodecConfiguration.BuildEnabledCodecs(
            _settingsService.Settings.EnabledCodecs,
            _settingsService.Settings.VoicePreferOpus);
        var encoder = CodecConfiguration.CreateEncoder(enabled);
        var outputDevice = AudioDeviceHelper.FindOutputDeviceIndexForSip(
            _settingsService.Settings.SpeakerDevice,
            _settingsService.Settings.SpeakerDeviceId);
        var inputDevice = AudioDeviceHelper.FindInputDeviceIndexForSip(
            _settingsService.Settings.MicrophoneDevice,
            _settingsService.Settings.MicrophoneDeviceId);
        _log.Info(
            $"Creating waiting-call media session: WinMM output={(outputDevice < 0 ? "default (-1)" : outputDevice.ToString())}, " +
            $"input={(inputDevice < 0 ? "default (-1)" : inputDevice.ToString())}.");

        var innerAudio = CreateConfiguredAudioEndPoint(encoder, outputDevice, inputDevice);
        _waitingAudioEndPoint = new MutingAudioEndPoint(innerAudio);
        _waitingAudioEndPoint.OnAudioSinkError += message => _log.Warn($"Waiting-call audio playback: {message}");
        if (_isMuted)
        {
            _waitingAudioEndPoint.SetMuted(true);
        }

        var allowedPayloadIds = CodecConfiguration.GetNegotiableRtpPayloadIds(enabled);
        _waitingAudioEndPoint.RestrictFormats(format => CodecConfiguration.IsFormatAllowed(format, allowedPayloadIds));
        ApplyCallAudioLevels();

        _waitingMediaSession = new CallAnalogVoIPMediaSession(
            _waitingAudioEndPoint.ToMediaEndPoints(),
            SipNatHelper.CachedPublicIp);
        return _waitingMediaSession;
    }

    private void DisposeWaitingMediaSession()
    {
        _waitingMediaSession?.Close("waiting call ended");
        _waitingMediaSession = null;

        if (_waitingAudioEndPoint is not null)
        {
            if (_waitingAudioEndPoint.IsSpeakerMuted)
            {
                _ = _waitingAudioEndPoint.SetSpeakerMuted(false);
            }

            _ = _waitingAudioEndPoint.Close();
            _waitingAudioEndPoint = null;
        }
    }

    private void PromoteWaitingCallToPrimary(string? remoteParty, SIPRequest? waitingRequest)
    {
        if (_waitingCallUserAgent is not null)
        {
            UnwireWaitingCallUserAgentEvents(_waitingCallUserAgent);
        }

        _userAgent = _waitingCallUserAgent;
        _waitingCallUserAgent = null;
        _mediaSession = _waitingMediaSession;
        _waitingMediaSession = null;
        _audioEndPoint = _waitingAudioEndPoint;
        _waitingAudioEndPoint = null;

        WireUserAgentEvents(_userAgent!);

        _remoteParty = remoteParty;
        _activeCallId = SipCallIdHelper.Normalize(_userAgent!.Dialogue?.CallId ?? waitingRequest?.Header.CallId);
        _activeCallLeg = ActiveCallLeg.Primary;
        _hasHeldCall = false;
        _heldRemoteParty = null;
        _heldCallId = null;
        _isOnHold = false;
        _isOutboundCall = false;
        ConnectedAt = DateTimeOffset.Now;
        _accumulatedActiveDuration = TimeSpan.Zero;
        _activeSegmentStartedAt = ConnectedAt;
        _wasConnected = true;
    }

    private void CleanupWaitingCallInternal()
    {
        _waitingIncomingRequest = null;
        _waitingIncomingUas = null;
        _waitingCallerNumber = null;
        _waitingCallerName = null;
        DisposeWaitingMediaSession();
        _waitingCallUserAgent = null;
    }

    private bool TryHangupActiveLegAndResumeHeld()
    {
        if (_activeCallLeg == ActiveCallLeg.Waiting)
        {
            _legHangupIntent = LegHangupIntent.ResumePrimaryAfterWaitingEnd;
            _waitingCallUserAgent?.Hangup();
            return true;
        }

        if (_activeCallLeg == ActiveCallLeg.Primary)
        {
            var heldParty = _heldRemoteParty;
            var waitingAgent = _waitingCallUserAgent;
            if (waitingAgent is null || string.IsNullOrWhiteSpace(heldParty))
            {
                return false;
            }

            StopHoldMusicInternal();
            _legHangupIntent = LegHangupIntent.PromoteWaitingAfterPrimaryEnd;
            _userAgent?.Hangup();
            return true;
        }

        return false;
    }

    private void MuteCallAudioForHoldMusic()
    {
        if (_holdMusicMutedCallSpeaker)
        {
            return;
        }

        var endpoint = GetActiveAudioEndPoint();
        if (endpoint is null)
        {
            return;
        }

        _holdMusicMutedCallSpeaker = true;
        endpoint.Inner.ClearPlaybackBuffer();
        endpoint.Inner.SuspendPlaybackForExclusiveAudio();
        _log.Info("Call playback suspended so hold music can use the output device exclusively.");
    }

    private void RestoreCallAudioAfterHoldMusic()
    {
        if (!_holdMusicMutedCallSpeaker)
        {
            return;
        }

        _holdMusicMutedCallSpeaker = false;
        if (_isSpeakerMuted)
        {
            return;
        }

        var endpoint = GetActiveAudioEndPoint();
        if (endpoint is not null)
        {
            endpoint.Inner.ClearPlaybackBuffer();
            endpoint.Inner.ReinitializePlayback();
            _ = endpoint.Inner.StartAudioSink();
            _ = endpoint.Inner.ResumeAudioSink();
            _log.Info("Call playback reinitialized after hold music stopped.");
        }
    }

    private async Task FinalizeLegSwapPlaybackAsync()
    {
        StopHoldMusicInternal();
        ApplyCallAudioLevels();
        AttachPlaybackTap();
        _loggedFirstIncomingRtp = false;
        _loggedFirstPlaybackFrame = false;

        var endpoint = GetActiveAudioEndPoint();
        if (endpoint?.Inner is CallAnalogWindowsAudioEndPoint inner)
        {
            inner.ClearPlaybackBuffer();
            inner.ReinitializePlayback();
        }

        await EnsureActiveLegPlaybackReadyAsync();
        SchedulePlaybackEnsureRetry();
    }

    private async Task EnsureActiveLegPlaybackReadyAsync()
    {
        if (_hasHeldCall && _activeCallLeg == ActiveCallLeg.Waiting)
        {
            await EnsureWaitingPlaybackReadyAsync();
            return;
        }

        await EnsurePlaybackReadyAsync();
    }

    private async Task EnsureWaitingPlaybackReadyAsync()
    {
        if (_waitingAudioEndPoint is null)
        {
            return;
        }

        await _waitingAudioEndPoint.Start();

        if (_isSpeakerMuted)
        {
            await _waitingAudioEndPoint.SetSpeakerMuted(true);
        }
        else
        {
            await _waitingAudioEndPoint.SetSpeakerMuted(false);
            await _waitingAudioEndPoint.Inner.StartAudioSink();
            await _waitingAudioEndPoint.Inner.ResumeAudioSink();
        }
    }

    private void LogRegistrationFailure(ProvisionConfig config, string detail)
    {
        _log.Error(SipLogTag.Register, $"Registration failed: {detail}");
        if (detail.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            _log.CustomerError(
                SipLogTag.Register,
                "Registration timed out — no response from the PBX registrar.",
                $"Allow outbound {config.Transport.ToUpperInvariant()} port {config.SipPort} through your firewall, then sign in again.");
            return;
        }

        if (detail.Contains("403", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            _log.CustomerError(
                SipLogTag.Register,
                "Registration rejected — invalid credentials or extension not allowed.",
                "Verify extension and password with your administrator, then sign in again.");
            return;
        }

        _log.CustomerError(
            SipLogTag.Register,
            "Could not register this extension with the PBX.",
            "Sign out and sign in again. If the problem continues, export diagnostics and contact CallAnalog support.");
    }

    private void ScheduleRegistrationReconnect(string reason)
    {
        if (_config is null || _registrationAgent is null)
        {
            return;
        }

        _reconnectAttempt++;
        var delaySeconds = RegistrationTimingHelper.GetReconnectDelaySeconds(_reconnectAttempt, reason);
        SetRegistrationState(SipRegistrationState.Reconnecting);
        _log.Warn(SipLogTag.Network, $"Scheduling re-register in {delaySeconds}s ({reason}).");

        _reconnectTimer?.Dispose();
        _reconnectTimer = new Timer(
            _ => TryRestartRegistration(),
            null,
            TimeSpan.FromSeconds(delaySeconds),
            Timeout.InfiniteTimeSpan);
    }

    private void TryRestartRegistration()
    {
        lock (_sync)
        {
            if (_config is null || _registrationAgent is null)
            {
                return;
            }

            try
            {
                SetRegistrationState(SipRegistrationState.Registering);
                _log.Info(SipLogTag.Register, "Attempting SIP re-registration...");
                _registrationAgent.Stop();
                _registrationAgent.Start();
            }
            catch (Exception ex)
            {
                _log.Error(SipLogTag.Register, $"Re-register attempt failed: {ex.Message}");
                ScheduleRegistrationReconnect(ex.Message);
            }
        }
    }

    private void PlayHoldMusicInternal()
    {
        var path = _settingsService.Settings.HoldMusicPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _log.Info("Hold music file not configured.");
            return;
        }

        StopHoldMusicInternal();
        MuteCallAudioForHoldMusic();

        try
        {
            _holdMusicReader = CallAnalog.Softphone.Helpers.AudioFilePlaybackHelper.OpenAudioFile(path);
            _holdMusicPlayer = WinMmPlaybackHelper.CreateWaveOutOutput(
                WinMmAudioOutputManager.OwnerHoldMusic,
                _holdMusicReader,
                _settingsService.Settings.SpeakerDevice,
                _settingsService.Settings.SpeakerDeviceId);
            _holdMusicStoppedHandler = (_, _) =>
            {
                if (!_isOnHold || _holdMusicReader is null || _holdMusicPlayer is null)
                {
                    return;
                }

                try
                {
                    _holdMusicReader.Position = 0;
                    _holdMusicPlayer.Play();
                }
                catch (Exception ex)
                {
                    _log.Warn($"Hold music loop stopped: {ex.Message}");
                }
            };
            _holdMusicPlayer.PlaybackStopped += _holdMusicStoppedHandler;
            _holdMusicPlayer.Play();
        }
        catch (Exception ex)
        {
            _log.Error($"Hold music error: {ex.Message}");
        }
    }

    private void AttachPlaybackTap()
    {
        if (_audioEndPoint?.Inner is not CallAnalogWindowsAudioEndPoint inner)
        {
            return;
        }

        if (_playbackTapHandler is not null)
        {
            inner.PlaybackPcmAvailable -= _playbackTapHandler;
        }

        if (_captureTapHandler is not null)
        {
            inner.CapturePcmAvailable -= _captureTapHandler;
        }

        _playbackTapHandler = pcm =>
        {
            if (_isRecording)
            {
                _mixedRecorder?.TapRemotePcm(pcm);
            }

            IncomingPlaybackPcm?.Invoke(pcm, pcm.Length);
        };
        inner.PlaybackPcmAvailable += _playbackTapHandler;

        _captureTapHandler = (_, e) =>
        {
            if (e.BytesRecorded > 0)
            {
                OutgoingCapturePcm?.Invoke(e.Buffer, e.BytesRecorded);
            }
        };
        inner.CapturePcmAvailable += _captureTapHandler;
    }

    private void StopRecordingInternal()
    {
        if (!_isRecording)
        {
            return;
        }

        if (_audioEndPoint?.Inner is CallAnalogWindowsAudioEndPoint inner && _playbackTapHandler is not null)
        {
            inner.PlaybackPcmAvailable -= _playbackTapHandler;
        }

        if (_audioEndPoint?.Inner is CallAnalogWindowsAudioEndPoint innerCapture && _captureTapHandler is not null)
        {
            innerCapture.CapturePcmAvailable -= _captureTapHandler;
        }

        _playbackTapHandler = null;
        _captureTapHandler = null;
        _mixedRecorder?.Stop();
        _mixedRecorder?.Dispose();
        _mixedRecorder = null;

        var recordedPath = _recordingFilePath;
        _recordingFilePath = null;

        _isRecording = false;
        RecordingStateChanged?.Invoke(this, false);
        _log.Info("Recording stopped (mixed mic + remote)");

        if (!string.IsNullOrWhiteSpace(recordedPath) && File.Exists(recordedPath))
        {
            TryFinalizeRecordingFile(recordedPath);
        }
    }

    private void TryFinalizeRecordingFile(string recordedPath)
    {
        var format = UserSettingsService.NormalizeRecordingFormat(_settingsService.Settings.CallRecordingFormat);
        if (format != "mp3")
        {
            _log.Info($"Recording saved to {recordedPath}");
            return;
        }

        var mp3Path = Path.ChangeExtension(recordedPath, ".mp3");
        try
        {
            MediaFoundationLifecycle.Startup();
            using var reader = new AudioFileReader(recordedPath);
            MediaFoundationEncoder.EncodeToMp3(reader, mp3Path);
            File.Delete(recordedPath);
            _log.Info($"Recording saved to {mp3Path}");
        }
        catch (Exception ex)
        {
            _log.Error($"MP3 conversion failed, keeping WAV file: {ex.Message}");
        }
        finally
        {
            MediaFoundationLifecycle.Shutdown();
        }
    }

    private void StopHoldMusicInternal()
    {
        if (_holdMusicPlayer is not null && _holdMusicStoppedHandler is not null)
        {
            _holdMusicPlayer.PlaybackStopped -= _holdMusicStoppedHandler;
        }

        _holdMusicStoppedHandler = null;
        try
        {
            _holdMusicPlayer?.Stop();
        }
        catch
        {
            // Best-effort.
        }

        WinMmAudioOutputManager.Release(WinMmAudioOutputManager.OwnerHoldMusic);
        _holdMusicPlayer = null;

        CallAnalog.Softphone.Helpers.AudioFilePlaybackHelper.SafeDispose(_holdMusicReader);
        _holdMusicReader = null;
        RestoreCallAudioAfterHoldMusic();
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private string BuildDestinationUri(string number)
    {
        if (_config is null)
        {
            throw new InvalidOperationException("SIP is not configured.");
        }

        return SipUriBuilder.BuildDialUri(_config, number);
    }

    private SIPCallDescriptor CreateOutboundCallDescriptor(string destination)
    {
        if (_config is null)
        {
            throw new InvalidOperationException("SIP is not configured.");
        }

        var fromUri = SipUriBuilder.BuildFromUri(_config);
        var displayName = _config.DisplayName ?? _config.Extension;
        var fromHeader = string.IsNullOrWhiteSpace(displayName)
            ? $"<{fromUri}>"
            : $"\"{displayName}\" <{fromUri}>";

        return new SIPCallDescriptor(
            _config.Extension,
            _config.Password,
            destination,
            fromHeader,
            destination,
            routeSet: null,
            customHeaders: [ $"User-Agent: {UserAgent}" ],
            authUsername: null,
            callDirection: SIPCallDirection.Out,
            contentType: SDP.SDP_MIME_CONTENTTYPE,
            content: null,
            mangleIPAddress: null);
    }

    private SIPURI BuildTransferUri(string target)
    {
        if (target.StartsWith("sip:", StringComparison.OrdinalIgnoreCase))
        {
            return SIPURI.ParseSIPURI(SipUriBuilder.BuildDialUri(_config!, target));
        }

        if (_config is null)
        {
            throw new InvalidOperationException("SIP is not configured.");
        }

        return SIPURI.ParseSIPURI(SipUriBuilder.BuildDialUri(_config, target));
    }

    private void UnregisterInternal()
    {
        StopKeepAlive();
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
        _reconnectAttempt = 0;
        _keepAliveSendFailures = 0;
        _lastSipActivityUtc = null;
        StopRecordingInternal();
        StopHoldMusicInternal();

        _registrationAgent?.Stop();
        _registrationAgent = null;

        _userAgent?.Hangup();
        _userAgent = null;

        _pendingIncomingRequest = null;
        _pendingIncomingUas = null;
        _remoteParty = null;

        DisposeMediaSession();

        _transport?.Shutdown();
        _transport = null;

        _config = null;
        SetRegistrationState(SipRegistrationState.Unregistered);
        SetCallState(CallState.Idle);
    }

    private CallAnalogWindowsAudioEndPoint CreateConfiguredAudioEndPoint(
        IAudioEncoder encoder,
        int outputDevice,
        int inputDevice)
    {
        var settings = _settingsService.Settings;
        return new CallAnalogWindowsAudioEndPoint(
            encoder,
            outputDevice,
            inputDevice,
            VoiceQualitySettingsHelper.ParseProfile(settings.VoiceQualityProfile),
            VoiceQualitySettingsHelper.ParseEcho(settings.VoiceEchoControl),
            VoiceQualitySettingsHelper.ParseNoise(settings.VoiceNoiseReduction),
            settings.VoiceAutoGainEnabled,
            _callQualityMonitor,
            settings.SpeakerDevice,
            settings.SpeakerDeviceId,
            settings.MicrophoneDevice,
            settings.MicrophoneDeviceId,
            _preferWasapiAudio);
    }

    private void DisposeMediaSession()
    {
        StopMediaRecoveryMonitor();

        if (_callQualityMonitor.Current.FramesReceived > 0)
        {
            _log.Info(_callQualityMonitor.FormatHangupSummary());
        }

        _mediaSession?.Close("shutdown");
        _mediaSession = null;

        if (_audioEndPoint is not null)
        {
            if (_audioEndPoint.IsSpeakerMuted)
            {
                _ = _audioEndPoint.SetSpeakerMuted(false);
            }

            _ = _audioEndPoint.Close();
            _audioEndPoint = null;
        }
    }

    private void SetRegistrationState(SipRegistrationState state)
    {
        RegistrationState = state;
        RegistrationStateChanged?.Invoke(this, state);
    }

    private sealed class SipLogProvider(SipLogService log) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SipLogger(log, categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class SipLogger(SipLogService log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} ({exception.Message})";
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                message = $"{category}: {message}";
            }

            switch (logLevel)
            {
                case LogLevel.Warning:
                case LogLevel.Error:
                case LogLevel.Critical:
                    log.Error(message);
                    break;
                default:
                    log.Info(message);
                    break;
            }
        }
    }
}
