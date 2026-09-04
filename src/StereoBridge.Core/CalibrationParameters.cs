namespace StereoBridge.Core;

public sealed record CalibrationParameters(
    double MicrophoneToLeftMeters,
    double MicrophoneToRightMeters,
    double ListenerToLeftMeters,
    double ListenerToRightMeters)
{
    public void Validate()
    {
        foreach (var distance in new[]
                 {
                     MicrophoneToLeftMeters,
                     MicrophoneToRightMeters,
                     ListenerToLeftMeters,
                     ListenerToRightMeters,
                 })
        {
            if (!double.IsFinite(distance) || distance is < 0 or > 50)
            {
                throw new ArgumentOutOfRangeException(nameof(distance), "Distances must be between 0 and 50 metres.");
            }
        }
    }
}
