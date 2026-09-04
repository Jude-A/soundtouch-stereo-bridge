namespace StereoBridge.Core;

public class RoomCalibration
{
    public int LeftDelaySamples { get; set; }
    public int RightDelaySamples { get; set; }
    public double MicrophoneToLeftMeters { get; set; } = 2.8;
    public double MicrophoneToRightMeters { get; set; } = 1.3;
    public double ListenerToLeftMeters { get; set; } = 2.0;
    public double ListenerToRightMeters { get; set; } = 1.2;
    public DateTimeOffset? LastCalibrationUtc { get; set; }
    public string? LastCalibrationMicrophone { get; set; }
    public double? LastCalibrationConfidence { get; set; }
    public double? LastCalibrationDifferenceMilliseconds { get; set; }

    public void CopyTo(RoomCalibration destination)
    {
        destination.LeftDelaySamples = LeftDelaySamples;
        destination.RightDelaySamples = RightDelaySamples;
        destination.MicrophoneToLeftMeters = MicrophoneToLeftMeters;
        destination.MicrophoneToRightMeters = MicrophoneToRightMeters;
        destination.ListenerToLeftMeters = ListenerToLeftMeters;
        destination.ListenerToRightMeters = ListenerToRightMeters;
        destination.LastCalibrationUtc = LastCalibrationUtc;
        destination.LastCalibrationMicrophone = LastCalibrationMicrophone;
        destination.LastCalibrationConfidence = LastCalibrationConfidence;
        destination.LastCalibrationDifferenceMilliseconds = LastCalibrationDifferenceMilliseconds;
    }
}

public sealed class RoomProfile : RoomCalibration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Par défaut";
}
