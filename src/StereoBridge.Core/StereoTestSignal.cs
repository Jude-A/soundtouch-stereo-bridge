using NAudio.Wave;

namespace StereoBridge.Core;

/// <summary>Creates an internal float signal: low tone on left, silence, high tone on right.</summary>
internal static class StereoTestSignal
{
    private const double LeadSeconds = 0.10;
    private const double ToneSeconds = 0.50;
    private const double GapSeconds = 0.15;
    private const double TailSeconds = 0.10;
    private const double Gain = 0.18;

    public const double ChannelTestDurationSeconds = LeadSeconds + ToneSeconds + GapSeconds + ToneSeconds + TailSeconds;
    public const double AlignmentTestDurationSeconds = 1.35;
    public const double CalibrationLeftStartSeconds = 0.18;
    public const double CalibrationRightStartSeconds = 0.88;
    public const double CalibrationChirpDurationSeconds = 0.16;
    public const double CalibrationDurationSeconds = 1.35;

    public static byte[] CreateChannelTest(WaveFormat format)
    {
        ValidateFormat(format);

        var frameCount = (int)Math.Round(ChannelTestDurationSeconds * format.SampleRate);
        var samples = new float[frameCount * 2];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var time = frame / (double)format.SampleRate;
            if (time >= LeadSeconds && time < LeadSeconds + ToneSeconds)
            {
                samples[frame * 2] = Tone(time - LeadSeconds, 660);
            }
            else if (time >= LeadSeconds + ToneSeconds + GapSeconds &&
                     time < LeadSeconds + (2 * ToneSeconds) + GapSeconds)
            {
                samples[(frame * 2) + 1] = Tone(time - LeadSeconds - ToneSeconds - GapSeconds, 990);
            }
        }

        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static byte[] CreateAlignmentClicks(WaveFormat format)
    {
        ValidateFormat(format);
        var frameCount = (int)Math.Round(AlignmentTestDurationSeconds * format.SampleRate);
        var samples = new float[frameCount * 2];
        var clickTimes = new[] { 0.15, 0.45, 0.75, 1.05 };
        var clickFrames = Math.Max(1, (int)Math.Round(0.004 * format.SampleRate));

        foreach (var clickTime in clickTimes)
        {
            var firstFrame = (int)Math.Round(clickTime * format.SampleRate);
            for (var offset = 0; offset < clickFrames && firstFrame + offset < frameCount; offset++)
            {
                var envelope = 1 - (offset / (double)clickFrames);
                var sample = (float)(0.25 * envelope * Math.Sin(2 * Math.PI * 1_800 * offset / format.SampleRate));
                var sampleIndex = (firstFrame + offset) * 2;
                samples[sampleIndex] = sample;
                samples[sampleIndex + 1] = sample;
            }
        }

        return ToBytes(samples);
    }

    public static byte[] CreateCalibrationSequence(WaveFormat format)
    {
        ValidateFormat(format);
        var frameCount = (int)Math.Round(CalibrationDurationSeconds * format.SampleRate);
        var samples = new float[frameCount * 2];
        WriteChirp(samples, format.SampleRate, CalibrationLeftStartSeconds, StereoSide.Left);
        WriteChirp(samples, format.SampleRate, CalibrationRightStartSeconds, StereoSide.Right);
        return ToBytes(samples);
    }

    public static float[] CreateCalibrationReference(int sampleRate, StereoSide side)
    {
        var frameCount = (int)Math.Round(CalibrationChirpDurationSeconds * sampleRate);
        var samples = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            samples[frame] = Chirp(frame / (double)sampleRate, side == StereoSide.Left);
        }
        return samples;
    }

    private static float Tone(double time, double frequency)
    {
        const double fadeSeconds = 0.015;
        var fade = Math.Min(1, Math.Min(time / fadeSeconds, (ToneSeconds - time) / fadeSeconds));
        return (float)(Gain * Math.Max(0, fade) * Math.Sin(2 * Math.PI * frequency * time));
    }

    private static void WriteChirp(float[] stereoSamples, int sampleRate, double startSeconds, StereoSide side)
    {
        var firstFrame = (int)Math.Round(startSeconds * sampleRate);
        var chirpFrames = (int)Math.Round(CalibrationChirpDurationSeconds * sampleRate);
        var channelIndex = side == StereoSide.Left ? 0 : 1;
        for (var frame = 0; frame < chirpFrames; frame++)
        {
            stereoSamples[((firstFrame + frame) * 2) + channelIndex] =
                Chirp(frame / (double)sampleRate, side == StereoSide.Left);
        }
    }

    private static float Chirp(double time, bool ascending)
    {
        const double startFrequency = 550;
        const double endFrequency = 4_800;
        const double amplitude = 0.14;
        var low = ascending ? startFrequency : endFrequency;
        var high = ascending ? endFrequency : startFrequency;
        var sweepRate = (high - low) / CalibrationChirpDurationSeconds;
        var phase = 2 * Math.PI * ((low * time) + (0.5 * sweepRate * time * time));
        var envelope = Math.Sin(Math.PI * time / CalibrationChirpDurationSeconds);
        return (float)(amplitude * envelope * envelope * Math.Sin(phase));
    }

    private static void ValidateFormat(WaveFormat format)
    {
        if (format.Channels != 2 || format.BitsPerSample != 32)
        {
            throw new NotSupportedException("The internal test signals require a 32-bit float stereo capture format.");
        }
    }

    private static byte[] ToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
