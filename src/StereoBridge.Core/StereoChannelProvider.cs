using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>
/// Maps one channel from an interleaved stereo stream back to a conventional stereo stream.
/// Left maps [L,R] to [L,L]; right maps [L,R] to [R,R].
/// </summary>
public sealed class StereoChannelProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannelIndex;
    private float[] _readBuffer = Array.Empty<float>();
    private int _mode;

    public StereoChannelProvider(ISampleProvider source, StereoSide side, PlaybackMode mode = PlaybackMode.Stereo)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels != 2)
        {
            throw new ArgumentException("StereoChannelProvider requires a two-channel source.", nameof(source));
        }

        _source = source;
        _sourceChannelIndex = side == StereoSide.Left ? 0 : 1;
        Mode = mode;
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    public PlaybackMode Mode
    {
        get => (PlaybackMode)Volatile.Read(ref _mode);
        set => Volatile.Write(ref _mode, (int)value);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_readBuffer.Length < count)
        {
            _readBuffer = new float[count];
        }

        var samplesRead = _source.Read(_readBuffer, 0, count);
        if (samplesRead % 2 != 0)
        {
            throw new InvalidOperationException("The stereo source returned a partial sample frame.");
        }

        var mode = Mode;
        for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex += 2)
        {
            var monoSample = mode == PlaybackMode.Mono
                ? (_readBuffer[sampleIndex] + _readBuffer[sampleIndex + 1]) * 0.5f
                : _readBuffer[sampleIndex + _sourceChannelIndex];
            buffer[offset + sampleIndex] = monoSample;
            buffer[offset + sampleIndex + 1] = monoSample;
        }

        return samplesRead;
    }
}
