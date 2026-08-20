using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// Estimates call media quality from inbound frame timing (proxy for RTP loss/jitter).
/// </summary>
public sealed class CallQualityMonitor
{
    private readonly object _sync = new();
    private readonly Queue<double> _intervalsMs = new();
    private DateTime? _lastFrameUtc;
    private int _framesReceived;
    private int _gapEvents;
    private double _jitterEma;

    public CallMediaQualitySnapshot Current { get; private set; } =
        new(0, "Waiting", null, null, 0);

    public event EventHandler<CallMediaQualitySnapshot>? QualityUpdated;

    public void Reset()
    {
        lock (_sync)
        {
            _intervalsMs.Clear();
            _lastFrameUtc = null;
            _framesReceived = 0;
            _gapEvents = 0;
            _jitterEma = 0;
            Current = new CallMediaQualitySnapshot(0, "Waiting", null, null, 0);
        }
    }

    public void OnPlaybackFrame()
    {
        CallMediaQualitySnapshot snapshot;
        lock (_sync)
        {
            var now = DateTime.UtcNow;
            _framesReceived++;
            if (_lastFrameUtc is not null)
            {
                var interval = (now - _lastFrameUtc.Value).TotalMilliseconds;
                if (interval > 80)
                {
                    _gapEvents++;
                }

                _intervalsMs.Enqueue(interval);
                while (_intervalsMs.Count > 50)
                {
                    _intervalsMs.Dequeue();
                }

                var mean = _intervalsMs.Average();
                var variance = _intervalsMs.Select(x => (x - mean) * (x - mean)).Average();
                var jitter = Math.Sqrt(variance);
                _jitterEma = _jitterEma <= 0 ? jitter : (0.8 * _jitterEma) + (0.2 * jitter);
            }

            _lastFrameUtc = now;

            var lossPct = _framesReceived <= 1
                ? 0
                : Math.Min(40, 100.0 * _gapEvents / _framesReceived);
            var bars = lossPct switch
            {
                < 2 when _jitterEma < 25 => 4,
                < 5 when _jitterEma < 40 => 3,
                < 12 => 2,
                _ => 1
            };
            var label = bars switch
            {
                4 => "Excellent",
                3 => "Good",
                2 => "Fair",
                _ => "Poor"
            };

            snapshot = new CallMediaQualitySnapshot(
                bars,
                label,
                Math.Round(lossPct, 1),
                Math.Round(_jitterEma, 1),
                _framesReceived);
            Current = snapshot;
        }

        QualityUpdated?.Invoke(this, snapshot);
    }

    public string FormatHangupSummary()
    {
        var s = Current;
        return $"Call media quality: {s.Label} bars={s.Bars} loss~{s.PacketLossPct?.ToString("0.0") ?? "n/a"}% "
            + $"jitter~{s.JitterMs?.ToString("0.0") ?? "n/a"}ms frames={s.FramesReceived}";
    }
}
