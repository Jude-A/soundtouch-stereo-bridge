using NAudio.Wave;
using StereoBridge.Core;

namespace StereoBridge.Core.Tests;

public class LiveAudioBufferTests
{
    private static byte[] Frames(int first, int count)
    {
        var samples = Enumerable.Range(first, count).SelectMany(i => new[] { (float)i, -(float)i }).ToArray();
        var data = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, data, 0, data.Length);
        return data;
    }
    private static float[] Read(LiveAudioBuffer buffer, int frames)
    {
        var bytes = new byte[frames * 8];
        buffer.Read(bytes, 0, bytes.Length);
        var samples = new float[frames * 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }
    [Fact]
    public void OverflowKeepsNewestFramesWithoutEmptyingQueueOrSplittingChannels()
    {
        var queue = new LiveAudioBuffer(WaveFormat.CreateIeeeFloatWaveFormat(1000, 2), 10);
        queue.Write(Frames(1, 8), 64);
        queue.Write(Frames(9, 6), 48);
        Assert.Equal(10, queue.BufferedMilliseconds);
        Assert.Equal(4, queue.DroppedFrames);
        Assert.Equal(1, queue.TrimCount);
        Assert.Equal(Enumerable.Range(5, 10).SelectMany(i => new[] { (float)i, -(float)i }), Read(queue, 10));
    }
    [Fact]
    public void LargeCapturePacketKeepsTailWithinLimit()
    {
        var queue = new LiveAudioBuffer(WaveFormat.CreateIeeeFloatWaveFormat(1000, 2), 10);
        queue.Write(Frames(1, 3), 24);
        queue.Write(Frames(4, 20), 160);
        Assert.Equal(13, queue.DroppedFrames);
        Assert.Equal(Enumerable.Range(14, 10).SelectMany(i => new[] { (float)i, -(float)i }), Read(queue, 10));
    }
    [Fact]
    public void CalibrationBypassesLiveLimitAndSurvivesUnchanged()
    {
        var queue = new LiveAudioBuffer(WaveFormat.CreateIeeeFloatWaveFormat(1000, 2), 10);
        var signal = Frames(1, 1350);
        queue.Write(signal, signal.Length, preserveSignal: true);
        Assert.Equal(1350, queue.BufferedMilliseconds);
        Assert.Equal(0, queue.TrimCount);
        Assert.Equal(Enumerable.Range(1, 1350).SelectMany(i => new[] { (float)i, -(float)i }), Read(queue, 1350));
        queue.Clear();
        Assert.All(Read(queue, 10), value => Assert.Equal(0, value));
    }
    [Fact]
    public void RejectsPartialFrames()
    {
        var queue = new LiveAudioBuffer(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 40);
        Assert.Throws<ArgumentException>(() => queue.Write(new byte[7], 7));
    }
}
