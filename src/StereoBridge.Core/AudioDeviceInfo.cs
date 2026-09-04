namespace StereoBridge.Core;

/// <summary>A disposable-free snapshot of a Windows render endpoint.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);
