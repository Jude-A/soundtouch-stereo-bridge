using NAudio.Wave;

namespace StereoBridge.Core.Tests;

public sealed class StereoChannelProviderTests
{
    [Theory]
    [InlineData(StereoSide.Left, new float[] { 1f, 1f, 2f, 2f })]
    [InlineData(StereoSide.Right, new float[] { 10f, 10f, 20f, 20f })]
    public void Read_DuplicatesSelectedChannelAcrossStereo(StereoSide side, float[] expected)
    {
        var source = new ArraySampleProvider(new float[] { 1f, 10f, 2f, 20f });
        var provider = new StereoChannelProvider(source, side);
        var actual = new float[4];

        var samplesRead = provider.Read(actual, 0, actual.Length);

        Assert.Equal(4, samplesRead);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Read_InMonoMode_AveragesBothChannelsForBothOutputs()
    {
        var source = new ArraySampleProvider(new float[] { 1f, 3f, -2f, 2f });
        var provider = new StereoChannelProvider(source, StereoSide.Left, PlaybackMode.Mono);
        var actual = new float[4];

        provider.Read(actual, 0, actual.Length);

        Assert.Equal(new float[] { 2f, 2f, 0f, 0f }, actual);
    }

    private sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;

        public ArraySampleProvider(float[] samples)
        {
            _samples = samples;
        }

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
