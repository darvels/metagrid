using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class D2ptNoGuideBuildException(int heroId, GuideRole role)
    : Exception($"No D2PT build in the retrieved public build index for hero {heroId}, role {role}.");
