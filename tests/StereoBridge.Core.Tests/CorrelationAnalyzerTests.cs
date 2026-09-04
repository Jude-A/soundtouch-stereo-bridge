namespace StereoBridge.Core.Tests;

public sealed class CorrelationAnalyzerTests
{
    [Fact]
    public void FindPeak_RecoversKnownDelay()
    {
        var reference = Enumerable.Range(0, 400)
            .Select(index => (float)(Math.Sin(index * 0.31) * Math.Sin(Math.PI * index / 399)))
            .ToArray();
        var recording = new float[2_000];
        const int knownDelay = 1_234;
        Array.Copy(reference, 0, recording, knownDelay, reference.Length);

        var result = CorrelationAnalyzer.FindPeak(recording, reference, 900, 1_500);

        Assert.Equal(knownDelay, result.SampleIndex);
        Assert.True(result.Confidence > 0.99);
    }
}
