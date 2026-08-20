using System.Net;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// SIPSorcery audio endpoint: prefers WASAPI Communications for call I/O, with WinMM fallback.
/// </summary>
public sealed class CallAnalogWindowsAudioEndPoint : IAudioEndPoint
{
    private const int DeviceBitsPerSample = 16;
    private const int DefaultDeviceChannels = 1;
    private const int InputBuffers = 2;

    private readonly ILogger _logger = SIPSorcery.LogFactory.CreateLogger<CallAnalogWindowsAudioEndPoint>();
    private readonly IAudioEncoder _audioEncoder;
    private readonly MediaFormatManager<AudioFormat> _audioFormatManager;
    private readonly int _audioOutDeviceIndex;
    private readonly int _audioInDeviceIndex;
    private readonly string? _speakerDeviceName;
    private readonly string? _speakerDeviceId;
    private readonly string? _microphoneDeviceName;
    private readonly string? _microphoneDeviceId;
    private readonly int _captureBufferMs;
    private readonly int _playbackLatencyMs;
    private readonly int _playbackBuffers;
    private readonly CallVoiceProcessor _voiceProcessor;
    private readonly CallQualityMonitor? _qualityMonitor;
    private readonly AntiAliasLowPassFilter _captureAntiAlias = new();

    private WaveFormat _waveSinkFormat = new(8000, DeviceBitsPerSample, DefaultDeviceChannels);
    private WaveFormat _waveSourceFormat = new(8000, DeviceBitsPerSample, DefaultDeviceChannels);
    private IWavePlayer? _wavePlayer;
    private WaveOutEvent? _waveOutEvent;
    private WasapiOut? _wasapiOut;
    private SilencePaddingWaveProvider? _waveProvider;
    private WaveInEvent? _waveInEvent;
    private WasapiCapture? _wasapiCapture;
    private EventHandler<StoppedEventArgs>? _playbackStoppedHandler;
    private readonly bool _preferWasapi;
    private bool _usingWasapiPlayback;
    private bool _usingWasapiCapture;
    private bool _forceWinMmPlayback;
    private int _saturatedPlaybackChecks;
    private int _playbackRecoveryPending;

    private bool _isAudioSourceStarted;
    private bool _isAudioSinkStarted;
    private bool _isAudioSourcePaused;
    private bool _isAudioSinkPaused;
    private bool _isAudioSourceClosed;
    private bool _isAudioSinkClosed;
    private int _receivedFrameCount;
    private byte[]? _lastPcmFrame;

    public CallAnalogWindowsAudioEndPoint(
        IAudioEncoder audioEncoder,
        int audioOutDeviceIndex = -1,
        int audioInDeviceIndex = -1,
        VoiceQualityProfile profile = VoiceQualityProfile.Balanced,
        VoiceEchoControl echoControl = VoiceEchoControl.On,
        VoiceNoiseReduction noiseReduction = VoiceNoiseReduction.Low,
        bool autoGain = true,
        CallQualityMonitor? qualityMonitor = null,
        string? speakerDeviceName = null,
        string? speakerDeviceId = null,
        string? microphoneDeviceName = null,
        string? microphoneDeviceId = null,
        bool preferWasapi = false)
    {
        _preferWasapi = preferWasapi;
        _audioEncoder = audioEncoder;
        _audioFormatManager = new MediaFormatManager<AudioFormat>(audioEncoder.SupportedFormats);
        _audioOutDeviceIndex = audioOutDeviceIndex;
        _audioInDeviceIndex = audioInDeviceIndex;
        _speakerDeviceName = speakerDeviceName;
        _speakerDeviceId = speakerDeviceId;
        _microphoneDeviceName = microphoneDeviceName;
        _microphoneDeviceId = microphoneDeviceId;
        (_captureBufferMs, _playbackLatencyMs, _playbackBuffers) = CallVoiceProcessor.ResolveBuffers(profile);
        _voiceProcessor = new CallVoiceProcessor(echoControl, noiseReduction, autoGain);
        _qualityMonitor = qualityMonitor;

        InitPlaybackDevice(_audioOutDeviceIndex, _waveSinkFormat.SampleRate, _waveSinkFormat.Channels);
        InitCaptureDevice(_audioInDeviceIndex, _waveSourceFormat.SampleRate, _waveSourceFormat.Channels);

        if (audioEncoder.SupportedFormats?.Count == 1)
        {
            SetAudioSinkFormat(audioEncoder.SupportedFormats[0]);
            SetAudioSourceFormat(audioEncoder.SupportedFormats[0]);
        }
    }

    public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
    public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
    public event RawAudioSampleDelegate? OnAudioSourceRawSample { add { } remove { } }
    public event SourceErrorDelegate? OnAudioSourceError;
    public event SourceErrorDelegate? OnAudioSinkError;
    public event Action<byte[]>? PlaybackPcmAvailable;
    public event EventHandler<WaveInEventArgs>? CapturePcmAvailable;

    public void RestrictFormats(Func<AudioFormat, bool> filter) => _audioFormatManager.RestrictFormats(filter);

    public List<AudioFormat> GetAudioSourceFormats() => _audioFormatManager.GetSourceFormats();

    public List<AudioFormat> GetAudioSinkFormats() => _audioFormatManager.GetSourceFormats();

    public bool HasEncodedAudioSubscribers() => OnAudioSourceEncodedSample is not null;

    public bool IsAudioSourcePaused() => _isAudioSourcePaused;

    public bool IsAudioSinkPaused() => _isAudioSinkPaused;

    public void ExternalAudioSourceRawSample(
        AudioSamplingRatesEnum samplingRate,
        uint durationMilliseconds,
        short[] sample) =>
        throw new NotImplementedException();

    public void SetAudioSourceFormat(AudioFormat audioFormat)
    {
        _audioFormatManager.SetSelectedFormat(audioFormat);

        if (_waveSourceFormat.SampleRate != _audioFormatManager.SelectedFormat.ClockRate
            || _waveSourceFormat.Channels != _audioFormatManager.SelectedFormat.ChannelCount)
        {
            InitCaptureDevice(
                _audioInDeviceIndex,
                _audioFormatManager.SelectedFormat.ClockRate,
                _audioFormatManager.SelectedFormat.ChannelCount);
        }
    }

    public void SetAudioSinkFormat(AudioFormat audioFormat)
    {
        _audioFormatManager.SetSelectedFormat(audioFormat);

        if (_waveSinkFormat.SampleRate != _audioFormatManager.SelectedFormat.ClockRate
            || _waveSinkFormat.Channels != _audioFormatManager.SelectedFormat.ChannelCount)
        {
            InitPlaybackDevice(
                _audioOutDeviceIndex,
                _audioFormatManager.SelectedFormat.ClockRate,
                _audioFormatManager.SelectedFormat.ChannelCount);
        }
    }

    public MediaEndPoints ToMediaEndPoints() =>
        new()
        {
            AudioSource = this,
            AudioSink = this
        };

    public Task Start()
    {
        StartAudio();
        StartAudioSink();
        return Task.CompletedTask;
    }

    public Task Close()
    {
        CloseAudio();
        CloseAudioSink();
        DetachPlaybackStoppedHandler();
        ReleasePlaybackDevice();
        return Task.CompletedTask;
    }

    public Task Pause()
    {
        PauseAudio();
        PauseAudioSink();
        return Task.CompletedTask;
    }

    public Task Resume()
    {
        ResumeAudio();
        ResumeAudioSink();
        return Task.CompletedTask;
    }

    public Task PauseAudioSink()
    {
        _isAudioSinkPaused = true;
        try
        {
            _wavePlayer?.Pause();
        }
        catch
        {
            // Best-effort.
        }

        return Task.CompletedTask;
    }

    public Task ResumeAudioSink()
    {
        _isAudioSinkPaused = false;
        EnsurePlaying();
        return Task.CompletedTask;
    }

    public Task StartAudioSink()
    {
        _isAudioSinkStarted = true;
        _isAudioSinkClosed = false;
        _isAudioSinkPaused = false;
        EnsurePlaying();
        return Task.CompletedTask;
    }

    public Task CloseAudioSink()
    {
        if (!_isAudioSinkClosed)
        {
            _isAudioSinkClosed = true;
            try
            {
                _wavePlayer?.Stop();
            }
            catch
            {
                // Best-effort.
            }

            DetachPlaybackStoppedHandler();
            ReleasePlaybackDevice();
        }

        return Task.CompletedTask;
    }

    public Task PauseAudio()
    {
        _isAudioSourcePaused = true;
        StopCaptureRecording();
        return Task.CompletedTask;
    }

    public Task ResumeAudio()
    {
        _isAudioSourcePaused = false;
        StartCaptureRecording();
        return Task.CompletedTask;
    }

    public Task StartAudio()
    {
        if (!_isAudioSourceStarted)
        {
            _isAudioSourceStarted = true;
            StartCaptureRecording();
        }

        return Task.CompletedTask;
    }

    public Task CloseAudio()
    {
        if (!_isAudioSourceClosed)
        {
            _isAudioSourceClosed = true;
            DisposeCaptureDevice();
        }

        return Task.CompletedTask;
    }

    public void ClearPlaybackBuffer() => _waveProvider?.ClearBuffer();

    /// <summary>
    /// Fully releases call playback so ringtone/hold music can use the output device.
    /// </summary>
    public void SuspendPlaybackForExclusiveAudio()
    {
        if (_wavePlayer is null)
        {
            return;
        }

        DetachPlaybackStoppedHandler();
        try
        {
            _wavePlayer.Stop();
        }
        catch
        {
            // Best-effort.
        }

        ReleasePlaybackDevice();
        _isAudioSinkClosed = true;
    }

    /// <summary>
    /// Recreates call playback after ringtone/hold music releases the output device.
    /// </summary>
    public void ReinitializePlayback()
    {
        InitPlaybackDevice(_audioOutDeviceIndex, _waveSinkFormat.SampleRate, _waveSinkFormat.Channels);
        _isAudioSinkStarted = true;
        _isAudioSinkClosed = false;
        _isAudioSinkPaused = false;
    }

    public void GotAudioSample(byte[] pcmSample) => _waveProvider?.AddSamples(pcmSample, 0, pcmSample.Length);

    public void GotAudioRtp(
        IPEndPoint remoteEndPoint,
        uint ssrc,
        uint seqnum,
        uint timestamp,
        int payloadID,
        bool marker,
        byte[] payload)
    {
        if (_waveProvider is null)
        {
            return;
        }

        var pcmSample = _audioEncoder.DecodeAudio(payload, _audioFormatManager.SelectedFormat);
        var pcmBytes = PcmSamplesToBytes(pcmSample);
        _waveProvider.AddSamples(pcmBytes, 0, pcmBytes.Length);
    }

    public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
    {
        var audioFormat = encodedMediaFrame.AudioFormat;
        var waveProvider = _waveProvider;
        if (waveProvider is null || audioFormat.IsEmpty())
        {
            return;
        }

        var pcmSample = _audioEncoder.DecodeAudio(encodedMediaFrame.EncodedAudio, audioFormat);
        var pcmBytes = PcmSamplesToBytes(pcmSample);

        if (_lastPcmFrame is not null
            && PlaybackConcealmentHelper.ShouldRepeatLastFrame(
                _receivedFrameCount,
                waveProvider.BufferedBytes,
                pcmBytes.Length))
        {
            waveProvider.AddSamples(_lastPcmFrame, 0, _lastPcmFrame.Length);
        }

        _lastPcmFrame = pcmBytes;
        _voiceProcessor.ObservePlaybackPcm(pcmBytes);
        PlaybackPcmAvailable?.Invoke(pcmBytes);
        waveProvider.AddSamples(pcmBytes, 0, pcmBytes.Length);
        _qualityMonitor?.OnPlaybackFrame();

        _receivedFrameCount++;
        if (_receivedFrameCount == 1 || _receivedFrameCount % 100 == 0)
        {
            App.SipLog.Info(
                $"Call audio playback frame #{_receivedFrameCount} ({pcmBytes.Length} PCM bytes, {audioFormat.FormatName}, " +
                $"buffer={waveProvider.BufferedBytes} bytes, backend={(_usingWasapiPlayback ? "WASAPI" : "WinMM")}).");
            EnsurePlaying();
            CheckPlaybackDraining(waveProvider);
        }
    }

    private static byte[] PcmSamplesToBytes(short[] samples)
    {
        var pcmBytes = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, pcmBytes, 0, pcmBytes.Length);
        return pcmBytes;
    }

    private void InitPlaybackDevice(int audioOutDeviceIndex, int audioSinkSampleRate, int channels)
    {
        try
        {
            DetachPlaybackStoppedHandler();
            ReleasePlaybackDevice();

            _waveSinkFormat = new WaveFormat(audioSinkSampleRate, DeviceBitsPerSample, channels);
            _waveProvider ??= new SilencePaddingWaveProvider(_waveSinkFormat);
            if (_waveProvider.WaveFormat.SampleRate != _waveSinkFormat.SampleRate
                || _waveProvider.WaveFormat.Channels != _waveSinkFormat.Channels)
            {
                _waveProvider = new SilencePaddingWaveProvider(_waveSinkFormat);
            }

            if (_forceWinMmPlayback || !_preferWasapi || !TryInitWasapiPlayback())
            {
                InitWinMmPlayback(audioOutDeviceIndex, audioSinkSampleRate);
            }

            _playbackStoppedHandler = (_, args) =>
            {
                if (args.Exception is not null)
                {
                    App.SipLog.Warn($"Call playback stopped: {args.Exception.Message}");
                    RequestPlaybackRecovery();
                    return;
                }

                EnsurePlaying();
            };
            _wavePlayer!.PlaybackStopped += _playbackStoppedHandler;

            _isAudioSinkClosed = false;
            EnsurePlaying();
        }
        catch (Exception excp)
        {
            _logger.LogWarning(0, excp, "CallAnalogWindowsAudioEndPoint failed to initialise playback device.");
            OnAudioSinkError?.Invoke($"Playback init failed: {excp.Message}");
        }
    }

    private bool TryInitWasapiPlayback()
    {
        try
        {
            // Ensure ringtone/hold WinMM owners release before WASAPI opens the same endpoint.
            WinMmAudioOutputManager.ForceReleaseAll();
            var device = AudioDeviceHelper.GetCommunicationsRenderDevice(_speakerDeviceName, _speakerDeviceId);
            _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, true, _playbackLatencyMs);
            _wasapiOut.Init(_waveProvider);
            _wavePlayer = _wasapiOut;
            _usingWasapiPlayback = true;
            App.SipLog.Info(
                $"Call playback WASAPI Communications/shared: {device.FriendlyName} @ {_waveSinkFormat.SampleRate} Hz " +
                $"(latency={_playbackLatencyMs} ms)");
            return true;
        }
        catch (Exception ex)
        {
            App.SipLog.Warn($"WASAPI playback unavailable ({ex.Message}); falling back to WinMM.");
            try
            {
                _wasapiOut?.Dispose();
            }
            catch
            {
                // Best-effort.
            }

            _wasapiOut = null;
            _wavePlayer = null;
            _usingWasapiPlayback = false;
            return false;
        }
    }

    private void InitWinMmPlayback(int audioOutDeviceIndex, int audioSinkSampleRate)
    {
        _waveOutEvent = WinMmAudioOutputManager.CreateWaveOut(
            WinMmAudioOutputManager.OwnerCallPlayback,
            _waveProvider!,
            audioOutDeviceIndex,
            desiredLatency: _playbackLatencyMs,
            numberOfBuffers: _playbackBuffers);
        _waveOutEvent.Volume = 1f;
        _wavePlayer = _waveOutEvent;
        _usingWasapiPlayback = false;

        if (audioOutDeviceIndex >= 0)
        {
            var deviceName = WaveOut.GetCapabilities(audioOutDeviceIndex).ProductName;
            App.SipLog.Info(
                $"Call playback WinMM device index: {audioOutDeviceIndex} ({deviceName}) @ {audioSinkSampleRate} Hz");
        }
        else
        {
            App.SipLog.Info($"Call playback WinMM default device @ {audioSinkSampleRate} Hz");
        }
    }

    private void DetachPlaybackStoppedHandler()
    {
        if (_wavePlayer is not null && _playbackStoppedHandler is not null)
        {
            _wavePlayer.PlaybackStopped -= _playbackStoppedHandler;
        }

        _playbackStoppedHandler = null;
    }

    private void ReleasePlaybackDevice()
    {
        if (_usingWasapiPlayback)
        {
            try
            {
                _wasapiOut?.Dispose();
            }
            catch
            {
                // Best-effort.
            }

            _wasapiOut = null;
            _wavePlayer = null;
            _waveOutEvent = null;
            _usingWasapiPlayback = false;
            return;
        }

        WinMmAudioOutputManager.Release(WinMmAudioOutputManager.OwnerCallPlayback);
        _waveOutEvent = null;
        _wavePlayer = null;
    }

    private void InitCaptureDevice(int audioInDeviceIndex, int audioSourceSampleRate, int audioSourceChannels)
    {
        DisposeCaptureDevice();

        _waveSourceFormat = new WaveFormat(audioSourceSampleRate, DeviceBitsPerSample, audioSourceChannels);
        _captureAntiAlias.Reset();
        if (!_preferWasapi || !TryInitWasapiCapture())
        {
            InitWinMmCapture(audioInDeviceIndex);
        }
    }

    private bool TryInitWasapiCapture()
    {
        try
        {
            var device = AudioDeviceHelper.GetCommunicationsCaptureDevice(_microphoneDeviceName, _microphoneDeviceId);
            _wasapiCapture = new WasapiCapture(device);
            _wasapiCapture.DataAvailable += LocalAudioSampleAvailable;
            _usingWasapiCapture = true;
            var deviceFormat = _wasapiCapture.WaveFormat;
            App.SipLog.Info(
                $"Call capture WASAPI Communications: {device.FriendlyName} " +
                $"(device={deviceFormat.SampleRate} Hz {deviceFormat.Encoding} {deviceFormat.BitsPerSample}-bit " +
                $"{deviceFormat.Channels}ch → codec={_waveSourceFormat.SampleRate} Hz)");
            return true;
        }
        catch (Exception ex)
        {
            App.SipLog.Warn($"WASAPI capture unavailable ({ex.Message}); falling back to WinMM.");
            DisposeCaptureDevice();
            return false;
        }
    }

    private void InitWinMmCapture(int audioInDeviceIndex)
    {
        if (WaveInEvent.DeviceCount <= 0)
        {
            OnAudioSourceError?.Invoke("No audio capture devices are available.");
            return;
        }

        if (audioInDeviceIndex >= WaveInEvent.DeviceCount)
        {
            OnAudioSourceError?.Invoke(
                $"The requested audio input device index {audioInDeviceIndex} exceeds the maximum index of {WaveInEvent.DeviceCount - 1}.");
            return;
        }

        _waveInEvent = new WaveInEvent
        {
            BufferMilliseconds = _captureBufferMs,
            NumberOfBuffers = InputBuffers,
            DeviceNumber = audioInDeviceIndex >= 0 ? audioInDeviceIndex : 0,
            WaveFormat = _waveSourceFormat
        };
        _waveInEvent.DataAvailable += LocalAudioSampleAvailable;
        _usingWasapiCapture = false;
        App.SipLog.Info($"Call capture WinMM device index: {(audioInDeviceIndex >= 0 ? audioInDeviceIndex : 0)}");
    }

    private void DisposeCaptureDevice()
    {
        if (_wasapiCapture is not null)
        {
            _wasapiCapture.DataAvailable -= LocalAudioSampleAvailable;
            try
            {
                _wasapiCapture.StopRecording();
            }
            catch
            {
                // Best-effort.
            }

            try
            {
                _wasapiCapture.Dispose();
            }
            catch
            {
                // Best-effort.
            }

            _wasapiCapture = null;
        }

        if (_waveInEvent is not null)
        {
            _waveInEvent.DataAvailable -= LocalAudioSampleAvailable;
            try
            {
                _waveInEvent.StopRecording();
            }
            catch
            {
                // Best-effort.
            }

            try
            {
                _waveInEvent.Dispose();
            }
            catch
            {
                // Best-effort.
            }

            _waveInEvent = null;
        }

        _usingWasapiCapture = false;
    }

    private void StartCaptureRecording()
    {
        try
        {
            if (_wasapiCapture is not null)
            {
                _wasapiCapture.StartRecording();
            }
            else
            {
                _waveInEvent?.StartRecording();
            }
        }
        catch (Exception ex)
        {
            OnAudioSourceError?.Invoke($"Capture start failed: {ex.Message}");
        }
    }

    private void StopCaptureRecording()
    {
        try
        {
            if (_wasapiCapture is not null)
            {
                _wasapiCapture.StopRecording();
            }
            else
            {
                _waveInEvent?.StopRecording();
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private void LocalAudioSampleAvailable(object? sender, WaveInEventArgs args)
    {
        if (args.BytesRecorded <= 0)
        {
            return;
        }

        CapturePcmAvailable?.Invoke(this, args);

        short[] pcm;
        if (_usingWasapiCapture && _wasapiCapture is not null)
        {
            pcm = PcmFormatConverter.ConvertToPcm16(
                args.Buffer,
                args.BytesRecorded,
                _wasapiCapture.WaveFormat,
                _waveSourceFormat,
                _captureAntiAlias);
        }
        else
        {
            var buffer = args.Buffer.Take(args.BytesRecorded).ToArray();
            pcm = buffer.Where((_, i) => i % 2 == 0).Select((_, i) => BitConverter.ToInt16(buffer, i * 2)).ToArray();
        }

        if (pcm.Length == 0)
        {
            return;
        }

        _voiceProcessor.ProcessCapturePcm(pcm);
        var encodedSample = _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat);

        OnAudioSourceEncodedSample?.Invoke((uint)encodedSample.Length, encodedSample);

        if (OnAudioSourceEncodedFrameReady is not null)
        {
            OnAudioSourceEncodedFrameReady(
                new EncodedAudioFrame(
                    0,
                    _audioFormatManager.SelectedFormat,
                    GetEncodeSampleDurationMs(pcm.Length, _audioFormatManager.SelectedFormat),
                    encodedSample));
        }
    }

    private static uint GetEncodeSampleDurationMs(int totalPcmSamples, AudioFormat audioFormat)
    {
        var numChannels = audioFormat.ChannelCount;
        var sampleRate = audioFormat.ClockRate;
        var frames = totalPcmSamples / Math.Max(1, numChannels);
        var durationMs = sampleRate > 0 ? frames / (double)sampleRate * 1000.0 : 0;
        return (uint)Math.Round(durationMs);
    }

    private void EnsurePlaying()
    {
        if (_wavePlayer is null || _isAudioSinkClosed || _isAudioSinkPaused || !_isAudioSinkStarted)
        {
            return;
        }

        try
        {
            if (_wavePlayer.PlaybackState != PlaybackState.Playing)
            {
                _wavePlayer.Play();
            }
        }
        catch (Exception ex)
        {
            // Never let an audio fault propagate into the RTP receive path.
            App.SipLog.Warn($"Call playback could not resume ({ex.Message}).");
            RequestPlaybackRecovery();
        }
    }

    private void CheckPlaybackDraining(SilencePaddingWaveProvider waveProvider)
    {
        var saturated = PlaybackStallHelper.IsBufferSaturated(
            waveProvider.BufferedBytes,
            waveProvider.CapacityBytes);
        _saturatedPlaybackChecks = PlaybackStallHelper.NextSaturatedStreak(_saturatedPlaybackChecks, saturated);

        if (!PlaybackStallHelper.ShouldRecoverPlayback(_saturatedPlaybackChecks))
        {
            return;
        }

        _saturatedPlaybackChecks = 0;
        App.SipLog.Warn(
            $"Call playback is not draining (buffer pinned at {waveProvider.BufferedBytes} bytes); restarting sink.");
        RequestPlaybackRecovery();
    }

    private void RequestPlaybackRecovery()
    {
        if (_isAudioSinkClosed || Interlocked.Exchange(ref _playbackRecoveryPending, 1) == 1)
        {
            return;
        }

        // Off the audio callback thread: disposing a wave player from its own event can deadlock.
        Task.Run(RecoverPlayback);
    }

    private void RecoverPlayback()
    {
        try
        {
            if (_isAudioSinkClosed)
            {
                return;
            }

            _forceWinMmPlayback = true;
            _waveProvider?.ClearBuffer();
            InitPlaybackDevice(_audioOutDeviceIndex, _waveSinkFormat.SampleRate, _waveSinkFormat.Channels);
            _saturatedPlaybackChecks = 0;
            App.SipLog.Info("Call playback restarted on WinMM after sink failure.");
        }
        catch (Exception ex)
        {
            App.SipLog.Error($"Call playback recovery failed: {ex.Message}");
            OnAudioSinkError?.Invoke($"Playback recovery failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _playbackRecoveryPending, 0);
        }
    }

    private sealed class SilencePaddingWaveProvider : IWaveProvider
    {
        private readonly BufferedWaveProvider _buffer;

        public SilencePaddingWaveProvider(WaveFormat format)
        {
            _buffer = new BufferedWaveProvider(format)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };
        }

        public WaveFormat WaveFormat => _buffer.WaveFormat;

        public int BufferedBytes => _buffer.BufferedBytes;

        public int CapacityBytes => _buffer.BufferLength;

        public void AddSamples(byte[] buffer, int offset, int count) =>
            _buffer.AddSamples(buffer, offset, count);

        public void ClearBuffer() => _buffer.ClearBuffer();

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = _buffer.Read(buffer, offset, count);
            if (read > 0)
            {
                return read;
            }

            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
