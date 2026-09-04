using StereoBridge.Core;

namespace StereoBridge.Core.Tests;

public class RoomProfileTests
{
    [Fact]
    public void LegacySettingsMigrateWithoutLosingCalibrationOrAudioSettings()
    {
        var config = StereoPairConfig.FromJson("""
            {"leftDeviceId":"left","rightDeviceId":"right","leftDelaySamples":125,
             "rightDelaySamples":27,"microphoneToLeftMeters":3.1,"microphoneToRightMeters":4.2,
             "listenerToLeftMeters":5.3,"listenerToRightMeters":6.4,
             "lastCalibrationUtc":"2026-09-04T12:00:00Z","lastCalibrationMicrophone":"PC",
             "lastCalibrationConfidence":0.91,"lastCalibrationDifferenceMilliseconds":2.5,
             "masterBoostDecibels":3,"balanceDecibels":-2,"playbackMode":1,"latencyProfile":0}
            """);
        var profile = Assert.Single(config.RoomProfiles);
        Assert.Equal(profile.Id, config.ActiveRoomProfileId);
        Assert.Equal(125, profile.LeftDelaySamples);
        Assert.Equal(27, profile.RightDelaySamples);
        Assert.Equal(3.1, profile.MicrophoneToLeftMeters);
        Assert.Equal(4.2, profile.MicrophoneToRightMeters);
        Assert.Equal(5.3, profile.ListenerToLeftMeters);
        Assert.Equal(6.4, profile.ListenerToRightMeters);
        Assert.NotNull(profile.LastCalibrationUtc);
        Assert.Equal("PC", profile.LastCalibrationMicrophone);
        Assert.Equal(0.91, profile.LastCalibrationConfidence);
        Assert.Equal(2.5, profile.LastCalibrationDifferenceMilliseconds);
        var reloaded = StereoPairConfig.FromJson(config.ToJson());
        Assert.Single(reloaded.RoomProfiles);
        Assert.Equal("left", reloaded.LeftDeviceId);
        Assert.Equal("right", reloaded.RightDeviceId);
        Assert.Equal(3, reloaded.MasterBoostDecibels);
        Assert.Equal(-2, reloaded.BalanceDecibels);
        Assert.Equal(config.PlaybackMode, reloaded.PlaybackMode);
        Assert.Equal(config.LatencyProfile, reloaded.LatencyProfile);
        Assert.False(reloaded.AutoStartBridge);
        Assert.False(reloaded.StartInTray);
    }

    [Fact]
    public void SwitchingAndRoundTripKeepIndependentGeometryDelaysAndHistory()
    {
        var config = StereoPairConfig.FromJson("{}");
        var original = config.ActiveRoomProfileId!;
        config.LeftDelaySamples = 480;
        config.MicrophoneToLeftMeters = 7;
        config.LastCalibrationMicrophone = "First mic";
        var copy = config.AddRoomProfile("Sofa", true);
        Assert.Equal(480, copy.LeftDelaySamples);
        config.LeftDelaySamples = 960;
        config.MicrophoneToLeftMeters = 9;
        config.LastCalibrationMicrophone = "Second mic";
        config.SelectRoomProfile(original);
        Assert.Equal(480, config.LeftDelaySamples);
        Assert.Equal(7, config.MicrophoneToLeftMeters);
        Assert.Equal("First mic", config.LastCalibrationMicrophone);
        config = StereoPairConfig.FromJson(config.ToJson());
        config.SelectRoomProfile(copy.Id);
        Assert.Equal(960, config.LeftDelaySamples);
        Assert.Equal(9, config.MicrophoneToLeftMeters);
        Assert.Equal("Second mic", config.LastCalibrationMicrophone);
        config.RoomProfiles.Single(p => p.Id == copy.Id).Name = "Desk";
        Assert.Contains("Desk", config.ToJson());
        Assert.True(config.DeleteActiveRoomProfile());
        Assert.Equal(480, config.LeftDelaySamples);
        Assert.False(config.DeleteActiveRoomProfile());
        var fresh = config.AddRoomProfile("New", false);
        Assert.Equal(0, fresh.LeftDelaySamples);
        Assert.Null(fresh.LastCalibrationMicrophone);
    }

    [Fact]
    public void MissingActiveIdRecoversFirstProfileAndPreservesOthers()
    {
        var config = StereoPairConfig.FromJson("{}");
        config.AddRoomProfile("Other", false);
        config.ActiveRoomProfileId = "missing";
        config.EnsureProfiles();
        Assert.Equal(config.RoomProfiles[0].Id, config.ActiveRoomProfileId);
        Assert.Equal(2, config.RoomProfiles.Count);
    }

    [Fact]
    public void AutomaticStartRequiresCableAndExactIndependentPair()
    {
        var devices = new[] {
            new AudioDeviceInfo("source", "CABLE Input (VB-Audio Virtual Cable)", true),
            new AudioDeviceInfo("left", "Bose", false),
            new AudioDeviceInfo("right", "Bose", false) };
        Assert.True(AutomaticStartPolicy.CanStart(devices, "left", "right"));
        Assert.False(AutomaticStartPolicy.CanStart(devices, "left", "left"));
        Assert.False(AutomaticStartPolicy.CanStart(devices, "source", "right"));
        Assert.False(AutomaticStartPolicy.CanStart(devices, "absent", "right"));
        Assert.False(AutomaticStartPolicy.CanStart(devices, null, "right"));
        Assert.False(AutomaticStartPolicy.CanStart(devices.Skip(1).ToArray(), "left", "right"));
        devices[0] = devices[0] with { Name = "Laptop speakers" };
        Assert.False(AutomaticStartPolicy.CanStart(devices, "left", "right"));
    }
}
