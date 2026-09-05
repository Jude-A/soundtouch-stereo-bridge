using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using StereoBridge.Core;
using Forms = System.Windows.Forms;

namespace StereoBridge.App;

public partial class MainWindow
{
    private readonly Forms.NotifyIcon _tray = new();
    private readonly Forms.ToolStripMenuItem _trayStart = new("Démarrer le bridge");
    private readonly Forms.ToolStripMenuItem _trayStop = new("Arrêter le bridge");
    private readonly Dictionary<LatencyProfile, Forms.ToolStripMenuItem> _trayLatencyItems = new();
    private readonly Forms.ToolStripMenuItem _trayExit = new("Quitter");
    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _wantRunning;
    private bool _exiting;
    private bool _updatingProfiles;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "SoundTouchStereoBridge";

    private void InitializeIntegration()
    {
        LeftDeviceBox.SelectionChanged += SaveEndpointSelection;
        RightDeviceBox.SelectionChanged += SaveEndpointSelection;
        System.Windows.Application.Current.SessionEnding += (_, _) => _exiting = true;
        using (var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app-icon.ico")).Stream)
        using (var icon = new System.Drawing.Icon(resource))
        {
            _tray.Icon = (System.Drawing.Icon)icon.Clone();
        }
        _tray.Text = "SoundTouch Stereo Bridge · arrêté";
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir", null, (_, _) => ShowFromTray());
        menu.Items.Add(_trayStart);
        menu.Items.Add(_trayStop);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Profil de latence") { Enabled = false });
        foreach (var (profile, label) in new[]
        {
            (LatencyProfile.Stable, "Stable"),
            (LatencyProfile.Balanced, "Équilibré"),
            (LatencyProfile.LowLatency, "Vidéo · tampon minimal"),
        })
        {
            var item = new Forms.ToolStripMenuItem(label);
            item.Click += (_, _) =>
            {
                if (!_isCalibrating && _config.LatencyProfile != profile)
                    LatencyProfileBox.SelectedValue = profile;
            };
            _trayLatencyItems.Add(profile, item);
            menu.Items.Add(item);
        }
        UpdateTrayLatency();
        menu.Opening += (_, _) => UpdateTrayLatency();
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Créer un raccourci sur le bureau", null, (_, _) => CreateDesktopShortcut());
        menu.Items.Add(_trayExit);
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Left) return;
            if (IsVisible) Hide();
            else ShowFromTray();
        };
        _trayStart.Click += (_, _) => { if (!_isCalibrating) { _wantRunning = true; RefreshDevices(); StartStereo(); } };
        _trayStop.Click += (_, _) => { if (!_isCalibrating) StopButton_Click(this, new RoutedEventArgs()); };
        _trayExit.Click += (_, _) => { if (!_isCalibrating) { _exiting = true; Close(); } };
        _tray.Visible = true;
        Closing += (_, e) =>
        {
            // Windows session shutdown still follows WPF's normal shutdown path.
            if (!_exiting) { e.Cancel = true; SaveConfig(); Hide(); }
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        _deviceTimer.Tick += (_, _) => CheckAutomaticStart();
        AutoStartBox.IsChecked = _config.AutoStartBridge;
        StartInTrayBox.IsChecked = _config.StartInTray;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            WindowsStartupBox.IsChecked = key?.GetValue(RunValue) is string;
        }
        catch (Exception error) { SetStatus(error.Message, true); }
        RefreshProfileControls();
        SetRunningState(false);
    }

    private void UpdateTrayLatency()
    {
        foreach (var (profile, item) in _trayLatencyItems)
        {
            item.Checked = _config.LatencyProfile == profile;
            item.Enabled = !_isCalibrating;
        }
    }

    private void SaveEndpointSelection(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_refreshingDevices && !_engine.IsRunning) SaveConfig();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void CreateDesktopShortcut()
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Chemin de l’application indisponible.");
            if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                executable = System.IO.Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
            }
            if (!System.IO.File.Exists(executable))
                throw new InvalidOperationException("L’exécutable est introuvable. Compile l’application avant de créer le raccourci.");

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                throw new InvalidOperationException("Le dossier Bureau est indisponible.");
            System.IO.Directory.CreateDirectory(desktop);
            var shortcutPath = System.IO.Path.Combine(desktop, "SoundTouch Stereo Bridge.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Le service Windows de création de raccourcis est indisponible.");
            shell = Activator.CreateInstance(shellType)!;
            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            dynamic link = shortcut;
            link.TargetPath = executable;
            link.Arguments = string.Empty;
            link.WorkingDirectory = System.IO.Path.GetDirectoryName(executable);
            link.IconLocation = executable + ",0";
            link.Description = "Lancer SoundTouch Stereo Bridge";
            link.Save();
            SetStatus("Raccourci créé ou mis à jour sur le bureau.");
            _tray.ShowBalloonTip(4000, "SoundTouch Stereo Bridge", "Raccourci créé ou mis à jour sur le bureau.", Forms.ToolTipIcon.Info);
        }
        catch (Exception error)
        {
            SetStatus($"Impossible de créer le raccourci : {error.Message}", isError: true);
            _tray.ShowBalloonTip(6000, "Raccourci non créé", error.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            if (shortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private void StartupOptions_Click(object sender, RoutedEventArgs e)
    {
        _config.AutoStartBridge = AutoStartBox.IsChecked == true;
        _config.StartInTray = StartInTrayBox.IsChecked == true;
        _wantRunning = _config.AutoStartBridge;
        SaveConfig();
        CheckAutomaticStart();
    }

    private void WindowsStartup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (WindowsStartupBox.IsChecked == true)
            {
                var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Chemin de l’application indisponible.");
                if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Lance l’exécutable StereoBridge.App.exe pour activer le démarrage Windows.");
                key.SetValue(RunValue, $"\"{executable}\"");
            }
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
            SetStatus("Préférence de démarrage Windows enregistrée pour cette session utilisateur.");
        }
        catch (Exception error)
        {
            WindowsStartupBox.IsChecked = !(WindowsStartupBox.IsChecked == true);
            SetStatus($"Démarrage Windows non modifié : {error.Message}", true);
        }
    }

    private void CheckAutomaticStart()
    {
        if (_isCalibrating || !_wantRunning || !_config.AutoStartBridge) return;
        try
        {
            var devices = AudioDeviceManager.GetActiveRenderDevices();
            var source = devices.FirstOrDefault(d => d.IsDefault);
            var ready = AutomaticStartPolicy.CanStart(devices, _config.LeftDeviceId, _config.RightDeviceId);
            if (_engine.IsRunning)
            {
                if (ready && source?.Id == _sourceEndpointId) return;
                _engine.Stop();
                _diagnosticsTimer.Stop();
                SetRunningState(false);
                UpdateDiagnostics();
            }
            if (!ready)
            {
                SetStatus("En attente de VB-CABLE comme sortie Windows et des deux enceintes mémorisées.");
                return;
            }
            RefreshDevices();
            // Automatic restarts must use the saved pair, never a UI fallback.
            _refreshingDevices = true;
            LeftDeviceBox.SelectedValue = _config.LeftDeviceId;
            RightDeviceBox.SelectedValue = _config.RightDeviceId;
            _refreshingDevices = false;
            StartStereo();
        }
        catch (Exception error) { SetStatus($"Reconnexion en attente : {error.Message}", true); }
    }

    private void RefreshProfileControls()
    {
        _updatingProfiles = true;
        RoomProfileBox.ItemsSource = null;
        RoomProfileBox.ItemsSource = _config.RoomProfiles;
        RoomProfileBox.SelectedValue = _config.ActiveRoomProfileId;
        ProfileNameBox.Text = _config.RoomProfiles.Single(p => p.Id == _config.ActiveRoomProfileId).Name;
        InitializeCalibrationControls();
        UpdateDelayLabels();
        UpdateCalibrationHistory();
        _updatingProfiles = false;
    }

    private void UpdateCalibrationHistory()
    {
        CalibrationHistoryText.Text = _config.LastCalibrationUtc is { } date
            ? $"Dernière calibration : {date.ToLocalTime():g} · {_config.LastCalibrationMicrophone} · confiance {_config.LastCalibrationConfidence:0.00}"
            : "Aucune auto-calibration enregistrée pour ce profil.";
    }

    private void ApplyRoomProfile()
    {
        _engine.SetAlignmentDelay(StereoSide.Left, _config.LeftDelaySamples);
        _engine.SetAlignmentDelay(StereoSide.Right, _config.RightDelaySamples);
        _config.Save();
        RefreshProfileControls();
        SetStatus($"Profil actif : {ProfileNameBox.Text}. Géométrie et retards chargés.");
    }

    private void RoomProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfiles || _isCalibrating || RoomProfileBox.SelectedValue is not string id) return;
        SaveConfig();
        _config.SelectRoomProfile(id);
        ApplyRoomProfile();
    }

    private void AddProfile(bool duplicate)
    {
        if (_isCalibrating) return;
        SaveConfig();
        var name = ProfileNameBox.Text.Trim();
        _config.AddRoomProfile(name.Length == 0 ? "Nouvelle position" : name + (duplicate ? " (copie)" : ""), duplicate);
        ApplyRoomProfile();
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e) => AddProfile(false);
    private void DuplicateProfile_Click(object sender, RoutedEventArgs e) => AddProfile(true);
    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (_isCalibrating || name.Length == 0) return;
        SaveConfig();
        _config.RoomProfiles.Single(p => p.Id == _config.ActiveRoomProfileId).Name = name;
        _config.Save();
        RefreshProfileControls();
    }
    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_isCalibrating || _config.RoomProfiles.Count <= 1) return;
        if (MessageBox.Show(this, "Supprimer le profil actif et ses retards enregistrés ?", "Supprimer le profil",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _config.DeleteActiveRoomProfile();
        ApplyRoomProfile();
    }
}
