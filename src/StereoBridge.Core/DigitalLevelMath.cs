namespace StereoBridge.Core;

public static class DigitalLevelMath
{
    public const double MaximumBoostDecibels = 9.5;
    public const double MaximumBalanceDecibels = 12;

    /// <summary>
    /// Positive balance favours left and negative balance favours right. Balance only attenuates
    /// the nearer side so the selected master boost remains the loudest side.
    /// </summary>
    public static (double LeftGain, double RightGain) CalculateGains(
        double masterBoostDecibels,
        double balanceDecibels)
    {
        var master = Math.Clamp(masterBoostDecibels, 0, MaximumBoostDecibels);
        var balance = Math.Clamp(balanceDecibels, -MaximumBalanceDecibels, MaximumBalanceDecibels);
        var leftDecibels = master + Math.Min(balance, 0);
        var rightDecibels = master - Math.Max(balance, 0);
        return (DecibelsToGain(leftDecibels), DecibelsToGain(rightDecibels));
    }

    public static double DecibelsToGain(double decibels) => Math.Pow(10, decibels / 20);

    public static double GainToDecibels(double gain) =>
        20 * Math.Log10(Math.Max(gain, 0.000_001));

    public static (double MasterBoostDecibels, double BalanceDecibels) FromGains(
        double leftGain,
        double rightGain)
    {
        var leftDecibels = GainToDecibels(leftGain);
        var rightDecibels = GainToDecibels(rightGain);
        return (
            Math.Clamp(Math.Max(leftDecibels, rightDecibels), 0, MaximumBoostDecibels),
            Math.Clamp(leftDecibels - rightDecibels, -MaximumBalanceDecibels, MaximumBalanceDecibels));
    }
}
