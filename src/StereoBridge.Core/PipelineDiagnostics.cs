namespace StereoBridge.Core;

public sealed record OutputPipelineDiagnostics(
    StereoSide Side,
    string DeviceName,
    double CurrentBufferMilliseconds,
    double PeakBufferMilliseconds,
    long UnderrunCount,
    long BacklogResetCount,
    int SourceSampleRate,
    int OutputSampleRate,
    bool LimiterActive,
    double? WasapiBufferMilliseconds,
    double? ReportedLatencyMilliseconds,
    string Backend,
    double DroppedMilliseconds);

public sealed record PipelineDiagnostics(
    LatencyProfile LatencyProfile,
    int CaptureBufferMilliseconds,
    IReadOnlyList<OutputPipelineDiagnostics> Outputs);
