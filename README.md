# SoundTouch Stereo Bridge

**Use two independent Bose speakers as a left/right stereo pair on Windows.**

Send Windows audio through VB-CABLE, select your speakers, and adjust their timing for your listening position. The app keeps playing from the system tray.

> **Experimental · Windows 10/11 · Source code only**<br>
> No installer yet. Bluetooth delay remains: synchronized speakers do not guarantee synchronized movie dialogue.

## What it does

- **Stereo or mono:** separate left/right channels, or the same mix on both speakers.
- **One volume:** follows Windows volume, with a common boost and left/right balance.
- **Room profiles:** save distances and calibrated delays for different listening positions.
- **Microphone calibration:** align the speakers automatically, with manual fine adjustments.
- **Background playback:** tray controls and optional automatic startup/reconnection.

## Get started

You need **two independently connected speakers**, [VB-CABLE](https://vb-audio.com/Cable/) and the **.NET 7 SDK**. The app interface is currently in French.

1. Install VB-CABLE and connect both Bose speakers to Windows as stereo audio outputs.
2. Set **CABLE Input** (the VB-Audio virtual cable) as the default Windows playback device.
3. Clone this repository and launch the app:

   ```powershell
   git clone https://github.com/Jude-A/soundtouch-stereo-bridge.git
   cd soundtouch-stereo-bridge
   dotnet run --project .\src\StereoBridge.App\StereoBridge.App.csproj
   ```

4. Check the suggested Bose speakers and their left/right assignments, then click **Démarrer la stéréo**. VB-CABLE outputs are excluded from speaker selection.
5. Use **Test G → D** to check the channel assignments.

**Audio path:** Windows → VB-CABLE → Stereo Bridge → left and right speakers.

Keep VB-CABLE as the Windows default output. Neither Bose speaker can also be the capture source. Run only one instance of the app at a time.

## Choose a latency mode

| Mode in the app | When to use it |
| --- | --- |
| **Équilibré** | Start here for everyday listening. |
| **Stable** | Try this if playback clicks or stutters. |
| **Vidéo · tampon minimal** | Reduce application buffering for video. Bluetooth delay still applies. |

**Calibration aligns the two speakers with each other. It does not correct their shared delay relative to the picture.**

## Save a listening position

Under **PIÈCE / POSITION ACTIVE**:

1. Enter a name and choose **Créer**, or **Dupliquer** to copy the current calibration.
2. Enter the four distances: microphone → each speaker, then listening position → each speaker.
3. Start the bridge, keep the room quiet and click **Auto-calibrer**.

Selecting a profile loads its distances and applies its saved delays immediately. **Renommer** and **Supprimer** manage the profiles; at least one is always retained.

Volume, balance, devices and latency mode are shared across profiles. Existing settings migrate to **Par défaut**. Recalibrate if the capture sample rate changes.

## Keep it running in the background

**Closing or minimizing hides the window; it does not stop the audio.** Left-click the tray icon to show or hide the window. Use **Quitter** in the tray menu to exit completely.

The tray menu also lets you choose **Stable**, **Équilibré** or **Vidéo · tampon minimal**, with a checkmark on the selected mode. Changes are saved and restart active playback, just like the window control. This choice is disabled during auto-calibration.

Right-click the tray icon and choose **Créer un raccourci sur le bureau** to create a desktop shortcut with the app icon. Running it again updates the same shortcut. If you move the application folder, use this command again from its new location.

The **Démarrage et zone de notification** section has three independent options, all off by default:

| Option | Effect |
| --- | --- |
| Automatic bridge start/reconnection | Waits for VB-CABLE and the two saved speakers. A manual stop suspends retries for the session. |
| Launch in tray | Starts with the window hidden. |
| Launch with Windows | Starts the app when you sign in. Enable automatic bridge start separately for audio. |

Automatic selection preserves a valid saved pair. If the saved pair is incomplete or contains a virtual cable, it prioritizes Bose/SoundTouch speakers instead of guessing another output. A unique matching name can recover a speaker whose ID changed; an absent remembered Bose remains unselected. The app cannot infer physical left/right placement: confirm it with **Test G → D**.

Configure the speakers before enabling automatic start. If Bluetooth re-pairing changes a device's ID, select it again. A renamed VB-CABLE endpoint may require manual startup.

## Current limits

- **No signed installer yet.** Build and launch from source.
- **.NET 7 is out of support.** A supported runtime is needed before a general-audience release.
- **Hardware coverage is limited to one Bose pair.** Long-session stability, reconnection and video lip-sync need further testing.
- **No adaptive clock-drift correction.** Buffer recovery can still cause audible discontinuities.

<details>
<summary><strong>Developer notes: build, tests and diagnostics</strong></summary>

### Build and run the executable

```powershell
dotnet build .\SoundTouchStereoBridge.sln -c Release
& '.\src\StereoBridge.App\bin\Release\net7.0-windows\StereoBridge.App.exe'
```

For Windows sign-in startup, keep the executable at a stable path. Toggle that option off/on after moving it.

### Run tests

```powershell
dotnet test .\tests\StereoBridge.Core.Tests\StereoBridge.Core.Tests.csproj -c Release
```

GitHub Actions builds the WPF app and runs the tests on Windows. Tests cover audio math, complete calibration signals, queue limits, profile migration/persistence and automatic-start device selection. They do not replace listening tests.

### Buffering

| Mode | Capture request | Output request | Live queue limit |
| --- | --- | --- | --- |
| Stable | 100 ms | 100 ms, NAudio | 250 ms |
| Balanced | 40 ms | 60 ms, NAudio | 120 ms |
| Video | 10 ms | Minimum shared WASAPI buffer; 10 ms NAudio fallback | 40 ms |

These are requests and queue limits, **not end-to-end latency figures**. Outputs initialize before capture starts. Overflow discards only excess oldest frames; calibration/test sequences bypass the live queue limit.

On the tested Bose pair, Video negotiated approximately **22 ms WASAPI buffers**. A 20-second muted test plus channel/alignment signals reached a **30 ms live queue peak with no trimming**. Initial underrun counts stayed unchanged. Restart checks passed for all three modes. This does not establish audible quality or movie lip-sync.

### Local files

- Settings: `%APPDATA%\SoundTouchStereoBridge\settings.json`
- Diagnostics: `%LOCALAPPDATA%\SoundTouchStereoBridge\bridge.log`

Settings retain legacy fields as a mirror of the active profile. Avoid reopening an older app version after profile migration.

</details>

## Credits

Built with **NAudio**, with architecture and portions of the fan-out implementation adapted from [AudioHQ](https://github.com/UnderFusion/AudioHQ). VB-CABLE is installed separately. See [third-party notices](THIRD-PARTY-NOTICES.md).
