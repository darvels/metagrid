using MetaGrid.Core.Models;

namespace MetaGrid.Core.Services;

public static class GuideStartupSync
{
    public static async Task RunAsync(IEnumerable<GuideSubscriptionRecord> subscriptions,
        Func<int, GuideRole, CancellationToken, Task> sync, CancellationToken cancellationToken)
    {
        // Settings normalization replaces records; retain identities, not mutable record references.
        var enabled = subscriptions.Where(record => record.IsEnabled || record.NeedsRemoval)
            .Select(record => (record.HeroId, record.Role)).Distinct().ToArray();
        foreach (var (heroId, role) in enabled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sync(heroId, role, cancellationToken);
        }
    }
}
