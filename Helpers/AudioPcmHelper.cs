namespace CallAnalog.Softphone.Helpers;

internal static class AudioPcmHelper
{
    internal static float ComputeRms(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded <= 0)
        {
            return 0;
        }

        double sum = 0;
        var samples = bytesRecorded / 2;
        for (var i = 0; i < samples; i++)
        {
            var sample = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / Math.Max(1, samples));
    }

    internal static byte[] MixPcm(byte[] mic, byte[] remote)
    {
        var length = Math.Min(mic.Length, remote.Length);
        var mixed = new byte[length];

        for (var i = 0; i < length; i += 2)
        {
            if (i + 1 >= length)
            {
                break;
            }

            var micSample = BitConverter.ToInt16(mic, i);
            var remoteSample = BitConverter.ToInt16(remote, i);
            var sum = Math.Clamp(micSample + remoteSample, short.MinValue, short.MaxValue);
            var bytes = BitConverter.GetBytes((short)sum);
            mixed[i] = bytes[0];
            mixed[i + 1] = bytes[1];
        }

        return mixed;
    }
}
