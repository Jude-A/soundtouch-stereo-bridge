using NAudio.CoreAudioApi;

namespace StereoBridge.Core;

/// <summary>Enumerates Windows render endpoints without leaking COM objects into the UI.</summary>
public static class AudioDeviceManager
{
    public static IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var devices = new List<AudioDeviceInfo>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                devices.Add(new AudioDeviceInfo(
                    device.ID,
                    device.FriendlyName,
                    string.Equals(device.ID, defaultDevice.ID, StringComparison.Ordinal)));
            }
        }

        return devices
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static double GetEndpointVolumePercent(string endpointId)
        => GetEndpointLevel(endpointId).VolumePercent;

    public static AudioEndpointLevel GetEndpointLevel(string endpointId)
    {
        using var device = FindActiveRenderDevice(endpointId)
            ?? throw new InvalidOperationException("The selected output is no longer available.");
        var volume = device.AudioEndpointVolume;
        return new AudioEndpointLevel(
            volume.MasterVolumeLevelScalar * 100d,
            volume.MasterVolumeLevel,
            volume.Mute);
    }

    public static void SetEndpointVolumePercent(string endpointId, double volumePercent)
    {
        using var device = FindActiveRenderDevice(endpointId)
            ?? throw new InvalidOperationException("The selected output is no longer available.");
        var volume = device.AudioEndpointVolume;
        var normalized = Math.Clamp(volumePercent, 0, 100) / 100d;
        volume.MasterVolumeLevelScalar = (float)normalized;
        if (normalized > 0 && volume.Mute)
        {
            volume.Mute = false;
        }
    }

    internal static MMDevice GetDefaultRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    internal static MMDevice GetDefaultCaptureDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }

    internal static MMDevice? FindActiveRenderDevice(string endpointId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(endpointId);
            if (device.State == DeviceState.Active)
            {
                return device;
            }

            device.Dispose();
            return null;
        }
        catch
        {
            return null;
        }
    }
}

public readonly record struct AudioEndpointLevel(
    double VolumePercent,
    double VolumeDecibels,
    bool IsMuted);
