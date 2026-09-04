namespace StereoBridge.Core.Tests;

public sealed class DigitalLevelMathTests
{
    [Fact]
    public void CalculateGains_PositiveBalanceKeepsLeftAtMasterAndAttenuatesRight()
    {
        var gains = DigitalLevelMath.CalculateGains(6, 4);

        Assert.Equal(6, DigitalLevelMath.GainToDecibels(gains.LeftGain), precision: 10);
        Assert.Equal(2, DigitalLevelMath.GainToDecibels(gains.RightGain), precision: 10);
    }

    [Fact]
    public void CalculateGains_NegativeBalanceKeepsRightAtMasterAndAttenuatesLeft()
    {
        var gains = DigitalLevelMath.CalculateGains(6, -4);

        Assert.Equal(2, DigitalLevelMath.GainToDecibels(gains.LeftGain), precision: 10);
        Assert.Equal(6, DigitalLevelMath.GainToDecibels(gains.RightGain), precision: 10);
    }

    [Fact]
    public void FromGains_RoundTripsLegacyPerSideGains()
    {
        const double legacyLeftGain = 1.4522996766079772;
        const double legacyRightGain = 1;
        var settings = DigitalLevelMath.FromGains(legacyLeftGain, legacyRightGain);

        var gains = DigitalLevelMath.CalculateGains(
            settings.MasterBoostDecibels,
            settings.BalanceDecibels);

        Assert.Equal(legacyLeftGain, gains.LeftGain, precision: 10);
        Assert.Equal(legacyRightGain, gains.RightGain, precision: 10);
    }
}
