using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using TmSync.Core.Abstractions;
using TmSync.Core.Models;
using TmSync.Core.State;
using TmSync.Service;

namespace TmSync.Manager;

public partial class MainWindow : Window
{
    private readonly ConfigService _config = new();
    private StateStore? _state;
    private bool _syncRunning;

    public MainWindow()
    {
        InitializeComponent();

        var path = ConfigService.LocateDefault() ?? PromptForConfig();
        if (path is null)
        {
            Application.Current.Shutdown();
            return;
        }

        LoadConfig(path);
    }

    private static string? PromptForConfig()
    {
        MessageBox.Show(
            "No TmSync configuration file was found.\n\n" +
            "Select the appsettings.json used by the TmSync service, or choose a location to create a new one.",
            "TmSync Manager", MessageBoxButton.OK, MessageBoxImage.Information);

        var dialog = new SaveFileDialog
        {
            Title = "Select or create the TmSync configuration file",
            FileName = "appsettings.json",
            Filter = "JSON configuration (*.json)|*.json",
            OverwritePrompt = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void LoadConfig(string path)
    {
        _config.Load(path);
        ConfigPathText.Text = _config.ConfigPath;
        _state = new StateStore(_config.ResolveStateDbPath());

        LoadSettingsTab();
        RefreshUsers();
        RefreshStatus();
        SetStatus("Configuration loaded.");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private IStateStore State => _state ?? throw new InvalidOperationException("No configuration loaded.");

    // =================== Users tab ===================

    public sealed record UserRow(SyncUser User)
    {
        public string StaffCode => User.StaffCode;
        public string Mailbox => User.Mailbox;
        public bool Enabled => User.Enabled;
        public bool Calendar => User.Calendar;
        public bool Contacts => User.Contacts;
        public bool Tasks => User.Tasks;
        public bool EmailJournal => User.EmailJournal;
        public string DirectionLabel => User.Direction switch
        {
            SyncDirection.TwoWay => "Two-way",
            SyncDirection.TimeMattersToM365 => "One-way: TM → Office 365",
            SyncDirection.M365ToTimeMatters => "One-way: Office 365 → TM",
            _ => "Default (global setting)"
        };
    }

    private void RefreshUsers()
    {
        var users = State.ListUsers();
        UsersGrid.ItemsSource = users.Select(u => new UserRow(u)).ToList();

        var selected = SyncUserCombo.SelectedItem as string;
        var items = new List<string> { "All users" };
        items.AddRange(users.Select(u => u.StaffCode));
        SyncUserCombo.ItemsSource = items;
        SyncUserCombo.SelectedItem = selected != null && items.Contains(selected) ? selected : "All users";
    }

    private void RefreshUsers_Click(object sender, RoutedEventArgs e) => RefreshUsers();

    private void AddUser_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UserDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            if (State.GetUser(dialog.Result.StaffCode) != null)
            {
                MessageBox.Show($"A user with staff code '{dialog.Result.StaffCode}' already exists.",
                    "TmSync Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            State.AddOrUpdateUser(dialog.Result);
            RefreshUsers();
            SetStatus($"User '{dialog.Result.StaffCode}' added.");
        }
    }

    private void EditUser_Click(object sender, RoutedEventArgs e) => EditSelectedUser();

    private void UsersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedUser();

    private void EditSelectedUser()
    {
        if (UsersGrid.SelectedItem is not UserRow row) return;
        var dialog = new UserDialog(row.User) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            State.AddOrUpdateUser(dialog.Result);
            RefreshUsers();
            SetStatus($"User '{dialog.Result.StaffCode}' updated.");
        }
    }

    private void RemoveUser_Click(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow row)
        {
            MessageBox.Show("Select a user first.", "TmSync Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Stop syncing '{row.StaffCode}' ({row.Mailbox})?\n\n" +
            "Existing records in Time Matters and Office 365 are left in place.\n\n" +
            "Yes = remove and purge sync history\nNo = remove but keep sync history\nCancel = do nothing",
            "Remove user", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return;
        State.RemoveUser(row.StaffCode, purgeState: answer == MessageBoxResult.Yes);
        RefreshUsers();
        SetStatus($"User '{row.StaffCode}' removed.");
    }

    private void ToggleUser_Click(object sender, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow row)
        {
            MessageBox.Show("Select a user first.", "TmSync Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        State.AddOrUpdateUser(row.User with { Enabled = !row.User.Enabled });
        RefreshUsers();
        SetStatus($"User '{row.StaffCode}' {(row.User.Enabled ? "disabled" : "enabled")}.");
    }

    // =================== Settings tab ===================

    private void LoadSettingsTab()
    {
        SqlConnBox.Text = _config.GetValue(["TimeMatters", "ConnectionString"], "");
        TenantBox.Text = _config.GetValue(["Microsoft365", "TenantId"], "");
        ClientIdBox.Text = _config.GetValue(["Microsoft365", "ClientId"], "");
        SecretBox.Password = _config.GetValue(["Microsoft365", "ClientSecret"], "");

        IntervalBox.Text = _config.GetValue(["Sync", "IntervalMinutes"], 5).ToString();
        SelectByTag(DirectionCombo, _config.GetValue(["Sync", "Direction"], "TwoWay"));
        SelectByTag(ConflictCombo, _config.GetValue(["Sync", "ConflictPolicy"], "NewestWins"));
        DeletesCheck.IsChecked = _config.GetValue(["Sync", "PropagateDeletes"], true);
        PastDaysBox.Text = _config.GetValue(["Sync", "Calendar", "PastDays"], 30).ToString();
        FutureDaysBox.Text = _config.GetValue(["Sync", "Calendar", "FutureDays"], 365).ToString();

        ModCalendarCheck.IsChecked = _config.GetValue(["Sync", "Modules", "Calendar"], true);
        ModContactsCheck.IsChecked = _config.GetValue(["Sync", "Modules", "Contacts"], true);
        ModTasksCheck.IsChecked = _config.GetValue(["Sync", "Modules", "Tasks"], true);
        ModEmailCheck.IsChecked = _config.GetValue(["Sync", "Modules", "EmailJournal"], false);

        StateDbBox.Text = _config.GetValue(["Sync", "StateDatabasePath"], "tmsync-state.db");
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 1 ||
            !int.TryParse(PastDaysBox.Text, out var pastDays) || pastDays < 0 ||
            !int.TryParse(FutureDaysBox.Text, out var futureDays) || futureDays < 1)
        {
            MessageBox.Show("Interval and calendar window values must be valid numbers.",
                "TmSync Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.Set(["TimeMatters", "ConnectionString"], SqlConnBox.Text.Trim());
        _config.Set(["Microsoft365", "TenantId"], TenantBox.Text.Trim());
        _config.Set(["Microsoft365", "ClientId"], ClientIdBox.Text.Trim());
        _config.Set(["Microsoft365", "ClientSecret"], SecretBox.Password);

        _config.Set(["Sync", "IntervalMinutes"], interval);
        _config.Set(["Sync", "Direction"], SelectedTag(DirectionCombo));
        _config.Set(["Sync", "ConflictPolicy"], SelectedTag(ConflictCombo));
        _config.Set(["Sync", "PropagateDeletes"], DeletesCheck.IsChecked == true);
        _config.Set(["Sync", "Calendar", "PastDays"], pastDays);
        _config.Set(["Sync", "Calendar", "FutureDays"], futureDays);

        _config.Set(["Sync", "Modules", "Calendar"], ModCalendarCheck.IsChecked == true);
        _config.Set(["Sync", "Modules", "Contacts"], ModContactsCheck.IsChecked == true);
        _config.Set(["Sync", "Modules", "Tasks"], ModTasksCheck.IsChecked == true);
        _config.Set(["Sync", "Modules", "EmailJournal"], ModEmailCheck.IsChecked == true);

        _config.Set(["Sync", "StateDatabasePath"], StateDbBox.Text.Trim());

        _config.Save();
        _state = new StateStore(_config.ResolveStateDbPath());
        RefreshUsers();
        RefreshStatus();
        SetStatus("Settings saved. Restart the TmSync service for the changes to take effect there.");
    }

    private void ReloadSettings_Click(object sender, RoutedEventArgs e)
    {
        LoadConfig(_config.ConfigPath);
    }

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open TmSync configuration",
            Filter = "JSON configuration (*.json)|*.json",
            CheckFileExists = false,
            FileName = "appsettings.json"
        };
        if (dialog.ShowDialog() == true)
        {
            LoadConfig(dialog.FileName);
        }
    }

    private async void TestSql_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Testing SQL Server connection...");
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(SqlConnBox.Text.Trim());
            await conn.OpenAsync();
            MessageBox.Show($"Connected to {conn.DataSource}, database '{conn.Database}'.",
                "SQL connection OK", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("SQL connection OK.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SQL connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("SQL connection failed.");
        }
    }

    private async void TestGraph_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Testing Microsoft 365 sign-in...");
        try
        {
            var credential = new ClientSecretCredential(TenantBox.Text.Trim(), ClientIdBox.Text.Trim(), SecretBox.Password);
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(["https://graph.microsoft.com/.default"]), CancellationToken.None);
            MessageBox.Show($"Authentication succeeded. Token valid until {token.ExpiresOn.UtcDateTime:u}.",
                "Microsoft 365 connection OK", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("Microsoft 365 authentication OK.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Microsoft 365 authentication failed", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Microsoft 365 authentication failed.");
        }
    }

    // =================== Status tab ===================

    public sealed record StatusRow(string StaffCode, string Module, string LastRun, string Incremental, string Watermark);

    private void RefreshStatus()
    {
        var rows = new List<StatusRow>();
        foreach (var user in State.ListUsers())
        {
            foreach (var moduleKey in new[] { "Calendar", "Contacts", "Tasks", "EmailJournal:inbox", "EmailJournal:sentitems" })
            {
                var s = State.GetModuleState(moduleKey, user.StaffCode);
                if (s.LastRunUtc is null && s.DeltaToken is null) continue;
                rows.Add(new StatusRow(
                    user.StaffCode,
                    moduleKey,
                    s.LastRunUtc?.ToString("u") ?? "never",
                    s.DeltaToken != null ? "yes" : "full sync next",
                    s.TmWatermarkUtc?.ToString("u") ?? "-"));
            }
        }
        StatusGrid.ItemsSource = rows;
    }

    private void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
        SetStatus("Status refreshed.");
    }

    // =================== Run Sync tab ===================

    private void AppendLog(string line)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private async void RunSync_Click(object sender, RoutedEventArgs e)
    {
        if (_syncRunning) return;

        string? staffCode = SyncUserCombo.SelectedItem as string;
        if (staffCode == "All users") staffCode = null;

        SyncModule? module = SyncModuleCombo.SelectedIndex switch
        {
            1 => SyncModule.Calendar,
            2 => SyncModule.Contacts,
            3 => SyncModule.Tasks,
            4 => SyncModule.EmailJournal,
            _ => null
        };

        _syncRunning = true;
        RunSyncButton.IsEnabled = false;
        SetStatus("Sync running...");
        AppendLog($"=== Sync started {DateTime.Now:G} (user: {staffCode ?? "all"}, module: {module?.ToString() ?? "all"}) ===");

        try
        {
            await Task.Run(async () =>
            {
                var services = new ServiceCollection();
                services.AddLogging(b => b.AddProvider(new CallbackLoggerProvider(AppendLog)).SetMinimumLevel(LogLevel.Information));
                services.AddTmSync(_config.BuildConfiguration());
                await using var provider = services.BuildServiceProvider();

                var runner = provider.GetRequiredService<SyncRunner>();
                await runner.RunOnceAsync(staffCode, module, CancellationToken.None);
            });
            AppendLog($"=== Sync finished {DateTime.Now:G} ===");
            SetStatus("Sync pass complete.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            SetStatus("Sync failed - see log.");
        }
        finally
        {
            _syncRunning = false;
            RunSyncButton.IsEnabled = true;
            RefreshStatus();
        }
    }
}
