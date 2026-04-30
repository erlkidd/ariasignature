namespace AriaSignature.UI.Services;

public static class ThemeService
{
    public static readonly IReadOnlyCollection<string> Themes = ["Светлая", "Тёмная"];

    public static void Apply(string themeName)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var lightPath = "Themes/AriaTheme.xaml";
        var darkPath = "Themes/AriaTheme.Dark.xaml";

        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith(lightPath, StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith(darkPath, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new System.Windows.ResourceDictionary
        {
            Source = new Uri(themeName == "Тёмная" ? darkPath : lightPath, UriKind.Relative)
        });
    }
}
