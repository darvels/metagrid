using System.Windows;
using MetaGrid.UI.ViewModels;

namespace MetaGrid.UI;

public partial class InstallConfirmationWindow : Window
{
    public InstallConfirmationWindow(InstallGridPreview preview)
    {
        StartupDiagnostics.LogDebug("InstallConfirmationWindow constructor entered.");
        InitializeComponent();
        StartupDiagnostics.LogDebug("InstallConfirmationWindow InitializeComponent completed.");
        TitleTextBlock.Text = preview.ActionLabel == "Update Grid"
            ? "Update MetaGrid Hero Grid?"
            : "Install MetaGrid Hero Grid?";
        PreflightTextBlock.Text = preview.HasExistingGridFile
            ? "A backup will be created before any changes are made."
            : "No existing hero grid file was found. MetaGrid will create a new configuration after confirmation.";
        SummaryTextBlock.Text = $"{preview.ActionLabel} for {preview.AccountDisplayName}";
        DetailsTextBlock.Text =
            $"Account: {preview.AccountDisplayName} ({preview.AccountId})\n" +
            $"Source: {preview.Source}\n" +
            $"Heroes: {preview.HeroCount}\n" +
            $"Groups: {preview.GroupCount}\n" +
            $"Available hash: {preview.AvailableHash}\n" +
            $"Target: {preview.TargetPath}";
        BackupStateTextBlock.Text = preview.HasExistingGridFile
            ? "MetaGrid detected an existing hero_grid_config.json file for this account. A backup will be created immediately before any real write occurs after final confirmation."
            : "No existing hero_grid_config.json file was found for this account. MetaGrid will create the configuration safely only after final confirmation. If a configuration appears before installation begins, MetaGrid will re-check it and create a backup before modifying it.";
        ConfirmButton.Content = preview.ActionLabel;
        StartupDiagnostics.LogDebug("InstallConfirmationWindow constructor completed.");
    }

    private void ConfirmClick(object sender, RoutedEventArgs e)
    {
        StartupDiagnostics.LogDebug("InstallConfirmationWindow confirm clicked.");
        DialogResult = true;
    }
}
