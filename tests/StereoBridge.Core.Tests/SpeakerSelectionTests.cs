using StereoBridge.Core;
namespace StereoBridge.Core.Tests;

public class SpeakerSelectionTests
{
    private static readonly AudioDeviceInfo[] Devices = {
        new("source", "CABLE Input (VB-Audio Virtual Cable)", true),
        new("monitor", "K273 (AMD High Definition Audio)", false),
        new("cable2", "CABLE In 16ch (VB-Audio Virtual Cable)", false),
        new("bose-a", "Haut-parleurs (Bose SoundTouch A)", false),
        new("bose-b", "Haut-parleurs (Bose SoundTouch B)", false) };

    [Fact]
    public void FreshSetupChoosesBoseInsteadOfMonitorOrCable()
    {
        var pair = SpeakerSelection.Select(Devices, null, null, null, null);
        Assert.Equal("bose-a", pair.Left?.Id);
        Assert.Equal("bose-b", pair.Right?.Id);
        Assert.DoesNotContain(SpeakerSelection.Outputs(Devices), SpeakerSelection.IsCable);
    }
    [Fact]
    public void RepairsRememberedMonitorAndCablePair()
    {
        var pair = SpeakerSelection.Select(Devices, "monitor", "cable2", Devices[1].Name, Devices[2].Name);
        Assert.Equal("bose-a", pair.Left?.Id);
        Assert.Equal("bose-b", pair.Right?.Id);
        Assert.False(AutomaticStartPolicy.CanStart(Devices, "monitor", "cable2"));
    }
    [Fact]
    public void PreservesSwappedAssignmentsAndRecoversChangedIdsByUniqueName()
    {
        var pair = SpeakerSelection.Select(Devices, "old-b", "old-a", Devices[4].Name, Devices[3].Name);
        Assert.Equal("bose-b", pair.Left?.Id);
        Assert.Equal("bose-a", pair.Right?.Id);
    }
    [Fact]
    public void MissingLeftBoseDoesNotStealRightOrChooseScreen()
    {
        var pair = SpeakerSelection.Select(Devices.Where(d => d.Id != "bose-b").ToArray(),
            "bose-b", "bose-a", Devices[4].Name, Devices[3].Name);
        Assert.Null(pair.Left);
        Assert.Equal("bose-a", pair.Right?.Id);
    }
    [Fact]
    public void KeepsValidManualNonBosePair()
    {
        var devices = Devices.Append(new AudioDeviceInfo("usb", "USB Audio", false)).ToArray();
        var pair = SpeakerSelection.Select(devices, "monitor", "usb", null, null);
        Assert.Equal("monitor", pair.Left?.Id);
        Assert.Equal("usb", pair.Right?.Id);
    }
    [Fact]
    public void DoesNotGuessIfNoBoseOrIfRememberedNameIsAmbiguous()
    {
        var pair = SpeakerSelection.Select(Devices.Take(3).ToArray(), null, null, null, null);
        Assert.Null(pair.Left);
        Assert.Null(pair.Right);
        var devices = Devices.Append(new AudioDeviceInfo("duplicate", Devices[3].Name, false)).ToArray();
        pair = SpeakerSelection.Select(devices, "old-a", "bose-b", Devices[3].Name, Devices[4].Name);
        Assert.Null(pair.Left);
        Assert.Equal("bose-b", pair.Right?.Id);
    }
}
