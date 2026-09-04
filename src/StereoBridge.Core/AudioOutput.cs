using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>Shared WASAPI with a minimum-size buffer, or the established NAudio fallback.</summary>
internal sealed class AudioOutput : IDisposable
{
    private readonly WasapiOut? _fallback;
    private readonly AudioClient? _client;
    private readonly IWaveProvider? _source;
    private readonly AutoResetEvent _ready = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly ManualResetEventSlim _started = new(false);
    private Exception? _startupError;
    private Thread? _thread;
    private int _disposed;

    public double? BufferMilliseconds { get; }
    public double? ReportedLatencyMilliseconds { get; }
    public string Backend { get; }
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public AudioOutput(MMDevice device, ISampleProvider source, int requestedMilliseconds, bool minimumBuffer)
    {
        if (minimumBuffer)
        {
            AudioClient? client = null;
            try
            {
                client = device.AudioClient;
                // Zero duration asks WASAPI for its minimum shared-mode buffer.
                client.Initialize(AudioClientShareMode.Shared,
                    AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm |
                    AudioClientStreamFlags.SrcDefaultQuality, 0, 0, source.WaveFormat, Guid.Empty);
                client.SetEventHandle(_ready.SafeWaitHandle.DangerousGetHandle());
                BufferMilliseconds = client.BufferSize * 1000d / source.WaveFormat.SampleRate;
                ReportedLatencyMilliseconds = client.StreamLatency / 10000d;
                if (client.BufferSize <= 0) throw new InvalidOperationException("WASAPI returned an empty buffer.");
                _client = client;
                _source = source.ToWaveProvider();
                Backend = "WASAPI minimum";
                return;
            }
            catch (Exception error)
            {
                client?.Dispose();
                BufferMilliseconds = null;
                ReportedLatencyMilliseconds = null;
                BridgeLog.Write($"Minimum WASAPI unavailable for '{device.FriendlyName}': {error.Message}; using NAudio.");
            }
        }

        WasapiOut? fallback = null;
        try
        {
            fallback = CreateFallback(device, source, requestedMilliseconds, true);
        }
        catch (Exception error)
        {
            BridgeLog.Write($"Event-sync unavailable for '{device.FriendlyName}': {error.Message}; using polling.");
            try { fallback = CreateFallback(device, source, requestedMilliseconds, false); }
            catch { _ready.Dispose(); _stop.Dispose(); _started.Dispose(); throw; }
        }
        _fallback = fallback;
        _fallback.PlaybackStopped += ForwardStopped;
        Backend = "NAudio";
    }

    private static WasapiOut CreateFallback(MMDevice device, ISampleProvider source, int latency, bool events)
    {
        var output = new WasapiOut(device, AudioClientShareMode.Shared, events, latency);
        try { output.Init(source); return output; }
        catch { output.Dispose(); throw; }
    }

    public void Play()
    {
        if (_fallback is not null) { _fallback.Play(); return; }
        if (_thread is not null) throw new InvalidOperationException("Output already started.");
        _thread = new Thread(Render) { IsBackground = true, Name = "StereoBridge WASAPI", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("WASAPI playback did not start.");
        if (_startupError is not null) throw new InvalidOperationException("WASAPI playback could not start.", _startupError);
    }

    private void Render()
    {
        Exception? fault = null;
        var client = _client!;
        var source = _source!;
        try
        {
            var frames = client.BufferSize;
            var bytesPerFrame = source.WaveFormat.BlockAlign;
            var buffer = new byte[frames * bytesPerFrame];
            var render = client.AudioRenderClient;
            void Fill(int count)
            {
                if (count <= 0) return;
                var bytes = count * bytesPerFrame;
                var read = source.Read(buffer, 0, bytes);
                if (read < bytes) Array.Clear(buffer, read, bytes - read);
                var destination = render.GetBuffer(count);
                try { Marshal.Copy(buffer, 0, destination, bytes); }
                finally { render.ReleaseBuffer(count, AudioClientBufferFlags.None); }
            }
            Fill(frames);
            client.Start();
            _started.Set();
            var waits = new WaitHandle[] { _stop, _ready };
            while (WaitHandle.WaitAny(waits, 1000) != 0)
            {
                Fill(frames - client.CurrentPadding);
            }
        }
        catch (Exception error) { fault = error; if (!_started.IsSet) _startupError = error; }
        finally
        {
            try { client.Stop(); client.Reset(); }
            catch (Exception error) { fault ??= error; }
            _started.Set();
        }
        if (!_stop.WaitOne(0)) PlaybackStopped?.Invoke(this, new StoppedEventArgs(fault));
    }

    private void ForwardStopped(object? sender, StoppedEventArgs e) => PlaybackStopped?.Invoke(this, e);
    public void Stop()
    {
        _stop.Set();
        _fallback?.Stop();
        if (_thread is not null && _thread != Thread.CurrentThread) _thread.Join();
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Stop();
        if (_fallback is not null) { _fallback.PlaybackStopped -= ForwardStopped; _fallback.Dispose(); }
        _client?.Dispose();
        _ready.Dispose();
        _stop.Dispose();
        _started.Dispose();
    }
}
