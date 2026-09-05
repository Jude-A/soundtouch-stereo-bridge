namespace StereoBridge.Core;

public static class SpeakerSelection
{
    public static bool IsCable(AudioDeviceInfo device) =>
        device.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
        device.Name.Contains("VB-CABLE", StringComparison.OrdinalIgnoreCase);

    public static bool IsBose(AudioDeviceInfo device) => IsBoseName(device.Name);
    private static bool IsBoseName(string? name) => name is not null &&
        (name.Contains("Bose", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("SoundTouch", StringComparison.OrdinalIgnoreCase));

    public static AudioDeviceInfo[] Outputs(IReadOnlyList<AudioDeviceInfo> devices) => devices
        .Where(d => !d.IsDefault && !IsCable(d))
        .OrderByDescending(IsBose).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(d => d.Id, StringComparer.Ordinal).ToArray();

    public static (AudioDeviceInfo? Left, AudioDeviceInfo? Right) Select(
        IReadOnlyList<AudioDeviceInfo> devices, string? leftId, string? rightId,
        string? leftName, string? rightName)
    {
        var outputs = Outputs(devices);
        AudioDeviceInfo? Remembered(string? id, string? name)
        {
            var exact = outputs.FirstOrDefault(d => d.Id == id);
            if (exact is not null) return exact;
            var matches = outputs.Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        var left = Remembered(leftId, leftName);
        var right = Remembered(rightId, rightName);
        // Preserve a complete, valid user-assigned pair, including non-Bose hardware.
        if (left is not null && right is not null && left.Id != right.Id) return (left, right);
        if (left?.Id == right?.Id) right = null;
        var bose = outputs.Where(IsBose).ToArray();
        if (bose.Length > 0)
        {
            if (left is not null && !IsBose(left)) left = null;
            if (right is not null && !IsBose(right)) right = null;
        }
        // Reserve both remembered sides before filling gaps: never steal the right speaker for the left.
        if (left is null && !IsBoseName(leftName))
            left = bose.FirstOrDefault(d => d.Id != right?.Id);
        if (right is null && !IsBoseName(rightName))
            right = bose.FirstOrDefault(d => d.Id != left?.Id);
        return (left, right);
    }
}
