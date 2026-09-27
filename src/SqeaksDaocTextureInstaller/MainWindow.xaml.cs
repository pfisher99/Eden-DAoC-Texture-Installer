using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SqeaksDaocTextures;

public partial class MainWindow : Window
{
    private readonly ArchiveService _archives = new();
    private readonly InstallerService _installer = new();
    private readonly LogService _log = new();
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    public ObservableCollection<ArchiveComponent> Components { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ThemeManager.ApplyToWindow(this);
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var options = App.StartupOptions;
        var settings = SettingsService.Load();
        TargetPathBox.Text = options.TargetPath ?? SettingsService.FindDaocPath(settings.TargetPath) ?? "";
        var preferredArchives = options.ArchivePath is not null
            ? ArchiveLocator.ResolveSelected(options.ArchivePath)
            : ArchiveLocator.FindPreferred(AppContext.BaseDirectory, settings.ArchivePath);
        ArchivePathBox.Text = preferredArchives?.Part1Path ?? options.ArchivePath ?? settings.ArchivePath ?? "";
        UpdateThemeControls();
        UpdateLocationValidation();
        await RefreshInventoryAsync();

        if (!string.IsNullOrWhiteSpace(options.ElevatedAction) && options.Components is { Count: > 0 })
        {
            foreach (var component in Components)
                component.IsSelected = options.Components.Contains(component.Name, StringComparer.OrdinalIgnoreCase);
            if (options.ElevatedAction.Equals("install", StringComparison.OrdinalIgnoreCase)) await RunInstallAsync(true);
            else if (options.ElevatedAction.Equals("swap", StringComparison.OrdinalIgnoreCase)) await RunSwapAsync(true);
            else if (options.ElevatedAction.Equals("uninstall", StringComparison.OrdinalIgnoreCase)) await RunUninstallAsync(true);
            Application.Current.Shutdown();
        }
    }

    private async Task RefreshInventoryAsync()
    {
        if (_busy) return;
        try
        {
            Components.Clear();
            var archive = ArchivePathBox.Text.Trim();
            var archivePair = ArchiveLocator.ResolveSelected(archive);
            var target = TargetPathBox.Text.Trim();
            UpdateLocationValidation();
            var targetValid = SettingsService.IsDaocPath(target);
            var archiveValid = ArchiveLocator.IsComplete(archivePair);
            if (targetValid)
            {
                var recovered = await Task.Run(() => InstallerService.RecoverIfNeeded(target));
                if (recovered) MessageBox.Show(this, "An interrupted operation was safely recovered.", "Recovery complete");
            }

            IReadOnlyList<ArchiveComponent> inventory = [];
            if (archiveValid)
            {
                SetStatus("Reading archive inventory...");
                try { inventory = await Task.Run(() => _archives.Inspect(archivePair!)); }
                catch (Exception ex)
                {
                    SetValidation(ArchiveValidationText, "Archive pair is corrupt or unreadable", "ErrorBrush");
                    throw new InvalidDataException("Both texture archive parts were found, but the pair could not be read.", ex);
                }
            }
            var combined = inventory.ToDictionary(component => component.Name, StringComparer.OrdinalIgnoreCase);
            if (targetValid)
                foreach (var installed in InstallerService.DiscoverInstalledComponents(target))
                    combined.TryAdd(installed.Name, installed);

            foreach (var component in combined.Values.OrderBy(component => component.Name, StringComparer.OrdinalIgnoreCase))
            {
                component.TargetExists = targetValid && Directory.Exists(Path.Combine(target, component.Name));
                component.State = targetValid
                    ? StateDetector.Detect(target, component.Name)
                    : ComponentState.Missing;
                component.IsSelected = component.State == ComponentState.NotInstalled;
                component.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ArchiveComponent.IsSelected)) UpdateButtons();
                };
                Components.Add(component);
            }

            var settings = SettingsService.Load();
            settings.TargetPath = target;
            settings.ArchivePath = archivePair?.Part1Path ?? archive;
            SettingsService.Save(settings);
            SetStatus(targetValid && archiveValid
                ? $"Found {Components.Count} texture folders. Choose what you'd like to change."
                : targetValid && Components.Count > 0
                    ? "One or both texture archive parts are unavailable. Installed folders can still be switched or uninstalled."
                    : !targetValid
                        ? "Choose your DAoC folder to make texture folders available."
                        : "Choose either texture archive part to install textures.");
        }
        catch (Exception ex)
        {
            _log.Write(ex.ToString());
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "Unable to read installer data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { UpdateButtons(); }
    }

    private async void BrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Dark Age of Camelot installation folder", Multiselect = false };
        if (Directory.Exists(TargetPathBox.Text)) dialog.InitialDirectory = TargetPathBox.Text;
        if (dialog.ShowDialog(this) == true)
        {
            TargetPathBox.Text = dialog.FolderName;
            await RefreshInventoryAsync();
        }
    }

    private async void BrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose either texture archive part",
            Filter = "7-Zip archives (*.7z)|*.7z|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            ArchivePathBox.Text = ArchiveLocator.ResolveSelected(dialog.FileName)?.Part1Path ?? dialog.FileName;
            await RefreshInventoryAsync();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshInventoryAsync();
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunInstallAsync(false);
    private async void Swap_Click(object sender, RoutedEventArgs e) => await RunSwapAsync(false);
    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var selected = Components.Where(c => c.IsSelected).ToList();
        var noun = selected.Count == 1 ? "folder" : "folders";
        var result = MessageBox.Show(this,
            $"Uninstall textures for {selected.Count} selected {noun}?\n\n" +
            "Your original folders will be restored first. The installed texture copies will then be permanently deleted.",
            "Uninstall selected textures", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result == MessageBoxResult.OK) await RunUninstallAsync(false);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateLocationValidation();
        UpdateButtons();
    }

    private void SelectAvailable_Click(object sender, RoutedEventArgs e)
    {
        foreach (var component in Components.Where(c => c.CanSelect)) component.IsSelected = true;
        UpdateButtons();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var component in Components) component.IsSelected = false;
        UpdateButtons();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e) => ThemePopup.IsOpen = true;

    private void ThemeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value }) return;
        var preference = ThemeManager.ParsePreference(value);
        ThemeManager.SetPreference(preference);
        var settings = SettingsService.Load();
        settings.Theme = preference.ToString();
        SettingsService.Save(settings);
        ThemePopup.IsOpen = false;
        UpdateThemeControls();
    }

    private async Task RunInstallAsync(bool alreadyElevated)
    {
        var selected = Components.Where(c => c.IsSelected).ToList();
        if (!alreadyElevated && !FileSystemService.CanWrite(TargetPathBox.Text.Trim()))
        {
            await RelaunchElevatedAsync("install", selected);
            await RefreshInventoryAsync();
            return;
        }

        await RunOperationAsync("Installing textures", async (progress, token) =>
            await _installer.InstallAsync(
                ArchiveLocator.ResolveSelected(ArchivePathBox.Text.Trim()) ??
                throw new InvalidOperationException("Choose either archive part and keep both matching parts together."),
                TargetPathBox.Text.Trim(), selected, progress, token));
    }

    private async Task RunSwapAsync(bool alreadyElevated)
    {
        var selected = Components.Where(c => c.IsSelected).ToList();
        if (!alreadyElevated && !FileSystemService.CanWrite(TargetPathBox.Text.Trim()))
        {
            await RelaunchElevatedAsync("swap", selected);
            await RefreshInventoryAsync();
            return;
        }

        await RunOperationAsync("Switching texture folders", async (progress, _) =>
            await _installer.SwapAsync(TargetPathBox.Text.Trim(), selected, progress));
    }

    private async Task RunUninstallAsync(bool alreadyElevated)
    {
        var selected = Components.Where(c => c.IsSelected).ToList();
        if (!alreadyElevated && !FileSystemService.CanWrite(TargetPathBox.Text.Trim()))
        {
            await RelaunchElevatedAsync("uninstall", selected);
            await RefreshInventoryAsync();
            return;
        }

        await RunOperationAsync("Uninstalling textures", async (progress, _) =>
            await _installer.UninstallAsync(TargetPathBox.Text.Trim(), selected, progress), false);
    }

    private async Task RunOperationAsync(string title,
        Func<IProgress<OperationProgress>, CancellationToken, Task> operation, bool canCancel = true)
    {
        SetBusy(true, canCancel);
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<OperationProgress>(p =>
        {
            var overallPercent = Math.Clamp(p.Percent, 0, 100);
            var folderPercent = Math.Clamp(p.ComponentPercent, 0, 100);
            OverallProgressBar.Value = overallPercent;
            FolderProgressBar.Value = folderPercent;
            OverallProgressPercentText.Text = $"{overallPercent:0}%";
            FolderProgressPercentText.Text = $"{folderPercent:0}%";
            FolderProgressLabelText.Text = string.IsNullOrWhiteSpace(p.ComponentName)
                ? "Current folder"
                : $"Current folder: {p.ComponentName}";
            StatusText.Text = p.Status;
            CurrentFileText.Text = p.CurrentFile ?? "";
        });
        try
        {
            _log.Write($"Starting: {title}");
            await operation(progress, _cancellation.Token);
            _log.Write($"Completed: {title}");
            MessageBox.Show(this, $"{title} completed successfully.", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            _log.Write($"Cancelled and rolled back: {title}");
            MessageBox.Show(this, "The operation was cancelled. Any live-folder changes were rolled back.", "Cancelled");
        }
        catch (Exception ex)
        {
            _log.Write(ex.ToString());
            MessageBox.Show(this, $"{ex.Message}\n\nDetails were written to:\n{_log.LogPath}",
                "Operation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
            await RefreshInventoryAsync();
        }
    }

    private async Task RelaunchElevatedAsync(string action, IReadOnlyList<ArchiveComponent> components)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the installer executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--elevated-action"); start.ArgumentList.Add(action);
        start.ArgumentList.Add("--target"); start.ArgumentList.Add(TargetPathBox.Text.Trim());
        start.ArgumentList.Add("--archive"); start.ArgumentList.Add(ArchivePathBox.Text.Trim());
        foreach (var component in components)
        {
            start.ArgumentList.Add("--component"); start.ArgumentList.Add(component.Name);
        }
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The elevated installer did not start.");
            SetStatus("Waiting for the elevated installer...");
            await process.WaitForExitAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            SetStatus("Administrator permission was cancelled.");
        }
    }

    private void SetBusy(bool value, bool canCancel = true)
    {
        _busy = value;
        TargetPathBox.IsEnabled = !value;
        ArchivePathBox.IsEnabled = !value;
        BrowseTargetButton.IsEnabled = !value;
        BrowseArchiveButton.IsEnabled = !value;
        RefreshButton.IsEnabled = !value;
        ComponentsList.IsEnabled = !value;
        SelectAvailableButton.IsEnabled = !value;
        ClearSelectionButton.IsEnabled = !value;
        CancelButton.Visibility = value && canCancel ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (!value)
        {
            OverallProgressBar.Value = 0;
            FolderProgressBar.Value = 0;
            OverallProgressPercentText.Text = "0%";
            FolderProgressPercentText.Text = "0%";
            FolderProgressLabelText.Text = "Current folder";
            CurrentFileText.Text = "";
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (!IsLoaded) return;
        var targetValid = SettingsService.IsDaocPath(TargetPathBox.Text.Trim());
        var archiveValid = ArchiveLocator.IsComplete(ArchiveLocator.ResolveSelected(ArchivePathBox.Text.Trim()));
        var selected = Components.Where(c => c.IsSelected).ToList();
        SelectionSummaryText.Text = selected.Count == 0
            ? $"{Components.Count} folders found - choose what you'd like to change."
            : $"{selected.Count} of {Components.Count} folders selected.";
        InstallButton.IsEnabled = !_busy && targetValid && archiveValid && selected.Count > 0 &&
                                  selected.All(c => c.State == ComponentState.NotInstalled);
        SwapButton.IsEnabled = !_busy && targetValid && selected.Count > 0 &&
                               selected.All(c => c.State is ComponentState.TextureActive or ComponentState.OriginalActive);
        UninstallButton.IsEnabled = !_busy && targetValid && selected.Count > 0 &&
                                    selected.All(c => c.State is ComponentState.TextureActive or ComponentState.OriginalActive);
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void UpdateLocationValidation()
    {
        if (!IsInitialized) return;
        var target = TargetPathBox.Text.Trim();
        var archive = ArchivePathBox.Text.Trim();
        var targetValid = SettingsService.IsDaocPath(target);
        var archivePair = ArchiveLocator.ResolveSelected(archive);
        var archiveValid = ArchiveLocator.IsComplete(archivePair);
        SetValidation(TargetValidationText,
            string.IsNullOrWhiteSpace(target) ? "Not selected" : targetValid ? "Game files found" : "Needs game.dll and camelot.exe",
            string.IsNullOrWhiteSpace(target) ? "SecondaryTextBrush" : targetValid ? "SuccessBrush" : "ErrorBrush");
        SetValidation(ArchiveValidationText,
            string.IsNullOrWhiteSpace(archive) ? "Not selected" : ArchiveLocator.ValidationMessage(archive),
            string.IsNullOrWhiteSpace(archive) ? "SecondaryTextBrush" : archiveValid ? "SuccessBrush" : "ErrorBrush");
    }

    private static void SetValidation(TextBlock textBlock, string text, string brushResource)
    {
        textBlock.Text = text;
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushResource);
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e) => Dispatcher.Invoke(UpdateThemeControls);

    private void UpdateThemeControls()
    {
        if (!IsInitialized) return;
        ThemeButtonText.Text = ThemeManager.Preference == AppTheme.System
            ? $"System ({ThemeManager.EffectiveTheme})"
            : ThemeManager.Preference.ToString();
        SystemThemeCheck.Visibility = ThemeManager.Preference == AppTheme.System ? Visibility.Visible : Visibility.Hidden;
        LightThemeCheck.Visibility = ThemeManager.Preference == AppTheme.Light ? Visibility.Visible : Visibility.Hidden;
        DarkThemeCheck.Visibility = ThemeManager.Preference == AppTheme.Dark ? Visibility.Visible : Visibility.Hidden;
    }
}
