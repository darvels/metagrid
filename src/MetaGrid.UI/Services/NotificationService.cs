using System.Windows.Forms;
using MetaGrid.Core.Abstractions;

namespace MetaGrid.UI.Services;

public sealed class NotificationService : INotificationService
{
    public NotifyIcon? NotifyIcon { get; set; }

    public void ShowInfo(string title, string message) => Show(title, message, ToolTipIcon.Info);
    public void ShowSuccess(string title, string message) => Show(title, message, ToolTipIcon.Info);
    public void ShowError(string title, string message) => Show(title, message, ToolTipIcon.Error);

    private void Show(string title, string message, ToolTipIcon icon)
    {
        NotifyIcon?.ShowBalloonTip(3500, title, message, icon);
    }
}
