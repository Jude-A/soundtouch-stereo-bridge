using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StereoBridge.Core;

internal sealed class MicrophoneRecorder : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly object _samplesLock = new();
    private readonly List<float> _monoSamples = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;

    public MicrophoneRecorder()
    {
        _device = AudioDeviceManager.GetDefaultCaptureDevice();
        DeviceName = _device.FriendlyName;
        _capture = new WasapiCapture(_device, useEventSync: true, audioBufferMillisecondsLength: 20);
        if (_capture.WaveFormat.BitsPerSample != 32)
        {
            _capture.Dispose();
            _device.Dispose();
            throw new NotSupportedException("Automatic calibration requires a 32-bit float microphone format.");
        }

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public string DeviceName { get; }

    public int SampleRate => _capture.WaveFormat.SampleRate;

    public int SampleCount
    {
        get
        {
            lock (_samplesLock)
            {
                return _monoSamples.Count;
            }
        }
    }

    public void Start()
    {
        _capture.StartRecording();
        _started = true;
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _capture.StopRecording();
        await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    public float[] Snapshot()
    {
        lock (_samplesLock)
        {
            return _monoSamples.ToArray();
        }
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();
        _device.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        var format = _capture.WaveFormat;
        var frameCount = eventArgs.BytesRecorded / format.BlockAlign;
        var captured = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            var frameByteOffset = frame * format.BlockAlign;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                sum += BitConverter.ToSingle(eventArgs.Buffer, frameByteOffset + (channel * sizeof(float)));
            }
            captured[frame] = (float)(sum / format.Channels);
        }

        lock (_samplesLock)
        {
            _monoSamples.AddRange(captured);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            _stopped.TrySetException(eventArgs.Exception);
        }
        else
        {
            _stopped.TrySetResult();
        }
    }
}
