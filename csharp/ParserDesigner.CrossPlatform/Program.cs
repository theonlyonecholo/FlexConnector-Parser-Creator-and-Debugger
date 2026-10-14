using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace ParserDesigner.CrossPlatform;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        BuildApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildApp() => AppBuilder.Configure<DesignerApp>().UsePlatformDetect().LogToTrace();
}

public class DesignerApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        // Use consistent form alignment regardless of the platform's font metrics.
        Styles.Add(new Avalonia.Styling.Style(s => s.OfType<Avalonia.Controls.ComboBox>())
        {
            Setters =
            {
                new Avalonia.Styling.Setter(Avalonia.Layout.Layoutable.VerticalAlignmentProperty, Avalonia.Layout.VerticalAlignment.Center),
                new Avalonia.Styling.Setter(Avalonia.Layout.Layoutable.HorizontalAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch),
                new Avalonia.Styling.Setter(Avalonia.Layout.Layoutable.MinHeightProperty, 34d)
            }
        });
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
