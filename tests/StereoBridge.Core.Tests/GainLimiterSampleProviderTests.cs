using NAudio.Wave;

namespace StereoBridge.Core.Tests;

public sealed class GainLimiterSampleProviderTests
{
    [Fact]
    public void Read_AmplifiesSignalsBelowLimiterThreshold()
    {
        var provider = new GainLimiterSampleProvider(new ArraySampleProvider(new float[] { 0.2f, -0.2f }), gain: 2f);
        var actual = new float[2];

        provider.Read(actual, 0, actual.Length);

        Assert.Equal(0.4f, actual[0], precision: 5);
        Assert.Equal(-0.4f, actual[1], precision: 5);
    }

    [Fact]
    public void Read_SmoothlyLimitsBoostedPeaksToFullScale()
    {
        var provider = new GainLimiterSampleProvider(new ArraySampleProvider(new float[] { 1f, -1f }), gain: 3f);
        var actual = new float[2];

        provider.Read(actual, 0, actual.Length);

        Assert.InRange(actual[0], 0.9f, 1f);
        Assert.InRange(actual[1], -1f, -0.9f);
    }

    private sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public ArraySampleProvider(float[] samples) => _samples = samples;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesToCopy = Math.Min(count, _samples.Length - _position);
            Array.Copy(_samples, _position, buffer, offset, samplesToCopy);
            _position += samplesToCopy;
            return samplesToCopy;
        }
    }
}
