namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Fourth-order Butterworth low-pass applied before downsampling capture audio. Without it,
/// everything above the target Nyquist (mic hiss, fan noise, sibilance) folds back into the voice
/// band as constant harsh noise. State persists across buffers so 20 ms capture chunks do not
/// click at the seams.
/// </summary>
internal sealed class AntiAliasLowPassFilter
{
    private static readonly double[] SectionQ = [0.54119610, 1.30656296];

    private readonly Section[] _sections = [new Section(), new Section()];
    private int _sampleRate;
    private double _cutoffHz;

    public bool IsConfigured => _sampleRate > 0;

    public void Configure(int sampleRate, double cutoffHz)
    {
        if (sampleRate <= 0 || cutoffHz <= 0)
        {
            return;
        }

        cutoffHz = Math.Min(cutoffHz, sampleRate * 0.45);
        if (_sampleRate == sampleRate && Math.Abs(_cutoffHz - cutoffHz) < 0.001)
        {
            return;
        }

        _sampleRate = sampleRate;
        _cutoffHz = cutoffHz;
        for (var i = 0; i < _sections.Length; i++)
        {
            _sections[i].SetLowPass(sampleRate, cutoffHz, SectionQ[i]);
        }
    }

    public void Reset()
    {
        foreach (var section in _sections)
        {
            section.Reset();
        }
    }

    public void ProcessInPlace(short[] samples)
    {
        if (!IsConfigured)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            double sample = samples[i];
            foreach (var section in _sections)
            {
                sample = section.Process(sample);
            }

            samples[i] = (short)Math.Clamp(Math.Round(sample), short.MinValue, short.MaxValue);
        }
    }

    private sealed class Section
    {
        private double _b0;
        private double _b1;
        private double _b2;
        private double _a1;
        private double _a2;
        private double _x1;
        private double _x2;
        private double _y1;
        private double _y2;

        public void SetLowPass(int sampleRate, double cutoffHz, double q)
        {
            var w0 = 2.0 * Math.PI * cutoffHz / sampleRate;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2.0 * q);
            var a0 = 1.0 + alpha;

            _b0 = (1.0 - cos) / 2.0 / a0;
            _b1 = (1.0 - cos) / a0;
            _b2 = _b0;
            _a1 = -2.0 * cos / a0;
            _a2 = (1.0 - alpha) / a0;
            Reset();
        }

        public void Reset()
        {
            _x1 = 0;
            _x2 = 0;
            _y1 = 0;
            _y2 = 0;
        }

        public double Process(double x)
        {
            var y = (_b0 * x) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
