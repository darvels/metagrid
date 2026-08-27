using System.Windows;
using MetaGrid.Core.Models;

namespace MetaGrid.UI;

public partial class BackupRestoreWindow : Window
{
    public BackupRestoreWindow(IReadOnlyList<BackupEntry> backups)
    {
        InitializeComponent();
        BackupListBox.ItemsSource = backups;
        BackupListBox.SelectionChanged += (_, _) => RestoreButton.IsEnabled = SelectedBackup is not null;
        if (backups.Count > 0)
        {
            BackupListBox.SelectedIndex = 0;
            RestoreButton.IsEnabled = true;
        }
        else
        {
            BackupListBox.Visibility = Visibility.Collapsed;
            EmptyStateTextBlock.Visibility = Visibility.Visible;
            RestoreButton.IsEnabled = false;
        }
    }

    public BackupEntry? SelectedBackup => BackupListBox.SelectedItem as BackupEntry;

    private void RestoreSelectedBackupClick(object sender, RoutedEventArgs e)
    {
        if (SelectedBackup is null)
        {
            return;
        }

        DialogResult = true;
    }
}
