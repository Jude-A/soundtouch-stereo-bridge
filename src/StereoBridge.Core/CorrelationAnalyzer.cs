namespace StereoBridge.Core;

public static class CorrelationAnalyzer
{
    private const int CoarseStep = 4;
    private const int RefinementRadius = 16;

    public static CorrelationPeak FindPeak(
        IReadOnlyList<float> recording,
        IReadOnlyList<float> reference,
        int searchStartSample,
        int searchEndSample)
    {
        if (reference.Count == 0)
        {
            throw new ArgumentException("The reference signal is empty.", nameof(reference));
        }

        var lastValidStart = recording.Count - reference.Count;
        if (lastValidStart < 0)
        {
            throw new ArgumentException("The recording is shorter than the reference signal.", nameof(recording));
        }
        var first = Math.Clamp(searchStartSample, 0, lastValidStart);
        var last = Math.Clamp(searchEndSample, first, lastValidStart);

        var coarseBestSample = first;
        var coarseBestConfidence = double.NegativeInfinity;
        for (var candidate = first; candidate <= last; candidate += CoarseStep)
        {
            var confidence = CorrelationMagnitude(recording, reference, candidate, CoarseStep);
            if (confidence > coarseBestConfidence)
            {
                coarseBestConfidence = confidence;
                coarseBestSample = candidate;
            }
        }

        var refinedFirst = Math.Max(first, coarseBestSample - RefinementRadius);
        var refinedLast = Math.Min(last, coarseBestSample + RefinementRadius);
        var bestSample = coarseBestSample;
        var bestConfidence = double.NegativeInfinity;
        for (var candidate = refinedFirst; candidate <= refinedLast; candidate++)
        {
            var confidence = CorrelationMagnitude(recording, reference, candidate, 1);
            if (confidence > bestConfidence)
            {
                bestConfidence = confidence;
                bestSample = candidate;
            }
        }

        return new CorrelationPeak(bestSample, bestConfidence);
    }

    private static double CorrelationMagnitude(
        IReadOnlyList<float> recording,
        IReadOnlyList<float> reference,
        int recordingStart,
        int step)
    {
        double dot = 0;
        double recordingEnergy = 0;
        double referenceEnergy = 0;

        for (var referenceIndex = 0; referenceIndex < reference.Count; referenceIndex += step)
        {
            var recordedSample = recording[recordingStart + referenceIndex];
            var referenceSample = reference[referenceIndex];
            dot += recordedSample * referenceSample;
            recordingEnergy += recordedSample * recordedSample;
            referenceEnergy += referenceSample * referenceSample;
        }

        var denominator = Math.Sqrt(recordingEnergy * referenceEnergy);
        return denominator <= double.Epsilon ? 0 : Math.Abs(dot) / denominator;
    }
}

public readonly record struct CorrelationPeak(int SampleIndex, double Confidence);
