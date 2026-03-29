namespace DevProjex.Avalonia;

internal static class Program
{
    // Conservative GPU cache limit to avoid long-session native memory growth.
    private const long SkiaGpuCacheLimitBytes = 96L * 1024 * 1024;

    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(CreateWin32PlatformOptions())
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = SkiaGpuCacheLimitBytes
            });

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }

    internal static Win32PlatformOptions CreateWin32PlatformOptions()
        => new()
        {
            // Rounded WinUI composition backdrops prevent square translucent corners
            // on blurred top-levels such as popups, tooltips and borderless dialogs.
            WinUICompositionBackdropCornerRadius = 12f
        };
}
