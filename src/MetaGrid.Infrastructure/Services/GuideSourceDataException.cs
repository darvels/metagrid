using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class GuideSourceDataException(GuideFailureKind kind, string message) : Exception(message)
{
    public GuideFailureKind Kind { get; } = kind;
}
