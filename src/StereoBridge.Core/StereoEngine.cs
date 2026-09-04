using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>
/// Captures the default Windows render endpoint and fans it out to two independent outputs.
/// The left endpoint receives [L,L] and the right endpoint receives [R,R].
/// </summary>
public sealed class StereoEngine : IDisposable
{
    private WasapiCapture? _capture;
    private MMDevice? _source;
    private volatile OutputChannel[] _outputs = Array.Empty<OutputChannel>();
    private int _running;
    private int _faultReported;
    private int _testing;
    private int _calibrating;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public string? SourceName { get; private set; }

    public int CaptureSampleRate { get; private set; } = 48_000;

    public PlaybackMode CurrentPlaybackMode { get; private set; } = PlaybackMode.Stereo;

    public LatencyProfile ActiveLatencyProfile { get; private set; } = LatencyProfile.Stable;

    /// <summary>Raised from an audio thread. UI consumers must marshal the callback.</summary>
    public event EventHandler<string>? PlaybackFaulted;

    public void Start(
        string leftEndpointId,
        string rightEndpointId,
        int leftDelaySamples = 0,
        int rightDelaySamples = 0,
        float leftGain = 1f,
        float rightGain = 1f,
        PlaybackMode playbackMode = PlaybackMode.Stereo,
        LatencyProfile latencyProfile = LatencyProfile.Stable)
    {
        if (string.IsNullOrWhiteSpace(leftEndpointId) || string.IsNullOrWhiteSpace(rightEndpointId))
        {
            throw new ArgumentException("Select both output devices.");
        }

        if (string.Equals(leftEndpointId, rightEndpointId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Left and right outputs must be different devices.");
        }

        var latencySettings = LatencyProfileSettings.For(latencyProfile);

        Stop();
        Interlocked.Exchange(ref _faultReported, 0);

        MMDevice? source = null;
        MMDevice? leftDevice = null;
        MMDevice? rightDevice = null;
        OutputChannel? leftOutput = null;
        OutputChannel? rightOutput = null;
        WasapiCapture? capture = null;

        try
        {
            source = AudioDeviceManager.GetDefaultRenderDevice();
            if (string.Equals(source.ID, leftEndpointId, StringComparison.Ordinal) ||
                string.Equals(source.ID, rightEndpointId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Windows default playback device is the capture source and cannot also be a bridge output. " +
                    "Choose a third endpoint as the Windows default device, then refresh.");
            }

            capture = new ConfigurableLoopbackCapture(source, latencySettings.CaptureBufferMilliseconds);
            CaptureSampleRate = capture.WaveFormat.SampleRate;

            leftDevice = AudioDeviceManager.FindActiveRenderDevice(leftEndpointId)
                ?? throw new InvalidOperationException("The selected left output is no longer available.");
            leftOutput = new OutputChannel(
                leftDevice,
                capture.WaveFormat,
                StereoSide.Left,
                leftDelaySamples,
                leftGain,
                playbackMode,
                latencySettings.OutputBufferMilliseconds,
                latencySettings.MaximumBacklogMilliseconds,
                latencyProfile == LatencyProfile.LowLatency);
            leftDevice = null;

            rightDevice = AudioDeviceManager.FindActiveRenderDevice(rightEndpointId)
                ?? throw new InvalidOperationException("The selected right output is no longer available.");
            rightOutput = new OutputChannel(
                rightDevice,
                capture.WaveFormat,
                StereoSide.Right,
                rightDelaySamples,
                rightGain,
                playbackMode,
                latencySettings.OutputBufferMilliseconds,
                latencySettings.MaximumBacklogMilliseconds,
                latencyProfile == LatencyProfile.LowLatency);
            rightDevice = null;

            leftOutput.PlaybackStopped += OnOutputStopped;
            rightOutput.PlaybackStopped += OnOutputStopped;
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;

            _source = source;
            source = null;
            _capture = capture;
            capture = null;
            _outputs = new[] { leftOutput, rightOutput };
            leftOutput = null;
            rightOutput = null;
            SourceName = _source.FriendlyName;
            CurrentPlaybackMode = playbackMode;
            ActiveLatencyProfile = latencyProfile;

            // Both devices are initialized before either begins consuming audio.
            foreach (var output in _outputs) output.StartPlayback();
            _capture.StartRecording();
            if (Volatile.Read(ref _faultReported) != 0) throw new InvalidOperationException("An output failed during startup.");
            Interlocked.Exchange(ref _running, 1);
            BridgeLog.Write(
                $"Capture started: '{SourceName}', format={_capture.WaveFormat}, " +
                $"profile={latencyProfile}, requested buffer={latencySettings.CaptureBufferMilliseconds} ms.");
        }
        catch
        {
            leftOutput?.Dispose();
            rightOutput?.Dispose();
            leftDevice?.Dispose();
            rightDevice?.Dispose();
            capture?.Dispose();
            source?.Dispose();
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        Interlocked.Exchange(ref _running, 0);
        Interlocked.Exchange(ref _testing, 0);

        var outputs = Interlocked.Exchange(ref _outputs, Array.Empty<OutputChannel>());

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try
            {
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch (Exception error)
            {
                BridgeLog.Write($"Capture teardown failed: {error.Message}");
            }
            _capture = null;
        }

        foreach (var output in outputs)
        {
            output.PlaybackStopped -= OnOutputStopped;
            output.Dispose();
        }

        try
        {
            _source?.Dispose();
        }
        catch (Exception error)
        {
            BridgeLog.Write($"Source teardown failed: {error.Message}");
        }

        _source = null;
        SourceName = null;
    }

    public void Dispose() => Stop();

    public void SetAlignmentDelay(StereoSide side, int delaySamples)
    {
        foreach (var output in _outputs)
        {
            if (output.Side == side)
            {
                output.AlignmentDelayFrames = delaySamples;
                BridgeLog.Write($"{side} alignment delay set to {output.AlignmentDelayFrames} samples.");
                return;
            }
        }
    }

    public void SetOutputGain(StereoSide side, float gain)
    {
        foreach (var output in _outputs)
        {
            if (output.Side == side)
            {
                output.Gain = gain;
                BridgeLog.Write($"{side} digital gain set to {output.Gain:0.00}x.");
                return;
            }
        }
    }

    public void SetPlaybackMode(PlaybackMode mode)
    {
        CurrentPlaybackMode = mode;
        foreach (var output in _outputs)
        {
            output.PlaybackMode = mode;
        }
        BridgeLog.Write($"Playback mode set to {mode}.");
    }

    public PipelineDiagnostics? GetDiagnostics()
    {
        if (!IsRunning || _capture is null)
        {
            return null;
        }

        var outputs = _outputs;
        if (outputs.Length != 2)
        {
            return null;
        }

        return new PipelineDiagnostics(
            ActiveLatencyProfile,
            LatencyProfileSettings.For(ActiveLatencyProfile).CaptureBufferMilliseconds,
            outputs.Select(output => output.GetDiagnostics()).ToArray());
    }

    public async Task PlayChannelTestAsync()
    {
        if (_capture is null)
        {
            throw new InvalidOperationException("Start the stereo bridge before running the channel test.");
        }

        var previousMode = CurrentPlaybackMode;
        SetPlaybackMode(PlaybackMode.Stereo);
        try
        {
            await PlayTestSignalAsync(
                StereoTestSignal.CreateChannelTest(_capture.WaveFormat),
                StereoTestSignal.ChannelTestDurationSeconds).ConfigureAwait(false);
        }
        finally
        {
            SetPlaybackMode(previousMode);
        }
    }

    public async Task PlayAlignmentTestAsync()
    {
        if (_capture is null)
        {
            throw new InvalidOperationException("Start the stereo bridge before running the alignment test.");
        }

        await PlayTestSignalAsync(
            StereoTestSignal.CreateAlignmentClicks(_capture.WaveFormat),
            StereoTestSignal.AlignmentTestDurationSeconds).ConfigureAwait(false);
    }

    public async Task<CalibrationResult> AutoCalibrateAsync(CalibrationParameters parameters)
    {
        const int passCount = 3;
        const double maximumArrivalLatencySeconds = 2.0;
        const double confidenceThreshold = 0.08;
        const double maximumStandardDeviationMilliseconds = 10.0;

        parameters.Validate();
        if (!IsRunning || _capture is null)
        {
            throw new InvalidOperationException("Start the stereo bridge before calibrating.");
        }

        if (Interlocked.CompareExchange(ref _calibrating, 1, 0) != 0)
        {
            throw new InvalidOperationException("Automatic calibration is already running.");
        }

        var previousMode = CurrentPlaybackMode;
        var passes = new List<(double DifferenceMilliseconds, double Confidence)>();
        MicrophoneRecorder? recorder = null;

        try
        {
            recorder = new MicrophoneRecorder();
            SetPlaybackMode(PlaybackMode.Stereo);
            recorder.Start();
            await Task.Delay(200).ConfigureAwait(false);

            var leftReference = StereoTestSignal.CreateCalibrationReference(recorder.SampleRate, StereoSide.Left);
            var rightReference = StereoTestSignal.CreateCalibrationReference(recorder.SampleRate, StereoSide.Right);

            for (var pass = 0; pass < passCount; pass++)
            {
                if (!IsRunning)
                {
                    throw new InvalidOperationException("The stereo bridge stopped during calibration.");
                }

                var markerSample = recorder.SampleCount;
                await PlayTestSignalAsync(
                    StereoTestSignal.CreateCalibrationSequence(_capture.WaveFormat),
                    StereoTestSignal.CalibrationDurationSeconds + maximumArrivalLatencySeconds)
                    .ConfigureAwait(false);

                var recording = recorder.Snapshot();
                var leftPeak = FindCalibrationPeak(
                    recording,
                    leftReference,
                    recorder.SampleRate,
                    markerSample,
                    StereoTestSignal.CalibrationLeftStartSeconds,
                    maximumArrivalLatencySeconds);
                var rightPeak = FindCalibrationPeak(
                    recording,
                    rightReference,
                    recorder.SampleRate,
                    markerSample,
                    StereoTestSignal.CalibrationRightStartSeconds,
                    maximumArrivalLatencySeconds);

                var measuredDifferenceMilliseconds =
                    ((rightPeak.SampleIndex - leftPeak.SampleIndex) / (double)recorder.SampleRate -
                     (StereoTestSignal.CalibrationRightStartSeconds - StereoTestSignal.CalibrationLeftStartSeconds)) *
                    1_000;
                var passConfidence = Math.Min(leftPeak.Confidence, rightPeak.Confidence);
                passes.Add((measuredDifferenceMilliseconds, passConfidence));
                BridgeLog.Write(
                    $"Calibration pass {pass + 1}/{passCount}: R-L={measuredDifferenceMilliseconds:0.00} ms, " +
                    $"confidence={passConfidence:0.000}.");

                await Task.Delay(150).ConfigureAwait(false);
            }

            var measuredDifferences = passes.Select(value => value.DifferenceMilliseconds).Order().ToArray();
            var measuredDifference = measuredDifferences[measuredDifferences.Length / 2];
            var standardDeviation = Math.Sqrt(
                passes.Average(value => Math.Pow(value.DifferenceMilliseconds - measuredDifference, 2)));
            var confidence = passes.Min(value => value.Confidence);
            var geometryCorrection = CalibrationMath.GeometryCorrectionMilliseconds(parameters);
            var listeningDifference = measuredDifference + geometryCorrection;

            var outputs = _outputs;
            if (outputs.Length != 2)
            {
                throw new InvalidOperationException("Both output pipelines must be running.");
            }

            var currentLeftDelay = outputs.Single(output => output.Side == StereoSide.Left).AlignmentDelayFrames;
            var currentRightDelay = outputs.Single(output => output.Side == StereoSide.Right).AlignmentDelayFrames;
            var delays = CalibrationMath.CalculateAbsoluteDelays(
                listeningDifference,
                currentLeftDelay,
                currentRightDelay,
                CaptureSampleRate);
            var leftDelay = Math.Clamp(delays.LeftDelaySamples, 0, CaptureSampleRate);
            var rightDelay = Math.Clamp(delays.RightDelaySamples, 0, CaptureSampleRate);
            var withinDelayRange = delays.LeftDelaySamples <= CaptureSampleRate &&
                                   delays.RightDelaySamples <= CaptureSampleRate;
            var applied = confidence >= confidenceThreshold &&
                          standardDeviation <= maximumStandardDeviationMilliseconds &&
                          withinDelayRange;

            if (applied)
            {
                SetAlignmentDelay(StereoSide.Left, leftDelay);
                SetAlignmentDelay(StereoSide.Right, rightDelay);
            }

            BridgeLog.Write(
                $"Calibration result: measured R-L={measuredDifference:0.00} ms, " +
                $"geometry={geometryCorrection:+0.00;-0.00;0.00} ms, listener R-L={listeningDifference:0.00} ms, " +
                $"confidence={confidence:0.000}, deviation={standardDeviation:0.00} ms, applied={applied}.");

            return new CalibrationResult(
                recorder.DeviceName,
                measuredDifference,
                geometryCorrection,
                listeningDifference,
                confidence,
                standardDeviation,
                leftDelay,
                rightDelay,
                applied);
        }
        finally
        {
            try
            {
                if (recorder is not null)
                {
                    await recorder.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                recorder?.Dispose();
                SetPlaybackMode(previousMode);
                Interlocked.Exchange(ref _calibrating, 0);
            }
        }
    }

    private async Task PlayTestSignalAsync(byte[] signal, double durationSeconds)
    {
        if (!IsRunning || _capture is null)
        {
            throw new InvalidOperationException("Start the stereo bridge before running the channel test.");
        }

        if (Interlocked.CompareExchange(ref _testing, 1, 0) != 0)
        {
            return;
        }

        var outputs = _outputs;
        try
        {
            if (outputs.Length != 2)
            {
                throw new InvalidOperationException("Both output pipelines must be running.");
            }

            foreach (var output in outputs)
            {
                output.ClearBuffer();
            }

            foreach (var output in outputs)
            {
                output.Write(signal, signal.Length, preserveSignal: true);
            }

            var largestDelaySeconds = outputs.Max(output => output.AlignmentDelayFrames) /
                (double)_capture.WaveFormat.SampleRate;
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds + largestDelaySeconds + 0.35)).ConfigureAwait(false);
        }
        finally
        {
            foreach (var output in outputs)
            {
                output.ClearBuffer();
            }
            Interlocked.Exchange(ref _testing, 0);
        }
    }

    private static CorrelationPeak FindCalibrationPeak(
        IReadOnlyList<float> recording,
        IReadOnlyList<float> reference,
        int sampleRate,
        int markerSample,
        double sourceStartSeconds,
        double maximumArrivalLatencySeconds)
    {
        var searchStart = markerSample +
            (int)Math.Round((sourceStartSeconds - 0.05) * sampleRate);
        var searchEnd = markerSample +
            (int)Math.Round((sourceStartSeconds + maximumArrivalLatencySeconds) * sampleRate);
        if (recording.Count < searchEnd + reference.Count)
        {
            throw new InvalidOperationException("The microphone recording ended before the calibration signal arrived.");
        }

        return CorrelationAnalyzer.FindPeak(recording, reference, searchStart, searchEnd);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (Volatile.Read(ref _testing) != 0)
        {
            return;
        }

        foreach (var output in _outputs)
        {
            output.Write(eventArgs.Buffer, eventArgs.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        ReportFault($"The Windows capture source stopped: {eventArgs.Exception?.Message ?? "device unavailable"}");
    }

    private void OnOutputStopped(object? sender, Exception? error)
    {
        var outputName = sender is OutputChannel output ? output.DeviceName : "an output";
        ReportFault($"'{outputName}' stopped: {error?.Message ?? "device unavailable"}");
    }

    private void ReportFault(string message)
    {
        if (Interlocked.Exchange(ref _faultReported, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _running, 0);
        BridgeLog.Write(message);
        PlaybackFaulted?.Invoke(this, message);
    }
}
