namespace MetaGrid.UI.Services;

public static class GuidesLayoutMetrics
{
    // About 249-DIP catalog + 343-DIP editor + gutter; roles wrap inside the editor.
    public const double SplitWidth = 608;
    public const double Gutter = 16;
    public static bool IsSplit(double width) => width >= SplitWidth;
    public static double EditorWidth(double width) => (width - Gutter) * 0.58;
}
