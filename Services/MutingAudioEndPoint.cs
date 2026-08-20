using System.Net;
using SIPSorceryMedia.Abstractions;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Mute wrapper for call capture. AudioSource is this wrapper (mute on send);
/// AudioSink is the inner endpoint (direct playback), as SIPSorcery/MicroSIP expect.
/// </summary>
public sealed class MutingAudioEndPoint : IAudioEndPoint
{
    private readonly CallAnalogWindowsAudioEndPoint _inner;
    private bool _muted;
    private bool _speakerMuted;
    private AudioSilenceConfiguration.SilenceSpec _silenceSpec = new(false, 0xFF);

    public MutingAudioEndPoint(CallAnalogWindowsAudioEndPoint inner)
    {
        _inner = inner;
        _inner.OnAudioSourceEncodedSample += OnInnerEncodedSample;
        _inner.OnAudioSourceEncodedFrameReady += OnInnerEncodedFrameReady;
    }

    public CallAnalogWindowsAudioEndPoint Inner => _inner;

    public event EncodedSampleDelegate? OnAudioSourceEncodedSample;
    public event Action<EncodedAudioFrame>? OnAudioSourceEncodedFrameReady;
    public event RawAudioSampleDelegate? OnAudioSourceRawSample { add { } remove { } }
    public event SourceErrorDelegate? OnAudioSourceError
    {
        add => _inner.OnAudioSourceError += value;
        remove => _inner.OnAudioSourceError -= value;
    }

    public event SourceErrorDelegate? OnAudioSinkError
    {
        add => _inner.OnAudioSinkError += value;
        remove => _inner.OnAudioSinkError -= value;
    }

    public void SetMuted(bool muted) => _muted = muted;

    public bool IsSpeakerMuted => _speakerMuted;

    public Task SetSpeakerMuted(bool muted)
    {
        _speakerMuted = muted;
        return muted ? _inner.PauseAudioSink() : _inner.ResumeAudioSink();
    }

    public void RestrictFormats(Func<AudioFormat, bool> filter) => _inner.RestrictFormats(filter);

    public List<AudioFormat> GetAudioSourceFormats() => _inner.GetAudioSourceFormats();

    public List<AudioFormat> GetAudioSinkFormats() => _inner.GetAudioSinkFormats();

    public bool HasEncodedAudioSubscribers() =>
        OnAudioSourceEncodedSample is not null || OnAudioSourceEncodedFrameReady is not null;

    public bool IsAudioSourcePaused() => _inner.IsAudioSourcePaused();

    public bool IsAudioSinkPaused() => _inner.IsAudioSinkPaused();

    public void ExternalAudioSourceRawSample(
        AudioSamplingRatesEnum samplingRate,
        uint durationMilliseconds,
        short[] sample) =>
        _inner.ExternalAudioSourceRawSample(samplingRate, durationMilliseconds, sample);

    public void SetAudioSourceFormat(AudioFormat audioFormat)
    {
        _inner.SetAudioSourceFormat(audioFormat);
        UpdateSilenceByte(audioFormat);
        App.SipLog.Info($"Audio source format negotiated: {audioFormat.FormatName} (PT {audioFormat.FormatID}, {audioFormat.ClockRate} Hz).");
    }

    public void SetAudioSinkFormat(AudioFormat format)
    {
        _inner.SetAudioSinkFormat(format);
        App.SipLog.Info($"Audio sink format negotiated: {format.FormatName} (PT {format.FormatID}, {format.ClockRate} Hz).");
    }

    public Task Start() => _inner.Start();

    public Task Close() => _inner.Close();

    public Task Pause()
    {
        if (_muted)
        {
            return _inner.PauseAudioSink();
        }

        return _inner.Pause();
    }

    public Task Resume() => _inner.Resume();

    public Task PauseAudio() =>
        _muted ? Task.CompletedTask : _inner.PauseAudio();

    public Task ResumeAudio() =>
        _muted ? Task.CompletedTask : _inner.ResumeAudio();

    public Task PauseAudioSink() => _inner.PauseAudioSink();

    public Task ResumeAudioSink() => _inner.ResumeAudioSink();

    public Task StartAudio() => _inner.StartAudio();

    public Task CloseAudio() => _inner.CloseAudio();

    public Task StartAudioSink() => _inner.StartAudioSink();

    public Task CloseAudioSink() => _inner.CloseAudioSink();

    public void GotAudioSample(byte[] pcmSample) => _inner.GotAudioSample(pcmSample);

    public void GotAudioRtp(
        IPEndPoint remoteEndPoint,
        uint ssrc,
        uint seqnum,
        uint timestamp,
        int payloadID,
        bool marker,
        byte[] payload) =>
        _inner.GotAudioRtp(remoteEndPoint, ssrc, seqnum, timestamp, payloadID, marker, payload);

    public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame) =>
        _inner.GotEncodedMediaFrame(encodedMediaFrame);

    public MediaEndPoints ToMediaEndPoints() =>
        new()
        {
            AudioSource = this,
            AudioSink = _inner
        };

    private void OnInnerEncodedSample(uint sampleDurationMilliseconds, byte[] sample)
    {
        var payload = _muted ? CreateSilence(sample) : sample;
        OnAudioSourceEncodedSample?.Invoke(sampleDurationMilliseconds, payload);
    }

    private void OnInnerEncodedFrameReady(EncodedAudioFrame frame)
    {
        if (!_muted)
        {
            OnAudioSourceEncodedFrameReady?.Invoke(frame);
            return;
        }

        var silence = CreateSilence(frame.EncodedAudio);
        OnAudioSourceEncodedFrameReady?.Invoke(
            new EncodedAudioFrame(
                frame.MediaStreamIndex,
                frame.AudioFormat,
                frame.DurationMilliSeconds,
                silence));
    }

    private byte[] CreateSilence(byte[] template) =>
        AudioSilenceConfiguration.CreateSilenceBuffer(template.Length, _silenceSpec);

    private void UpdateSilenceByte(AudioFormat format) =>
        _silenceSpec = AudioSilenceConfiguration.GetSilenceSpec(format);
}
