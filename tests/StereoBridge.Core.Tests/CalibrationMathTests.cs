namespace StereoBridge.Core.Tests;

public sealed class CalibrationMathTests
{
    [Fact]
    public void CorrectDifferenceForListener_UsesBothMeasurementPositions()
    {
        var parameters = new CalibrationParameters(2.8, 1.3, 2.0, 1.2);

        var corrected = CalibrationMath.CorrectDifferenceForListener(0, parameters);

        Assert.Equal(2.041, corrected, precision: 3);
    }

    [Theory]
    [InlineData(20, 960, 0)]
    [InlineData(-20, 0, 960)]
    public void CalculateAbsoluteDelays_DelaysOnlyTheEarlierSide(
        double rightMinusLeftMilliseconds,
        int expectedLeft,
        int expectedRight)
    {
        var actual = CalibrationMath.CalculateAbsoluteDelays(
            rightMinusLeftMilliseconds,
            currentLeftDelaySamples: 0,
            currentRightDelaySamples: 0,
            sampleRate: 48_000);

        Assert.Equal(expectedLeft, actual.LeftDelaySamples);
        Assert.Equal(expectedRight, actual.RightDelaySamples);
    }
}
