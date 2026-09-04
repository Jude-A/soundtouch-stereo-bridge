using NAudio.Wave;

namespace StereoBridge.Core;

internal sealed class BufferMonitorSampleProvider : ISampleProvider
{
    private readonly LiveAudioBuffer _buffer;
    private readonly ISampleProvider _source;
    private long _lastWriteAtMilliseconds = -1;
    private long _underrunCount;

    public BufferMonitorSampleProvider(LiveAudioBuffer buffer)
    {
        _buffer = buffer;
        _source = buffer.ToSampleProvider();
        WaveFormat = _source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public long UnderrunCount => Interlocked.Read(ref _underrunCount);

    public void NotifyWrite() =>
        Volatile.Write(ref _lastWriteAtMilliseconds, Environment.TickCount64);

    public int Read(float[] buffer, int offset, int count)
    {
        var requestedBytes = count * sizeof(float);
        var lastWriteAt = Volatile.Read(ref _lastWriteAtMilliseconds);
        var recentlyReceivedAudio =
            lastWriteAt >= 0 && Environment.TickCount64 - lastWriteAt < 500;
        if (recentlyReceivedAudio && _buffer.BufferedBytes < requestedBytes)
        {
            Interlocked.Increment(ref _underrunCount);
        }

        return _source.Read(buffer, offset, count);
    }
}
