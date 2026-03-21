using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DevProjex.Tests.UI.AvaloniaHeadlessTestApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DevProjex.Tests.UI;

public static class AvaloniaHeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp()
        => Program.BuildAvaloniaApp().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
