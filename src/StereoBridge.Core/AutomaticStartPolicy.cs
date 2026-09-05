namespace StereoBridge.Core;

public static class AutomaticStartPolicy
{
    public static bool CanStart(IReadOnlyList<AudioDeviceInfo> devices, string? leftId, string? rightId)
    {
        var source = devices.FirstOrDefault(d => d.IsDefault);
        return source is not null &&
               SpeakerSelection.IsCable(source) &&
               leftId is not null && rightId is not null && leftId != rightId &&
               devices.Any(d => d.Id == leftId && !d.IsDefault && !SpeakerSelection.IsCable(d)) &&
               devices.Any(d => d.Id == rightId && !d.IsDefault && !SpeakerSelection.IsCable(d));
    }
}
