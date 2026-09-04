using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>NAudio loopback capture with an explicit shared-mode buffer request.</summary>
internal sealed class ConfigurableLoopbackCapture : WasapiCapture
{
    public ConfigurableLoopbackCapture(MMDevice device, int bufferMilliseconds)
        : base(device, useEventSync: true, audioBufferMillisecondsLength: bufferMilliseconds)
    {
    }

    protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
        AudioClientStreamFlags.Loopback;
}
