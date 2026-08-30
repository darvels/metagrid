using MetaGrid.Core.Models;

namespace MetaGrid.Tests;

public sealed class UiTextServiceTests
{
    [Fact]
    public void Translate_WhenRussianSelected_LocalizesKnownStatusCopy()
    {
        var text = new UiTextService
        {
            Language = AppLanguage.Russian
        };

        Assert.Equal("Сетка установлена", text.Translate("Grid installed"));
        Assert.Equal("Проверить обновления", text.CheckForUpdates);
        Assert.Equal("Главная", text.Dashboard);
    }
}
