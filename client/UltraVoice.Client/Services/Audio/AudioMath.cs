namespace UltraVoice.Client.Services.Audio;

internal static class AudioMath
{
    public static float CalculateRms(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
        {
            return 0f;
        }

        double sumSquares = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var normalized = samples[i] / 32768.0;
            sumSquares += normalized * normalized;
        }

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }
}
