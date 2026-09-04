using System.Text.Json;

namespace StereoBridge.Core;

public sealed class StereoPairConfig : RoomCalibration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string? LeftDeviceId { get; set; }
    public string? RightDeviceId { get; set; }
    public string? LeftFriendlyName { get; set; }
    public string? RightFriendlyName { get; set; }
    public double LeftGain { get; set; } = 1;
    public double RightGain { get; set; } = 1;
    public double? MasterBoostDecibels { get; set; }
    public double? BalanceDecibels { get; set; }
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.Stereo;
    public LatencyProfile LatencyProfile { get; set; } = LatencyProfile.Balanced;
    public bool AutoStartBridge { get; set; }
    public bool StartInTray { get; set; }
    public string? ActiveRoomProfileId { get; set; }
    public List<RoomProfile> RoomProfiles { get; set; } = new();

    public void EnsureProfiles()
    {
        if (RoomProfiles.Count == 0)
        {
            var migrated = Snapshot("Par défaut");
            RoomProfiles.Add(migrated);
            ActiveRoomProfileId = migrated.Id;
        }
        var active = RoomProfiles.FirstOrDefault(p => p.Id == ActiveRoomProfileId) ?? RoomProfiles[0];
        ActiveRoomProfileId = active.Id;
        active.CopyTo(this);
    }

    public void StoreActiveProfile()
    {
        var active = RoomProfiles.FirstOrDefault(p => p.Id == ActiveRoomProfileId);
        if (active is null) { EnsureProfiles(); return; }
        CopyTo(active);
    }

    public void SelectRoomProfile(string id)
    {
        var next = RoomProfiles.Single(p => p.Id == id);
        StoreActiveProfile();
        ActiveRoomProfileId = next.Id;
        next.CopyTo(this);
    }

    public RoomProfile AddRoomProfile(string name, bool duplicate)
    {
        StoreActiveProfile();
        var profile = duplicate ? Snapshot(name) : new RoomProfile { Name = name };
        RoomProfiles.Add(profile);
        SelectRoomProfile(profile.Id);
        return profile;
    }

    public bool DeleteActiveRoomProfile()
    {
        if (RoomProfiles.Count <= 1) return false;
        RoomProfiles.RemoveAll(p => p.Id == ActiveRoomProfileId);
        ActiveRoomProfileId = RoomProfiles[0].Id;
        RoomProfiles[0].CopyTo(this);
        return true;
    }

    private RoomProfile Snapshot(string name)
    {
        var profile = new RoomProfile { Name = name };
        CopyTo(profile);
        return profile;
    }

    public static StereoPairConfig FromJson(string json)
    {
        var config = JsonSerializer.Deserialize<StereoPairConfig>(json, JsonOptions) ?? new();
        config.EnsureProfiles();
        return config;
    }

    public string ToJson()
    {
        StoreActiveProfile();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SoundTouchStereoBridge",
        "settings.json");

    public static StereoPairConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return FromJson("{}");
            }

            return FromJson(File.ReadAllText(FilePath));
        }
        catch (Exception error)
        {
            BridgeLog.Write($"Settings load failed: {error.Message}");
            return FromJson("{}");
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, ToJson());
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception error)
        {
            BridgeLog.Write($"Settings save failed: {error.Message}");
        }
    }
}
