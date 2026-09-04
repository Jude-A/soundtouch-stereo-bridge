namespace StereoBridge.Core;

public sealed record CalibrationResult(
    string MicrophoneName,
    double MeasuredDifferenceMilliseconds,
    double GeometryCorrectionMilliseconds,
    double ListeningDifferenceMilliseconds,
    double Confidence,
    double StandardDeviationMilliseconds,
    int LeftDelaySamples,
    int RightDelaySamples,
    bool Applied);
