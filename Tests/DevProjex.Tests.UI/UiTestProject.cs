namespace DevProjex.Tests.UI;

internal sealed class UiTestProject : IDisposable
{
    private readonly string _rootPath;

    private UiTestProject(string rootPath)
    {
        _rootPath = rootPath;
    }

    public string RootPath => _rootPath;

    public static UiTestProject CreateDefault()
    {
        return Create(static rootPath =>
        {
            SeedDefaultWorkspace(rootPath);
        });
    }

    public static UiTestProject CreateWithScopedExtensionlessEntries()
    {
        return Create(static rootPath =>
        {
            SeedDefaultWorkspace(rootPath);
            WriteFile(rootPath, Path.Combine("src", "Makefile"), "build:\n\tdotnet build");
        });
    }

    public static UiTestProject CreateWithDynamicIgnoreEntries()
    {
        return Create(static rootPath =>
        {
            SeedDefaultWorkspace(rootPath);
            WriteFile(rootPath, Path.Combine("src", "Makefile"), "build:\n\tdotnet build");
            WriteFile(rootPath, Path.Combine("src", "empty.txt"), string.Empty);
            Directory.CreateDirectory(Path.Combine(rootPath, "src", "empty-folder"));
        });
    }

    public static UiTestProject CreateWithExtensionSensitiveEmptyFolders()
    {
        return Create(static rootPath =>
        {
            WriteFile(rootPath, Path.Combine("src", "ExtensionSensitive", "keep.cs"), BuildCSharpFile("AppCore.ExtensionSensitive", "Keep", 12));
            WriteFile(rootPath, Path.Combine("src", "ExtensionSensitive", "mixed-parent", "docs", "readme.md"), BuildMarkdown("Extension-sensitive folder", 12));
        });
    }

    public static UiTestProject CreateWithGitIgnoredExtensionlessNoise()
    {
        return Create(static rootPath =>
        {
            WriteFile(rootPath, ".gitignore", "obj/\nbin/\n");
            WriteFile(rootPath, "README", "visible extensionless");
            WriteFile(rootPath, Path.Combine("src", "Program.cs"), BuildCSharpFile("UiProbe", "Program", 8));
            WriteFile(rootPath, Path.Combine("obj", "Debug", "net10.0", "apphost"), "smart ignored apphost");
            WriteFile(rootPath, Path.Combine("obj", "Debug", "net10.0", "singlefilehost"), "smart ignored host");
            WriteFile(rootPath, Path.Combine("bin", "Debug", "net10.0", "createdump"), "smart ignored dump");
        });
    }

    public static UiTestProject CreateWithDotFolderExtensionlessNoise()
    {
        return Create(static rootPath =>
        {
            WriteFile(rootPath, "README", "visible extensionless");
            WriteFile(rootPath, Path.Combine("src", "Program.cs"), BuildCSharpFile("UiProbe", "Program", 8));

            for (var index = 0; index < 128; index++)
            {
                WriteFile(
                    rootPath,
                    Path.Combine(".cache", "nested", $"artifact-{index:000}"),
                    $"noise {index}");
            }
        });
    }

    private static UiTestProject Create(Action<string> seedWorkspace)
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "DevProjex",
            "DevProjex.Tests.UI",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(rootPath);
        seedWorkspace(rootPath);

        return new UiTestProject(rootPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
                Directory.Delete(_rootPath, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures from background file handles on CI.
        }
    }

    private static void WriteFile(string rootPath, string relativePath, string content)
    {
        var fullPath = Path.Combine(rootPath, relativePath);
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            Directory.CreateDirectory(directoryPath);

        File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SeedDefaultWorkspace(string rootPath)
    {
        WriteFile(rootPath, "README.md", BuildMarkdown("DevProjex UI test workspace", 24));
        WriteFile(rootPath, Path.Combine("docs", "app-preview-notes.md"), BuildMarkdown("App preview notes", 32));
        WriteFile(rootPath, Path.Combine("configs", "appsettings.json"), BuildJson("Production"));
        WriteFile(rootPath, Path.Combine("configs", "appsettings.Development.json"), BuildJson("Development"));
        WriteFile(rootPath, Path.Combine("src", "AppHost", "Program.cs"), BuildCSharpFile("AppHost", "Program", 52));
        WriteFile(rootPath, Path.Combine("src", "AppHost", "AppBootstrap.cs"), BuildCSharpFile("AppHost", "AppBootstrap", 44));
        WriteFile(rootPath, Path.Combine("src", "AppCore", "Services", "AppService.cs"), BuildCSharpFile("AppCore.Services", "AppService", 68));
        WriteFile(rootPath, Path.Combine("src", "AppCore", "Services", "PreviewService.cs"), BuildCSharpFile("AppCore.Services", "PreviewService", 74));
        WriteFile(rootPath, Path.Combine("src", "AppCore", "Features", "ApplicationFeature.cs"), BuildCSharpFile("AppCore.Features", "ApplicationFeature", 58));
        WriteFile(rootPath, Path.Combine("src", "AppCore", "Features", "FilterSupport.cs"), BuildCSharpFile("AppCore.Features", "FilterSupport", 46));
        WriteFile(rootPath, Path.Combine("src", "AppCore", "ViewModels", "AppViewModel.cs"), BuildCSharpFile("AppCore.ViewModels", "AppViewModel", 64));
        WriteFile(rootPath, Path.Combine("src", "AppCore", "Widgets", "AppWidget.cs"), BuildCSharpFile("AppCore.Widgets", "AppWidget", 48));
        WriteFile(rootPath, Path.Combine("tests", "AppHost.Tests", "AppServiceTests.cs"), BuildCSharpFile("AppHost.Tests", "AppServiceTests", 36));
        WriteFile(rootPath, Path.Combine("tests", "AppHost.Tests", "PreviewFeatureTests.cs"), BuildCSharpFile("AppHost.Tests", "PreviewFeatureTests", 42));
    }

    private static string BuildMarkdown(string title, int lineCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        for (var index = 1; index <= lineCount; index++)
            builder.AppendLine($"- app note line {index}: preview workspace stays readable and stable.");

        return builder.ToString();
    }

    private static string BuildJson(string environmentName)
    {
        return $$"""
        {
          "ApplicationName": "DevProjex.Tests.UI",
          "Environment": "{{environmentName}}",
          "Features": {
            "PreviewWorkspace": true,
            "AppSearch": true,
            "AppFilter": true
          }
        }
        """;
    }

    private static string BuildCSharpFile(string @namespace, string typeName, int methodCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"namespace {@namespace};");
        builder.AppendLine();
        builder.AppendLine($"public sealed class {typeName}");
        builder.AppendLine("{");

        for (var index = 1; index <= methodCount; index++)
        {
            builder.AppendLine($"    public string BuildAppValue{index}()");
            builder.AppendLine("    {");
            builder.AppendLine($"        return \"app-value-{index}\";");
            builder.AppendLine("    }");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        return builder.ToString();
    }
}
