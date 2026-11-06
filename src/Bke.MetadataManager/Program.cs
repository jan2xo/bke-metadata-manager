using Avalonia;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Keep the existing cross-platform Avalonia UI for Windows/Linux.
        // macOS SwiftUI uses the SAME safe mover through this JSON stdin/stdout bridge.
        if (args.Length == 1 && args[0] == "--organize-json")
            return RunBridgeAsync().GetAwaiter().GetResult();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static async Task<int> RunBridgeAsync()
    {
        try
        {
            var json = await Console.In.ReadToEndAsync();
            var response = await OrganizerBridge.ProcessJsonAsync(json);
            Console.Out.WriteLine(response);
            return 0; // individual errors are returned per file, not hidden
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Organizer bridge failed: " + ex.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
