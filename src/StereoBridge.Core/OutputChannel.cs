using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StereoBridge.Core;

/// <summary>One isolated output pipeline: buffer, sample-rate conversion, WASAPI render.</summary>
internal sealed class OutputChannel : IDisposable
{
    private readonly LiveAudioBuffer _buffer;
    private readonly AudioOutput _output;
    private readonly MMDevice _device;
    private readonly string _deviceName;
    private readonly int _sourceSampleRate;
    private readonly int _outputSampleRate;
    private readonly BufferMonitorSampleProvider _bufferMonitor;
    private readonly AlignmentDelayProvider _alignmentDelay;
    private readonly GainLimiterSampleProvider _gainLimiter;
    private readonly StereoChannelProvider _channelMapper;
    private int _disposed;
    private int _writeFailureReported;
    private int _peakBufferedHundredths;

    public OutputChannel(
        MMDevice device,
        WaveFormat captureFormat,
        StereoSide side,
        int alignmentDelayFrames,
        float gain,
        PlaybackMode playbackMode,
        int latencyMilliseconds,
        int maximumBacklogMilliseconds,
        bool minimumBuffer = false)
    {
        _deviceName = device.FriendlyName;
        using var formatClient = device.AudioClient;
        var mixFormat = formatClient.MixFormat;

        if (captureFormat.Channels != mixFormat.Channels)
        {
            throw new NotSupportedException(
                $"'{_deviceName}' uses {mixFormat.Channels} channels, but the Windows source uses {captureFormat.Channels}.");
        }

        _buffer = new LiveAudioBuffer(captureFormat, maximumBacklogMilliseconds);
        _sourceSampleRate = captureFormat.SampleRate;
        _outputSampleRate = mixFormat.SampleRate;

        Side = side;
        _bufferMonitor = new BufferMonitorSampleProvider(_buffer);
        _channelMapper = new StereoChannelProvider(_bufferMonitor, side, playbackMode);
        ISampleProvider pipeline = _channelMapper;
        _alignmentDelay = new AlignmentDelayProvider(pipeline, alignmentDelayFrames);
        pipeline = _alignmentDelay;
        if (captureFormat.SampleRate != mixFormat.SampleRate)
        {
            pipeline = new WdlResamplingSampleProvider(pipeline, mixFormat.SampleRate);
        }
        _gainLimiter = new GainLimiterSampleProvider(pipeline, gain);
        pipeline = _gainLimiter;

        _output = new AudioOutput(device, pipeline, latencyMilliseconds, minimumBuffer);
        _output.PlaybackStopped += OnPlaybackStopped;
        _device = device;
        BridgeLog.Write(
            $"{side} output started: '{_deviceName}', {captureFormat.SampleRate} Hz -> {mixFormat.SampleRate} Hz, " +
            $"requested latency={latencyMilliseconds} ms, max backlog={maximumBacklogMilliseconds} ms, " +
            $"backend={_output.Backend}, actual WASAPI buffer={_output.BufferMilliseconds:0.##} ms, reported latency={_output.ReportedLatencyMilliseconds:0.##} ms.");
    }

    public void StartPlayback() => _output.Play();

    public string DeviceName => _deviceName;

    public StereoSide Side { get; }

    public int AlignmentDelayFrames
    {
        get => _alignmentDelay.DelayFrames;
        set => _alignmentDelay.DelayFrames = value;
    }

    public float Gain
    {
        get => _gainLimiter.Gain;
        set => _gainLimiter.Gain = value;
    }

    public PlaybackMode PlaybackMode
    {
        get => _channelMapper.Mode;
        set => _channelMapper.Mode = value;
    }

    public event EventHandler<Exception?>? PlaybackStopped;

    public OutputPipelineDiagnostics GetDiagnostics() => new(
        Side,
        _deviceName,
        _buffer.BufferedMilliseconds,
        Volatile.Read(ref _peakBufferedHundredths) / 100d,
        _bufferMonitor.UnderrunCount,
        _buffer.TrimCount,
        _sourceSampleRate,
        _outputSampleRate,
        _gainLimiter.LimiterActive,
        _output.BufferMilliseconds, _output.ReportedLatencyMilliseconds, _output.Backend,
        _buffer.DroppedFrames * 1000d / _sourceSampleRate);

    public void ClearBuffer()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _buffer.Clear();
            _alignmentDelay.RequestReset();
        }
    }

    public void Write(byte[] buffer, int count, bool preserveSignal = false)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _buffer.Write(buffer, count, preserveSignal);
            _bufferMonitor.NotifyWrite();
            if (!preserveSignal) RecordPeakBuffer();
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref _writeFailureReported, 1) == 0)
            {
                BridgeLog.Write($"Output write failed for '{_deviceName}': {error.Message}");
                PlaybackStopped?.Invoke(this, error);
            }
        }
    }

    private void RecordPeakBuffer()
    {
        var observed = (int)Math.Round(_buffer.BufferedMilliseconds * 100);
        var current = Volatile.Read(ref _peakBufferedHundredths);
        while (observed > current)
        {
            var previous = Interlocked.CompareExchange(ref _peakBufferedHundredths, observed, current);
            if (previous == current)
            {
                return;
            }
            current = previous;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _output.PlaybackStopped -= OnPlaybackStopped;
        try
        {
            _output.Stop();
            _output.Dispose();
        }
        catch (Exception error)
        {
            BridgeLog.Write($"Output teardown failed for '{_deviceName}': {error.Message}");
        }

        try
        {
            _device.Dispose();
        }
        catch (Exception error)
        {
            BridgeLog.Write($"Device teardown failed for '{_deviceName}': {error.Message}");
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        BridgeLog.Write($"Output stopped unexpectedly: '{_deviceName}', {eventArgs.Exception?.Message ?? "no driver error"}.");
        PlaybackStopped?.Invoke(this, eventArgs.Exception);
    }
}
