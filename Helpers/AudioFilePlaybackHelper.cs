using System.IO;
using CallAnalog.Softphone.Services;
using NAudio.Wave;

namespace CallAnalog.Softphone.Helpers;

internal static class AudioFilePlaybackHelper
{
    private const int TargetSampleRate = 44100;

    internal static WaveStream OpenAudioFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var usesMediaFoundation = extension is ".mp3" or ".wma" or ".m4a" or ".aac";

        if (usesMediaFoundation)
        {
            MediaFoundationLifecycle.Startup();
        }

        try
        {
            WaveStream reader = usesMediaFoundation
                ? new MediaFoundationReader(path)
                : new AudioFileReader(path);

            WaveStream pcmStream = reader;
            if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm
                || reader.WaveFormat.BitsPerSample != 16)
            {
                var pcmFormat = new WaveFormat(reader.WaveFormat.SampleRate, 16, reader.WaveFormat.Channels);
                pcmStream = new WaveFormatConversionStream(pcmFormat, reader);
            }

            if (pcmStream.WaveFormat.SampleRate != TargetSampleRate)
            {
                var resampledFormat = new WaveFormat(TargetSampleRate, 16, pcmStream.WaveFormat.Channels);
                pcmStream = new WaveFormatConversionStream(resampledFormat, pcmStream);
            }

            App.SipLog.Info(
                $"Audio file PCM ready: {Path.GetFileName(path)} -> {pcmStream.WaveFormat.SampleRate} Hz, "
                + $"{pcmStream.WaveFormat.Channels} ch, {pcmStream.WaveFormat.BitsPerSample}-bit");

            return new OwnedAudioWaveStream(pcmStream, usesMediaFoundation);
        }
        catch
        {
            if (usesMediaFoundation)
            {
                MediaFoundationLifecycle.Shutdown();
            }

            throw;
        }
    }

    internal static void SafeDispose(WaveStream? stream)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            stream.Dispose();
        }
        catch (Exception ex)
        {
            App.SipLog.Warn($"Audio stream dispose suppressed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class OwnedAudioWaveStream : WaveStream
    {
        private readonly WaveStream _inner;
        private readonly bool _releaseMediaFoundation;
        private bool _disposed;

        public OwnedAudioWaveStream(WaveStream inner, bool releaseMediaFoundation)
        {
            _inner = inner;
            _releaseMediaFoundation = releaseMediaFoundation;
        }

        public override WaveFormat WaveFormat => _inner.WaveFormat;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _inner.Dispose();
                    }
                    catch (Exception ex)
                    {
                        App.SipLog.Warn($"Owned audio stream inner dispose: {ex.Message}");
                    }

                    if (_releaseMediaFoundation)
                    {
                        MediaFoundationLifecycle.Shutdown();
                    }
                }

                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
