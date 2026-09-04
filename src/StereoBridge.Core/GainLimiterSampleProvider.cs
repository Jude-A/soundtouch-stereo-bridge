using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>Applies per-output gain with a smooth ceiling near full scale.</summary>
public sealed class GainLimiterSampleProvider : ISampleProvider
{
    private const float LimitThreshold = 0.90f;
    private const float LimitRange = 1f - LimitThreshold;
    private readonly ISampleProvider _source;
    private float _gain = 1f;
    private long _lastLimitedAtMilliseconds = -1;

    public GainLimiterSampleProvider(ISampleProvider source, float gain = 1f)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        WaveFormat = source.WaveFormat;
        Gain = gain;
    }

    public WaveFormat WaveFormat { get; }

    public float Gain
    {
        get => Volatile.Read(ref _gain);
        set => Volatile.Write(ref _gain, Math.Clamp(value, 0f, 3f));
    }

    public bool LimiterActive
    {
        get
        {
            var lastLimitedAt = Volatile.Read(ref _lastLimitedAtMilliseconds);
            return lastLimitedAt >= 0 && Environment.TickCount64 - lastLimitedAt < 1_000;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        var gain = Gain;

        for (var index = offset; index < offset + samplesRead; index++)
        {
            var amplified = buffer[index] * gain;
            var magnitude = Math.Abs(amplified);
            if (magnitude <= LimitThreshold)
            {
                buffer[index] = amplified;
                continue;
            }

            Volatile.Write(ref _lastLimitedAtMilliseconds, Environment.TickCount64);

            var limitedMagnitude = LimitThreshold +
                (LimitRange * (1f - MathF.Exp(-(magnitude - LimitThreshold) / LimitRange)));
            buffer[index] = MathF.CopySign(limitedMagnitude, amplified);
        }

        return samplesRead;
    }
}
