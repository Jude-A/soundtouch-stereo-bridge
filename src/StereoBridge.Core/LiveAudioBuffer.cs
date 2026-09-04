using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>Frame-aligned live queue. Trims only old excess; calibration signals bypass the live limit.</summary>
public sealed class LiveAudioBuffer : IWaveProvider
{
    private readonly BufferedWaveProvider _buffer;
    private readonly object _gate = new();
    private readonly byte[] _discard;
    private readonly int _maximumBytes;
    public WaveFormat WaveFormat => _buffer.WaveFormat;
    public long TrimCount { get; private set; }
    public long DroppedFrames { get; private set; }
    public double BufferedMilliseconds { get { lock (_gate) return _buffer.BufferedDuration.TotalMilliseconds; } }
    public int BufferedBytes { get { lock (_gate) return _buffer.BufferedBytes; } }

    public LiveAudioBuffer(WaveFormat format, int maximumMilliseconds)
    {
        if (maximumMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(maximumMilliseconds));
        _buffer = new BufferedWaveProvider(format) { BufferDuration = TimeSpan.FromSeconds(2), ReadFully = true };
        _maximumBytes = Math.Max(1, format.SampleRate * maximumMilliseconds / 1000) * format.BlockAlign;
        _discard = new byte[Math.Max(format.BlockAlign, format.AverageBytesPerSecond / 100 / format.BlockAlign * format.BlockAlign)];
    }

    public void Write(byte[] data, int count, bool preserveSignal = false)
    {
        if (count < 0 || count > data.Length || count % WaveFormat.BlockAlign != 0)
            throw new ArgumentException("Audio writes must contain complete frames.");
        lock (_gate)
        {
            var offset = 0;
            if (!preserveSignal)
            {
                var excess = Math.Max(0, _buffer.BufferedBytes + count - _maximumBytes);
                if (excess > 0)
                {
                    TrimCount++;
                    DroppedFrames += excess / WaveFormat.BlockAlign;
                    var discard = Math.Min(excess, _buffer.BufferedBytes);
                    offset = excess - discard;
                    while (discard > 0)
                    {
                        var read = _buffer.Read(_discard, 0, Math.Min(discard, _discard.Length));
                        discard -= read;
                    }
                }
            }
            _buffer.AddSamples(data, offset, count - offset);
        }
    }

    public int Read(byte[] buffer, int offset, int count) { lock (_gate) return _buffer.Read(buffer, offset, count); }
    public void Clear() { lock (_gate) _buffer.ClearBuffer(); }
}
