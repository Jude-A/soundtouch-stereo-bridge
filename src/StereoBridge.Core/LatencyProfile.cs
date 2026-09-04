namespace StereoBridge.Core;

public enum LatencyProfile
{
    Stable,
    Balanced,
    LowLatency,
}

public sealed record LatencyProfileSettings(
    int CaptureBufferMilliseconds,
    int OutputBufferMilliseconds,
    int MaximumBacklogMilliseconds)
{
    public static LatencyProfileSettings For(LatencyProfile profile) => profile switch
    {
        LatencyProfile.Stable => new(100, 100, 250),
        LatencyProfile.Balanced => new(40, 60, 120),
        LatencyProfile.LowLatency => new(10, 10, 40),
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };
}
