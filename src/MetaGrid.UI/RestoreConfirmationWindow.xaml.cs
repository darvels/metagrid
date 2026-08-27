using System.Windows;
using MetaGrid.Core.Models;

namespace MetaGrid.UI;

public partial class RestoreConfirmationWindow : Window
{
    public RestoreConfirmationWindow(BackupEntry backup, string accountDisplayName, string targetPath)
    {
        InitializeComponent();
        MessageTextBlock.Text = "MetaGrid will overwrite the currently selected Dota hero-grid configuration with the chosen backup.";
        DetailsTextBlock.Text =
            $"Account: {accountDisplayName}\n" +
            $"Backup time: {backup.CreatedAt.LocalDateTime:g}\n" +
            $"Backup size: {backup.FileSizeText}\n" +
            $"Backup hash: {backup.BackupHash ?? "Unavailable"}\n" +
            $"Original path: {backup.OriginalPath ?? "Unavailable"}\n" +
            $"Target path: {targetPath}";
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
