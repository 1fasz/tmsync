using System.Windows;
using TmSync.Core.Models;

namespace TmSync.Manager;

public partial class UserDialog : Window
{
    public SyncUser? Result { get; private set; }

    public UserDialog(SyncUser? existing = null)
    {
        InitializeComponent();

        if (existing != null)
        {
            Title = $"Edit Sync User - {existing.StaffCode}";
            StaffBox.Text = existing.StaffCode;
            StaffBox.IsEnabled = false;
            MailboxBox.Text = existing.Mailbox;
            CalendarCheck.IsChecked = existing.Calendar;
            ContactsCheck.IsChecked = existing.Contacts;
            TasksCheck.IsChecked = existing.Tasks;
            EmailCheck.IsChecked = existing.EmailJournal;
            EnabledCheck.IsChecked = existing.Enabled;
            DirectionCombo.SelectedIndex = existing.Direction switch
            {
                SyncDirection.TwoWay => 1,
                SyncDirection.TimeMattersToM365 => 2,
                SyncDirection.M365ToTimeMatters => 3,
                _ => 0
            };
        }
        else
        {
            Title = "Add Sync User";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var staff = StaffBox.Text.Trim();
        var mailbox = MailboxBox.Text.Trim();

        if (staff.Length == 0)
        {
            MessageBox.Show("Enter the Time Matters staff code.", "Sync User", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (mailbox.Length == 0 || !mailbox.Contains('@'))
        {
            MessageBox.Show("Enter a valid Office 365 mailbox address (user@domain).", "Sync User", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new SyncUser(
            staff,
            mailbox,
            Enabled: EnabledCheck.IsChecked == true,
            Calendar: CalendarCheck.IsChecked == true,
            Contacts: ContactsCheck.IsChecked == true,
            Tasks: TasksCheck.IsChecked == true,
            EmailJournal: EmailCheck.IsChecked == true,
            Direction: DirectionCombo.SelectedIndex switch
            {
                1 => SyncDirection.TwoWay,
                2 => SyncDirection.TimeMattersToM365,
                3 => SyncDirection.M365ToTimeMatters,
                _ => null
            });

        DialogResult = true;
    }
}
