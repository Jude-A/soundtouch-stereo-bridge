namespace StereoBridge.Core;

public static class CalibrationMath
{
    public const double SpeedOfSoundMetersPerSecond = 343;

    /// <summary>
    /// Converts right-minus-left arrival time measured at the microphone into the expected
    /// right-minus-left arrival time at the listener position.
    /// </summary>
    public static double CorrectDifferenceForListener(
        double measuredRightMinusLeftMilliseconds,
        CalibrationParameters parameters)
    {
        parameters.Validate();
        var microphoneDistanceDifference =
            parameters.MicrophoneToRightMeters - parameters.MicrophoneToLeftMeters;
        var listenerDistanceDifference =
            parameters.ListenerToRightMeters - parameters.ListenerToLeftMeters;
        var correctionMilliseconds =
            (listenerDistanceDifference - microphoneDistanceDifference) /
            SpeedOfSoundMetersPerSecond * 1_000;
        return measuredRightMinusLeftMilliseconds + correctionMilliseconds;
    }

    public static double GeometryCorrectionMilliseconds(CalibrationParameters parameters) =>
        CorrectDifferenceForListener(0, parameters);

    public static (int LeftDelaySamples, int RightDelaySamples) CalculateAbsoluteDelays(
        double residualRightMinusLeftMilliseconds,
        int currentLeftDelaySamples,
        int currentRightDelaySamples,
        int sampleRate)
    {
        var currentDifferenceMilliseconds =
            (currentRightDelaySamples - currentLeftDelaySamples) * 1_000d / sampleRate;
        var targetDifferenceMilliseconds = currentDifferenceMilliseconds - residualRightMinusLeftMilliseconds;
        var targetDifferenceSamples = (int)Math.Round(targetDifferenceMilliseconds * sampleRate / 1_000d);

        return targetDifferenceSamples >= 0
            ? (0, targetDifferenceSamples)
            : (-targetDifferenceSamples, 0);
    }
}
