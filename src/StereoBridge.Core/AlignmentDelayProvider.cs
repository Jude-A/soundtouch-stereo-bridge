using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>A sample-frame-accurate stereo delay backed by a fixed circular buffer.</summary>
public sealed class AlignmentDelayProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _delayBuffer;
    private readonly int _bufferFrameCount;
    private float[] _readBuffer = Array.Empty<float>();
    private int _writeFrame;
    private int _delayFrames;
    private int _resetRequested;

    public AlignmentDelayProvider(ISampleProvider source, int delayFrames = 0, int maximumDelayMilliseconds = 1_000)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException("AlignmentDelayProvider requires a two-channel source.", nameof(source));
        }
        if (maximumDelayMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDelayMilliseconds));
        }

        _source = source;
        WaveFormat = source.WaveFormat;
        MaximumDelayFrames = (int)Math.Ceiling(WaveFormat.SampleRate * maximumDelayMilliseconds / 1_000d);
        _bufferFrameCount = MaximumDelayFrames + 1;
        _delayBuffer = new float[_bufferFrameCount * WaveFormat.Channels];
        DelayFrames = delayFrames;
    }

    public WaveFormat WaveFormat { get; }

    public int MaximumDelayFrames { get; }

    public int DelayFrames
    {
        get => Volatile.Read(ref _delayFrames);
        set => Volatile.Write(ref _delayFrames, Math.Clamp(value, 0, MaximumDelayFrames));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (Interlocked.Exchange(ref _resetRequested, 0) != 0)
        {
            Array.Clear(_delayBuffer);
            _writeFrame = 0;
        }

        if (_readBuffer.Length < count)
        {
            _readBuffer = new float[count];
        }

        var samplesRead = _source.Read(_readBuffer, 0, count);
        if (samplesRead % WaveFormat.Channels != 0)
        {
            throw new InvalidOperationException("The stereo source returned a partial sample frame.");
        }

        var delayFrames = DelayFrames;
        for (var sourceIndex = 0; sourceIndex < samplesRead; sourceIndex += WaveFormat.Channels)
        {
            var writeIndex = _writeFrame * WaveFormat.Channels;
            _delayBuffer[writeIndex] = _readBuffer[sourceIndex];
            _delayBuffer[writeIndex + 1] = _readBuffer[sourceIndex + 1];

            var readFrame = _writeFrame - delayFrames;
            if (readFrame < 0)
            {
                readFrame += _bufferFrameCount;
            }
            var readIndex = readFrame * WaveFormat.Channels;
            buffer[offset + sourceIndex] = _delayBuffer[readIndex];
            buffer[offset + sourceIndex + 1] = _delayBuffer[readIndex + 1];

            _writeFrame++;
            if (_writeFrame == _bufferFrameCount)
            {
                _writeFrame = 0;
            }
        }

        return samplesRead;
    }

    public void RequestReset() => Interlocked.Exchange(ref _resetRequested, 1);
}
