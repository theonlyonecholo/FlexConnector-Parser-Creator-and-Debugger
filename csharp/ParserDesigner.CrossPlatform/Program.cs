using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

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
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
