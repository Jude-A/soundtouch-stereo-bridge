using System.Windows;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StereoBridge.Core;

namespace StereoBridge.App;

public partial class MainWindow : Window
{
    private readonly StereoEngine _engine = new();
    private readonly StereoPairConfig _config = StereoPairConfig.Load();
    private IReadOnlyList<AudioDeviceInfo> _devices = Array.Empty<AudioDeviceInfo>();
    private string? _sourceEndpointId;
    private AudioEndpointLevel _windowsEndpointLevel = new(100, 0, false);
    private bool _hasWindowsEndpointLevel;
    private int _sampleRate = 48_000;
    private bool _updatingVolumeControls;
    private bool _updatingLevelControls;
    private bool _updatingLatencyControls;
    private bool _isCalibrating;
    private bool _refreshingDevices;
    private bool _started;
    private readonly DispatcherTimer _diagnosticsTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1),
    };
    private readonly DispatcherTimer _windowsVolumeTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(150),
    };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        _engine.PlaybackFaulted += OnPlaybackFaulted;
        _diagnosticsTimer.Tick += DiagnosticsTimer_Tick;
        _windowsVolumeTimer.Tick += WindowsVolumeTimer_Tick;
        UpdateDelayLabels();
        InitializeDigitalLevelControls();
        InitializePlaybackMode();
        InitializeCalibrationControls();
        InitializeLatencyControls();
        InitializeIntegration();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_started) return;
        _started = true;
        RefreshDevices();
        _windowsVolumeTimer.Start();
        _wantRunning = _config.AutoStartBridge;
        _deviceTimer.Start();
        CheckAutomaticStart();
        if (_config.StartInTray) Hide();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs eventArgs) => RefreshDevices();

    private void StartButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _wantRunning = true;
        StartStereo();
    }

    private bool StartStereo()
    {
        if (LeftDeviceBox.SelectedValue is not string leftId ||
            RightDeviceBox.SelectedValue is not string rightId)
        {
            SetStatus("Sélectionne les deux enceintes de sortie.", isError: true);
            return false;
        }

        try
        {
            RefreshWindowsVolume(forceApply: true);
            var effectiveGains = CalculateEffectiveGains();
            _engine.Start(
                leftId,
                rightId,
                _config.LeftDelaySamples,
                _config.RightDelaySamples,
                (float)effectiveGains.LeftGain,
                (float)effectiveGains.RightGain,
                _config.PlaybackMode,
                _config.LatencyProfile);
            _sampleRate = _engine.CaptureSampleRate;
            SaveConfig();
            UpdateDelayLabels();
            SetRunningState(isRunning: true);
            _diagnosticsTimer.Start();
            UpdateDiagnostics();
            SetStatus($"Stéréo active depuis « {_engine.SourceName} » avec le profil {FormatLatencyProfile(_config.LatencyProfile)}.");
            return true;
        }
        catch (Exception error)
        {
            _engine.Stop();
            _diagnosticsTimer.Stop();
            UpdateDiagnostics();
            SetRunningState(isRunning: false);
            SetStatus(error.Message, isError: true);
            return false;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        _wantRunning = false;
        _engine.Stop();
        _diagnosticsTimer.Stop();
        UpdateDiagnostics();
        SetRunningState(isRunning: false);
        SetStatus("Lecture arrêtée.");
    }

    private async void TestButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        TestButton.IsEnabled = false;
        SetStatus("Test des canaux : son grave à GAUCHE, puis son aigu à DROITE.");

        try
        {
            await _engine.PlayChannelTestAsync();
            SetStatus("Test terminé : le grave était à gauche et l’aigu à droite.");
        }
        catch (Exception error)
        {
            SetStatus($"Échec du test des canaux : {error.Message}", isError: true);
        }
        finally
        {
            TestButton.IsEnabled = _engine.IsRunning;
        }
    }

    private async void AlignmentTestButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        AlignmentTestButton.IsEnabled = false;
        SetStatus("Calibration à l’oreille : chaque clic net part simultanément vers les deux enceintes.");

        try
        {
            await _engine.PlayAlignmentTestAsync();
            SetStatus("Si tu entends deux attaques, retarde l’enceinte en avance. Un seul clic signifie que l’alignement est bon.");
        }
        catch (Exception error)
        {
            SetStatus($"Échec du test d’alignement : {error.Message}", isError: true);
        }
        finally
        {
            AlignmentTestButton.IsEnabled = _engine.IsRunning;
        }
    }

    private async void AutoCalibrateButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (!TryReadCalibrationParameters(out var parameters, showError: true))
        {
            return;
        }

        _config.MicrophoneToLeftMeters = parameters.MicrophoneToLeftMeters;
        _config.MicrophoneToRightMeters = parameters.MicrophoneToRightMeters;
        _config.ListenerToLeftMeters = parameters.ListenerToLeftMeters;
        _config.ListenerToRightMeters = parameters.ListenerToRightMeters;
        _config.Save();

        _isCalibrating = true;
        SetCalibrationBusy(isBusy: true);
        SetStatus("Auto-calibration : trois paires de chirps. Garde la pièce silencieuse pendant environ 15 secondes.");

        try
        {
            var result = await _engine.AutoCalibrateAsync(parameters);
            if (!result.Applied)
            {
                SetStatus(
                    $"Calibration non appliquée : confiance {result.Confidence:0.00}, variation " +
                    $"{result.StandardDeviationMilliseconds:0.0} ms. Réessaie dans le silence.",
                    isError: true);
                return;
            }

            _config.LeftDelaySamples = result.LeftDelaySamples;
            _config.RightDelaySamples = result.RightDelaySamples;
            _config.LastCalibrationUtc = DateTimeOffset.UtcNow;
            _config.LastCalibrationMicrophone = result.MicrophoneName;
            _config.LastCalibrationConfidence = result.Confidence;
            _config.LastCalibrationDifferenceMilliseconds = result.ListeningDifferenceMilliseconds;
            _config.Save();
            UpdateCalibrationHistory();
            UpdateDelayLabels();

            var appliedDelay = result.LeftDelaySamples > 0
                ? $"gauche +{result.LeftDelaySamples * 1_000d / _sampleRate:0.00} ms"
                : result.RightDelaySamples > 0
                    ? $"droite +{result.RightDelaySamples * 1_000d / _sampleRate:0.00} ms"
                    : "aucun retard supplémentaire";
            SetStatus(
                $"Calibré avec « {result.MicrophoneName} » : mesure D−G {result.MeasuredDifferenceMilliseconds:+0.00;-0.00;0.00} ms, " +
                $"correction de géométrie {result.GeometryCorrectionMilliseconds:+0.00;-0.00;0.00} ms, retard appliqué {appliedDelay}. " +
                $"Confiance {result.Confidence:0.00}.");
        }
        catch (Exception error)
        {
            SetStatus($"Échec de l’auto-calibration : {error.Message}", isError: true);
        }
        finally
        {
            _isCalibrating = false;
            SetCalibrationBusy(isBusy: false);
        }
    }

    private void DelayButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split(':');
        if (parts.Length != 2 ||
            !Enum.TryParse(parts[0], out StereoSide side) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var deltaMilliseconds))
        {
            return;
        }

        AdjustDelay(side, deltaMilliseconds);
    }

    private void WindowsVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_updatingVolumeControls || !IsLoaded || _sourceEndpointId is null)
        {
            return;
        }

        try
        {
            AudioDeviceManager.SetEndpointVolumePercent(_sourceEndpointId, eventArgs.NewValue);
            RefreshWindowsVolume(forceApply: true);
        }
        catch (Exception error)
        {
            SetStatus($"Impossible de modifier le volume Windows : {error.Message}", isError: true);
        }
    }

    private void LevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_updatingLevelControls || !IsLoaded)
        {
            return;
        }

        ApplyDigitalLevels(save: true);
    }

    private void ResetBalanceButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        BalanceSlider.Value = 0;
    }

    private void LatencyProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_updatingLatencyControls || !IsLoaded ||
            LatencyProfileBox.SelectedValue is not LatencyProfile profile)
        {
            return;
        }

        _config.LatencyProfile = profile;
        _config.Save();
        UpdateTrayLatency();
        UpdateLatencyHint();

        if (!_engine.IsRunning)
        {
            SetStatus("Le nouveau profil de latence sera utilisé au prochain démarrage de la stéréo.");
            return;
        }

        _engine.Stop();
        _diagnosticsTimer.Stop();
        SetRunningState(isRunning: false);
        StartStereo();
    }

    private void DistanceBox_TextChanged(object sender, TextChangedEventArgs eventArgs) =>
        UpdateGeometryCorrection();

    private void ModeRadioButton_Checked(object sender, RoutedEventArgs eventArgs)
    {
        if (!IsLoaded || sender is not RadioButton { Tag: string modeName } ||
            !Enum.TryParse(modeName, out PlaybackMode mode))
        {
            return;
        }

        _config.PlaybackMode = mode;
        _config.Save();
        _engine.SetPlaybackMode(mode);
        SetStatus(mode == PlaybackMode.Stereo
            ? "Mode stéréo : chaque canal utilise son enceinte."
            : "Mode mono : le même mix est envoyé aux deux enceintes.");
    }

    private void SwapButton_Click(object sender, RoutedEventArgs eventArgs)
    {
        var leftId = LeftDeviceBox.SelectedValue;
        var rightId = RightDeviceBox.SelectedValue;
        LeftDeviceBox.SelectedValue = rightId;
        RightDeviceBox.SelectedValue = leftId;
        SaveConfig();
        SetStatus("Les enceintes gauche et droite ont été inversées.");
    }

    private void RefreshDevices()
    {
        var previousLeftId = LeftDeviceBox.SelectedValue as string ?? _config.LeftDeviceId;
        var previousRightId = RightDeviceBox.SelectedValue as string ?? _config.RightDeviceId;

        try
        {
            _refreshingDevices = true;
            _devices = AudioDeviceManager.GetActiveRenderDevices();
            var source = _devices.FirstOrDefault(device => device.IsDefault);
            _sourceEndpointId = source?.Id;
            _hasWindowsEndpointLevel = false;
            SourceText.Text = source?.Name ?? "No default render endpoint";

            var outputs = SpeakerSelection.Outputs(_devices);
            LeftDeviceBox.ItemsSource = outputs;
            RightDeviceBox.ItemsSource = outputs;

            var pair = SpeakerSelection.Select(_devices, previousLeftId, previousRightId,
                _config.LeftFriendlyName, _config.RightFriendlyName);
            LeftDeviceBox.SelectedValue = pair.Left?.Id;
            RightDeviceBox.SelectedValue = pair.Right?.Id;

            RefreshWindowsVolume(forceApply: true);

            if (pair.Left is null || pair.Right is null)
                SetStatus("Une enceinte manque ou reste à sélectionner. Connecte les Bose puis actualise ; aucune sortie de remplacement n’est choisie.");
            else if (source is not null && !SpeakerSelection.IsCable(source))
                SetStatus("Enceintes sélectionnées. Choisis VB-CABLE comme sortie Windows par défaut, puis actualise.");
            else
                SetStatus($"Enceintes : gauche « {pair.Left.Name} », droite « {pair.Right.Name} ». Vérifie leur position avec le test G → D.");
        }
        catch (Exception error)
        {
            SourceText.Text = "Unavailable";
            SetStatus($"Impossible d’énumérer les périphériques audio : {error.Message}", isError: true);
        }
        finally { _refreshingDevices = false; }
    }

    private void OnPlaybackFaulted(object? sender, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _engine.Stop();
            _diagnosticsTimer.Stop();
            UpdateDiagnostics();
            SetRunningState(isRunning: false);
            SetStatus(message, isError: true);
        });
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _deviceTimer.Stop();
        _tray.Visible = false;
        var trayIcon = _tray.Icon;
        _tray.Dispose();
        trayIcon?.Dispose();
        _diagnosticsTimer.Stop();
        _windowsVolumeTimer.Stop();
        SaveConfig();
        _engine.PlaybackFaulted -= OnPlaybackFaulted;
        _engine.Dispose();
    }

    private void SetRunningState(bool isRunning)
    {
        _trayStart.Enabled = !isRunning && !_isCalibrating;
        _trayStop.Enabled = isRunning && !_isCalibrating;
        _trayExit.Enabled = !_isCalibrating;
        _tray.Text = isRunning ? "SoundTouch Stereo Bridge · actif" : "SoundTouch Stereo Bridge · arrêté";
        RoomProfilePanel.IsEnabled = !_isCalibrating;
        StartButton.IsEnabled = !isRunning && !_isCalibrating;
        StopButton.IsEnabled = isRunning && !_isCalibrating;
        TestButton.IsEnabled = isRunning && !_isCalibrating;
        AlignmentTestButton.IsEnabled = isRunning && !_isCalibrating;
        AutoCalibrateButton.IsEnabled = isRunning && !_isCalibrating;
        SwapButton.IsEnabled = !isRunning;
        RefreshButton.IsEnabled = !isRunning;
        LeftDeviceBox.IsEnabled = !isRunning;
        RightDeviceBox.IsEnabled = !isRunning;
        LatencyProfileBox.IsEnabled = !_isCalibrating;
        UpdateTrayLatency();
    }

    private void AdjustDelay(StereoSide side, double deltaMilliseconds)
    {
        var deltaSamples = (int)Math.Round(deltaMilliseconds * _sampleRate / 1_000d);
        if (side == StereoSide.Left)
        {
            _config.LeftDelaySamples = Math.Clamp(_config.LeftDelaySamples + deltaSamples, 0, _sampleRate);
            _engine.SetAlignmentDelay(side, _config.LeftDelaySamples);
        }
        else
        {
            _config.RightDelaySamples = Math.Clamp(_config.RightDelaySamples + deltaSamples, 0, _sampleRate);
            _engine.SetAlignmentDelay(side, _config.RightDelaySamples);
        }

        _config.Save();
        UpdateDelayLabels();
    }

    private void UpdateDelayLabels()
    {
        LeftDelayText.Text = FormatDelay(_config.LeftDelaySamples);
        RightDelayText.Text = FormatDelay(_config.RightDelaySamples);
    }

    private string FormatDelay(int delaySamples)
    {
        var milliseconds = delaySamples * 1_000d / _sampleRate;
        return $"Retard de synchro {milliseconds:0.00} ms · {delaySamples} échantillons";
    }

    private void SaveConfig()
    {
        if (LeftDeviceBox.SelectedItem is AudioDeviceInfo left)
        {
            _config.LeftDeviceId = left.Id;
            _config.LeftFriendlyName = left.Name;
        }
        if (RightDeviceBox.SelectedItem is AudioDeviceInfo right)
        {
            _config.RightDeviceId = right.Id;
            _config.RightFriendlyName = right.Name;
        }
        if (TryReadCalibrationParameters(out var parameters, showError: false))
        {
            _config.MicrophoneToLeftMeters = parameters.MicrophoneToLeftMeters;
            _config.MicrophoneToRightMeters = parameters.MicrophoneToRightMeters;
            _config.ListenerToLeftMeters = parameters.ListenerToLeftMeters;
            _config.ListenerToRightMeters = parameters.ListenerToRightMeters;
        }
        _config.Save();
    }

    private void RefreshWindowsVolume(bool forceApply = false)
    {
        if (_sourceEndpointId is null)
        {
            WindowsVolumeText.Text = "—";
            return;
        }

        try
        {
            var level = AudioDeviceManager.GetEndpointLevel(_sourceEndpointId);
            var changed = !_hasWindowsEndpointLevel ||
                          Math.Abs(level.VolumePercent - _windowsEndpointLevel.VolumePercent) > 0.05 ||
                          Math.Abs(level.VolumeDecibels - _windowsEndpointLevel.VolumeDecibels) > 0.01 ||
                          level.IsMuted != _windowsEndpointLevel.IsMuted;
            _windowsEndpointLevel = level;
            _hasWindowsEndpointLevel = true;

            _updatingVolumeControls = true;
            WindowsVolumeSlider.Value = level.VolumePercent;
            WindowsVolumeText.Text = level.IsMuted ? "Muet" : $"{level.VolumePercent:0}%";

            if (changed || forceApply)
            {
                ApplyDigitalLevels(save: false);
            }
        }
        catch
        {
            WindowsVolumeText.Text = "—";
        }
        finally
        {
            _updatingVolumeControls = false;
        }
    }

    private void InitializeDigitalLevelControls()
    {
        if (!_config.MasterBoostDecibels.HasValue || !_config.BalanceDecibels.HasValue)
        {
            var migrated = DigitalLevelMath.FromGains(_config.LeftGain, _config.RightGain);
            _config.MasterBoostDecibels = migrated.MasterBoostDecibels;
            _config.BalanceDecibels = migrated.BalanceDecibels;
        }

        _config.MasterBoostDecibels = Math.Clamp(
            _config.MasterBoostDecibels.Value,
            0,
            DigitalLevelMath.MaximumBoostDecibels);
        _config.BalanceDecibels = Math.Clamp(
            _config.BalanceDecibels.Value,
            -DigitalLevelMath.MaximumBalanceDecibels,
            DigitalLevelMath.MaximumBalanceDecibels);

        _updatingLevelControls = true;
        MasterBoostSlider.Value = _config.MasterBoostDecibels.Value;
        BalanceSlider.Value = _config.BalanceDecibels.Value;
        _updatingLevelControls = false;
        ApplyDigitalLevels(save: false);
    }

    private void InitializePlaybackMode()
    {
        if (!Enum.IsDefined(_config.PlaybackMode))
        {
            _config.PlaybackMode = PlaybackMode.Stereo;
        }

        if (_config.PlaybackMode == PlaybackMode.Mono)
        {
            MonoModeButton.IsChecked = true;
        }
        else
        {
            StereoModeButton.IsChecked = true;
        }
    }

    private void InitializeCalibrationControls()
    {
        MicLeftDistanceBox.Text = FormatDistance(_config.MicrophoneToLeftMeters);
        MicRightDistanceBox.Text = FormatDistance(_config.MicrophoneToRightMeters);
        ListenerLeftDistanceBox.Text = FormatDistance(_config.ListenerToLeftMeters);
        ListenerRightDistanceBox.Text = FormatDistance(_config.ListenerToRightMeters);
        UpdateGeometryCorrection();
    }

    private void InitializeLatencyControls()
    {
        if (!Enum.IsDefined(_config.LatencyProfile))
        {
            _config.LatencyProfile = LatencyProfile.Balanced;
        }

        LatencyProfileBox.ItemsSource = new[]
        {
            new LatencyProfileOption(LatencyProfile.Stable, "Stable · 100 / 100 ms"),
            new LatencyProfileOption(LatencyProfile.Balanced, "Équilibré · 40 / 60 ms"),
            new LatencyProfileOption(LatencyProfile.LowLatency, "Vidéo · tampon minimal"),
        };
        _updatingLatencyControls = true;
        LatencyProfileBox.SelectedValue = _config.LatencyProfile;
        _updatingLatencyControls = false;
        UpdateLatencyHint();
    }

    private bool TryReadCalibrationParameters(
        out CalibrationParameters parameters,
        bool showError)
    {
        parameters = new CalibrationParameters(0, 0, 0, 0);
        if (!TryParseDistance(MicLeftDistanceBox.Text, out var microphoneLeft) ||
            !TryParseDistance(MicRightDistanceBox.Text, out var microphoneRight) ||
            !TryParseDistance(ListenerLeftDistanceBox.Text, out var listenerLeft) ||
            !TryParseDistance(ListenerRightDistanceBox.Text, out var listenerRight))
        {
            if (showError)
            {
                SetStatus("Saisis les quatre distances en mètres, entre 0 et 50.", isError: true);
            }
            return false;
        }

        parameters = new CalibrationParameters(microphoneLeft, microphoneRight, listenerLeft, listenerRight);
        return true;
    }

    private static bool TryParseDistance(string text, out double value)
    {
        var parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                     double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return parsed && double.IsFinite(value) && value is >= 0 and <= 50;
    }

    private static string FormatDistance(double distance) =>
        distance.ToString("0.0#", CultureInfo.CurrentCulture);

    private void SetCalibrationBusy(bool isBusy)
    {
        LeftOutputPanel.IsEnabled = !isBusy;
        RightOutputPanel.IsEnabled = !isBusy;
        WindowsVolumeSlider.IsEnabled = !isBusy;
        MasterBoostSlider.IsEnabled = !isBusy;
        BalanceSlider.IsEnabled = !isBusy;
        StereoModeButton.IsEnabled = !isBusy;
        MonoModeButton.IsEnabled = !isBusy;
        MicLeftDistanceBox.IsEnabled = !isBusy;
        MicRightDistanceBox.IsEnabled = !isBusy;
        ListenerLeftDistanceBox.IsEnabled = !isBusy;
        ListenerRightDistanceBox.IsEnabled = !isBusy;
        SetRunningState(_engine.IsRunning);
    }

    private void ApplyDigitalLevels(bool save)
    {
        var master = MasterBoostSlider.Value;
        var balance = BalanceSlider.Value;
        var gains = DigitalLevelMath.CalculateGains(master, balance);
        var effectiveGains = CalculateEffectiveGains(gains);
        _config.MasterBoostDecibels = master;
        _config.BalanceDecibels = balance;
        _config.LeftGain = gains.LeftGain;
        _config.RightGain = gains.RightGain;
        _engine.SetOutputGain(StereoSide.Left, (float)effectiveGains.LeftGain);
        _engine.SetOutputGain(StereoSide.Right, (float)effectiveGains.RightGain);

        MasterBoostText.Text = $"{FormatSignedDecibels(master)} · {DigitalLevelMath.DecibelsToGain(master):0.00}×";
        BalanceText.Text = balance switch
        {
            > 0.001 => $"G {balance:+0.00} dB",
            < -0.001 => $"D {-balance:+0.00} dB",
            _ => "Centré",
        };
        LeftDigitalLevelText.Text = FormatEffectiveLevel("Gauche", effectiveGains.LeftGain);
        RightDigitalLevelText.Text = FormatEffectiveLevel("Droite", effectiveGains.RightGain);

        if (save)
        {
            _config.Save();
        }
    }

    private (double LeftGain, double RightGain) CalculateEffectiveGains() =>
        CalculateEffectiveGains(DigitalLevelMath.CalculateGains(
            _config.MasterBoostDecibels ?? 0,
            _config.BalanceDecibels ?? 0));

    private (double LeftGain, double RightGain) CalculateEffectiveGains(
        (double LeftGain, double RightGain) pairGains)
    {
        var windowsGain = !_hasWindowsEndpointLevel
            ? 1d
            : _windowsEndpointLevel.IsMuted || _windowsEndpointLevel.VolumePercent <= 0.01
                ? 0d
                : DigitalLevelMath.DecibelsToGain(_windowsEndpointLevel.VolumeDecibels);
        return (pairGains.LeftGain * windowsGain, pairGains.RightGain * windowsGain);
    }

    private static string FormatEffectiveLevel(string side, double gain) =>
        gain <= 0.000_001
            ? $"{side} muet"
            : $"{side} {FormatSignedDecibels(DigitalLevelMath.GainToDecibels(gain))}";

    private void UpdateGeometryCorrection()
    {
        if (!TryReadCalibrationParameters(out var parameters, showError: false))
        {
            GeometryCorrectionText.Text = "Renseigne les quatre distances en mètres.";
            return;
        }

        var correction = CalibrationMath.GeometryCorrectionMilliseconds(parameters);
        GeometryCorrectionText.Text =
            $"Ces distances déplacent la mesure du micro jusqu’à ta place : correction {FormatSignedDecibels(correction).Replace("dB", "ms")}.";
    }

    private void UpdateLatencyHint()
    {
        var settings = LatencyProfileSettings.For(_config.LatencyProfile);
        LatencyHintText.Text = _config.LatencyProfile switch
        {
            LatencyProfile.Stable => "Marge maximale contre les coupures Bluetooth. C’est le comportement historique.",
            LatencyProfile.Balanced => "Réglage recommandé : moins de retard tout en gardant une marge raisonnable.",
            LatencyProfile.LowLatency => "Vidéo : tampon WASAPI minimal, repli automatique si indisponible. Le délai Bluetooth reste à ajouter.",
            _ => string.Empty,
        } + (_config.LatencyProfile == LatencyProfile.LowLatency
            ? $" Capture demandée {settings.CaptureBufferMilliseconds} ms ; sorties au minimum du pilote (secours {settings.OutputBufferMilliseconds} ms)."
            : $" Capture demandée {settings.CaptureBufferMilliseconds} ms, sorties {settings.OutputBufferMilliseconds} ms.");
    }

    private void DiagnosticsTimer_Tick(object? sender, EventArgs eventArgs) => UpdateDiagnostics();

    private void WindowsVolumeTimer_Tick(object? sender, EventArgs eventArgs) => RefreshWindowsVolume();

    private void UpdateDiagnostics()
    {
        var diagnostics = _engine.GetDiagnostics();
        if (diagnostics is null)
        {
            DiagnosticsText.Text = "Diagnostics disponibles pendant la lecture.";
            LimiterText.Text = "Limiteur inactif";
            LimiterText.Foreground = Brushes.SlateGray;
            return;
        }

        var left = diagnostics.Outputs.Single(output => output.Side == StereoSide.Left);
        var right = diagnostics.Outputs.Single(output => output.Side == StereoSide.Right);
        DiagnosticsText.Text =
            $"Capture demandée {diagnostics.CaptureBufferMilliseconds} ms · " +
            $"tampons G {left.CurrentBufferMilliseconds:0}/{left.PeakBufferMilliseconds:0} ms, " +
            $"D {right.CurrentBufferMilliseconds:0}/{right.PeakBufferMilliseconds:0} ms\n" +
            $"Sous-alimentations G {left.UnderrunCount}, D {right.UnderrunCount} · " +
            $"rattrapages G {left.BacklogResetCount}, D {right.BacklogResetCount} · " +
            $"audio {left.SourceSampleRate / 1_000d:0.#} → {left.OutputSampleRate / 1_000d:0.#} kHz" +
            $"\nSorties : G {left.Backend} / D {right.Backend}" +
            $" · tampons WASAPI G {FormatMeasuredBuffer(left.WasapiBufferMilliseconds)}, D {FormatMeasuredBuffer(right.WasapiBufferMilliseconds)}" +
            $"\nAudio ancien écarté G {left.DroppedMilliseconds:0} ms / D {right.DroppedMilliseconds:0} ms. Hors délai Bluetooth.";

        var limiterActive = left.LimiterActive || right.LimiterActive;
        LimiterText.Text = limiterActive ? "Limiteur actif" : "Limiteur inactif";
        LimiterText.Foreground = limiterActive ? Brushes.Orange : Brushes.SlateGray;
    }

    private static string FormatMeasuredBuffer(double? value) => value is { } ms ? $"{ms:0.#} ms" : "non mesuré";

    private static string FormatSignedDecibels(double decibels) =>
        $"{decibels:+0.00;-0.00;0.00} dB";

    private static string FormatLatencyProfile(LatencyProfile profile) => profile switch
    {
        LatencyProfile.Stable => "Stable",
        LatencyProfile.Balanced => "Équilibré",
        LatencyProfile.LowLatency => "Faible latence",
        _ => profile.ToString(),
    };

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? Brushes.LightCoral
            : Brushes.WhiteSmoke;
    }

    private sealed record LatencyProfileOption(LatencyProfile Profile, string Name);
}
