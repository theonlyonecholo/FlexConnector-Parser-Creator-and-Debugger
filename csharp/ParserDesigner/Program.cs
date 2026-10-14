namespace ParserDesigner;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Keep framework diagnostics in English without changing data/regex culture.
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
