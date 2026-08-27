using MetaGrid.Core.Models;

namespace MetaGrid.Tests;

public sealed class HeroGridInstallActionStateTests
{
    [Fact]
    public void Resolve_WhenAvailableExistsAndNothingInstalled_OffersInstallGrid()
    {
        var state = HeroGridInstallActionResolver.Resolve(
            hasValidSelectedAccount: true,
            hasAvailableSnapshot: true,
            availableHash: "NEW",
            installedHash: null,
            originKind: GridOriginKind.NativeD2pt);

        Assert.True(state.CanOfferAction);
        Assert.Equal("Install Grid", state.ActionLabel);
    }

    [Fact]
    public void Resolve_WhenAvailableDiffersFromInstalled_OffersUpdateGrid()
    {
        var state = HeroGridInstallActionResolver.Resolve(
            hasValidSelectedAccount: true,
            hasAvailableSnapshot: true,
            availableHash: "NEW",
            installedHash: "OLD",
            originKind: GridOriginKind.NativeD2pt);

        Assert.True(state.CanOfferAction);
        Assert.Equal("Update Grid", state.ActionLabel);
    }

    [Fact]
    public void Resolve_WhenAvailableMatchesInstalled_HidesAction()
    {
        var state = HeroGridInstallActionResolver.Resolve(
            hasValidSelectedAccount: true,
            hasAvailableSnapshot: true,
            availableHash: "SAME",
            installedHash: "SAME",
            originKind: GridOriginKind.NativeD2pt);

        Assert.False(state.CanOfferAction);
    }

    [Fact]
    public void Resolve_WhenOriginIsCached_HidesAction()
    {
        var state = HeroGridInstallActionResolver.Resolve(
            hasValidSelectedAccount: true,
            hasAvailableSnapshot: true,
            availableHash: "NEW",
            installedHash: "OLD",
            originKind: GridOriginKind.Cached);

        Assert.False(state.CanOfferAction);
    }

    [Fact]
    public void Resolve_WhenNoAvailableSnapshot_HidesAction()
    {
        var state = HeroGridInstallActionResolver.Resolve(
            hasValidSelectedAccount: true,
            hasAvailableSnapshot: false,
            availableHash: "NEW",
            installedHash: null,
            originKind: GridOriginKind.NativeD2pt);

        Assert.False(state.CanOfferAction);
    }

    [Fact]
    public void Resolve_WhenNoSelectedAccount_HidesAction()
    {
        var state = HeroGridInstallActionResolver.Resolve(
            hasValidSelectedAccount: false,
            hasAvailableSnapshot: true,
            availableHash: "NEW",
            installedHash: null,
            originKind: GridOriginKind.NativeD2pt);

        Assert.False(state.CanOfferAction);
    }
}
