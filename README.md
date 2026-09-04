# SoundTouch Stereo Bridge

A small Windows utility for turning two independent audio endpoints into the two sides of a stereo pair.

## Status

Experimental Windows application, tested on one pair of Bose SoundTouch endpoints. This repository currently distributes source code, not a signed installer. VB-CABLE must be installed separately from [its official site](https://vb-audio.com/Cable/).

The current build targets .NET 7, which is out of support. Moving to a supported runtime and packaging an installer remain prerequisites for a general-audience release. This snapshot does not promise lip-sync with browser video services or long-session Bluetooth stability.

## Current milestone: tray, room profiles and video buffering

Implemented now:

- WASAPI loopback capture of the default Windows playback endpoint;
- active render-endpoint enumeration;
- two user-selected, independent WASAPI output pipelines;
- left-channel mapping to `[L,L]` and right-channel mapping to `[R,R]`;
- sample-frame-accurate per-side delay with a one-second limit;
- live 0.1 ms / 1 ms alignment controls and repeated alignment clicks;
- three-pass microphone auto-calibration with room-geometry correction;
- persistent endpoint assignments and delay values in `%APPDATA%\SoundTouchStereoBridge\settings.json`;
- one pair volume synchronized with the Windows volume of the virtual cable;
- common boost and left/right balance in 0.25 dB steps, with a smooth peak limiter;
- live stereo or dual-mono playback mode, persisted between launches;
- per-output buffering and sample-rate conversion;
- Stable, Balanced, and Low-latency buffer profiles;
- live buffer, underrun, backlog-reset, resampling, and limiter diagnostics;
- safe stop when either output disappears;
- Windows notification-area icon with open, start, stop and real quit commands;
- opt-in bridge auto-start/reconnect and launch-in-tray preferences;
- opt-in Windows sign-in startup for the current user;
- named room/position profiles with independent geometry, delays and calibration history;
- a compact WPF control UI;
- a deterministic `Test L → R` signal for checking physical channel routing.

The two-speaker hardware gate and channel split have been validated on the target PC. Adaptive drift compensation is intentionally deferred to the next milestone.

## Run

Requirements: Windows 10/11 and the .NET 7 SDK.

```powershell
dotnet run --project .\src\StereoBridge.App\StereoBridge.App.csproj
```

The Windows default playback endpoint is used as the loopback capture source, so it cannot also be one of the two destinations. Connect both Bose speakers as independent stereo/A2DP endpoints, choose **CABLE Input (VB-Audio Virtual Cable)** as the Windows default source, then use **Actualiser** and **Démarrer la stéréo**.

The Balanced profile requests a 40 ms capture buffer and 60 ms output buffers. If the Bluetooth drivers click or stutter, stop playback and switch back to Stable. The Video / minimum-buffer profile (the existing LowLatency setting) requests 10 ms capture and the smallest shared WASAPI output buffer, with a 10 ms NAudio fallback if initialization fails. The actual output buffer is displayed; it is device-dependent, and excludes Bluetooth/acoustic delay.

## Video latency optimization

The Video profile uses an event-driven WASAPI renderer with minimum shared buffer duration. Stable and Balanced retain NAudio output rendering. Both output chains are initialized before playback begins. Live queues retain at most 40 ms in Video mode (120 ms Balanced, 250 ms Stable), discarding only excess oldest complete frames instead of emptying the entire queue. Trimming can still cause an audible discontinuity; it is a recovery mechanism, not adaptive clock-drift correction.

Calibration and channel/alignment test signals explicitly bypass the live queue limit, preserving the complete sequence and existing delay processing. The sample-rate conversion, channel mapping, digital Windows volume, limiter and room-profile calibration remain in place. Diagnostics show queue trimming counts, discarded duration and actual WASAPI buffer sizes where available. A driver-reported latency of zero is not zero acoustic latency.

Hardware smoke comparison on the two target Bose endpoints, 48 kHz capture to 44.1 kHz output, using silence and muted output gains: the former fast profile reached a 100 ms application queue peak and eight complete resets per side; the new renderer negotiated approximately 22 ms WASAPI buffers. After the startup barrier fix, a 20-second run plus channel/alignment signal playback showed a 30 ms live queue peak and no trimming; initial underrun counters were 2 left / 1 right and stayed unchanged. Restart checks passed for all three profiles. These are internal buffering observations, not a measurement of movie lip-sync or a listening-quality guarantee. End-to-end Bluetooth delay and long-session clock drift still require evaluation.

## Background operation and startup

Minimizing or closing the window hides it in the Windows notification area. Audio and Windows-volume synchronization continue. Double-click the icon or select **Ouvrir** to restore the window. **Quitter** in its menu saves settings and releases the audio devices; closing the window alone does not exit.

Expand **Démarrage et zone de notification** for three independent choices (all off by default):

- **Démarrer / reconnecter automatiquement le bridge**: starts when the Windows default endpoint is identified as VB-CABLE/VB-Audio and both exact saved output IDs are available. Checks every five seconds; failed starts are retried at that interval. No fallback speaker is substituted. Configure the pair first. A manual **Arrêter** suppresses retries until a manual start, re-enabling this option, or the next app launch.
- **Lancer masqué dans la zone de notification**: hides the window at launch, independently of bridge auto-start.
- **Lancer avec ma session Windows**: explicitly adds/removes this executable in the current user's Windows Run registry key. No administrator rights required; nothing is registered until checked. Keep the executable at a stable path, and toggle the option off/on after moving it. This starts the application; enable bridge auto-start separately to start audio.

If a remembered speaker is absent, its saved assignment is retained. Reconnection requires the same Windows endpoint ID; after re-pairing Bluetooth hardware with a new ID, select it again. Auto-start recognizes the default source by its VB-Audio/VB-CABLE name; a custom renamed cable may need manual startup. Startup never changes the Windows default audio device. Tray start/stop/quit and room switching are disabled during auto-calibration so its result stays with the intended profile.

For the Release executable:

```powershell
dotnet build .\SoundTouchStereoBridge.sln -c Release
& '.\src\StereoBridge.App\bin\Release\net7.0-windows\StereoBridge.App.exe'
```

Exit an older running instance before opening the new build; run only one instance at a time.

## Room / listening-position profiles

**PIÈCE / POSITION ACTIVE** shows the selected profile above its four distances. Enter a name, then **Créer** for default geometry with no calibration, or **Dupliquer** to copy the active geometry, delays and calibration history. **Renommer** uses the same name field. **Supprimer** asks for confirmation and always keeps at least one profile.

Switching profiles saves the current valid geometry and delays, then immediately loads and applies the selected profile's left/right delays. Auto-calibration and manual delay changes update only the active profile. Each profile preserves the four distances, both delays in sample frames, calibration date, microphone, confidence and measured listening difference. Invalid distance edits are not saved. Volume, common boost, balance, stereo/mono, devices and pipeline latency remain shared across profiles.

Existing settings migrate to **Par défaut**, retaining geometry, delays, calibration metadata and global audio settings. The JSON keeps the legacy geometry/delay fields as the active profile's mirror. Avoid reopening an older version after migration, since it does not understand the profile list. Delays retain the existing sample-frame representation; recalibrate if the capture sample rate changes.

## Verification

GitHub Actions builds the WPF application and runs the deterministic tests on Windows for every push and pull request. Hardware audio checks are separate.

```powershell
dotnet test .\tests\StereoBridge.Core.Tests\StereoBridge.Core.Tests.csproj -c Release
```

Focused coverage protects legacy profile migration, JSON round-trips, independent profile switching/duplication/deletion, and exact-device automatic-start conditions, alongside the existing audio-math tests. Live Bluetooth reconnection, sign-in startup and listening quality require a hardware check on the target Windows session.

Runtime diagnostics are appended to `%LOCALAPPDATA%\SoundTouchStereoBridge\bridge.log`.

## Reference

The fan-out, endpoint ownership, event-sync fallback and fault-isolation design follows the MIT-licensed [AudioHQ](https://github.com/UnderFusion/AudioHQ) architecture. See `THIRD-PARTY-NOTICES.md`.
