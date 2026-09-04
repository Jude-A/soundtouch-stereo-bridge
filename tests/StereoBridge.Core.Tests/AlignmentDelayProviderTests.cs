using NAudio.Wave;

namespace StereoBridge.Core.Tests;

public sealed class AlignmentDelayProviderTests
{
    [Fact]
    public void Read_DelaysWholeStereoFramesExactly()
    {
        var source = new ArraySampleProvider(new float[] { 1, 10, 2, 20, 3, 30 });
        var provider = new AlignmentDelayProvider(source, delayFrames: 2);
        var actual = new float[6];

        var samplesRead = provider.Read(actual, 0, actual.Length);

        Assert.Equal(6, samplesRead);
        Assert.Equal(new float[] { 0, 0, 0, 0, 1, 10 }, actual);
    }

    [Fact]
    public void Read_WithZeroDelay_IsTransparent()
    {
        var expected = new float[] { 1, 10, 2, 20 };
        var provider = new AlignmentDelayProvider(new ArraySampleProvider(expected), delayFrames: 0);
        var actual = new float[expected.Length];

        provider.Read(actual, 0, actual.Length);

        Assert.Equal(expected, actual);
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
