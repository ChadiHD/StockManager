using System.Diagnostics;
using FlaUI.Core;

namespace SMDesktopUI.UITests;

/// <summary>
/// Locates and launches the built SMDesktopUI.exe for UI automation.
/// </summary>
internal static class DesktopApp
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string TargetFrameworkFolder = "net10.0-windows7.0";

    /// <summary>
    /// Resolves the desktop exe. Honors the SMDESKTOPUI_EXE environment variable if set,
    /// otherwise walks up from the test output directory to the repo root (the folder that
    /// contains StockManager.sln) and builds the expected bin path.
    /// </summary>
    public static string ResolveExePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("SMDESKTOPUI_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath))
            {
                throw new FileNotFoundException(
                    $"SMDESKTOPUI_EXE points at a file that does not exist: '{overridePath}'.");
            }

            return overridePath;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StockManager.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new FileNotFoundException(
                "Could not locate StockManager.sln above the test output directory.");
        }

        var exe = Path.Combine(
            dir.FullName, "SMDesktopUI", "bin", Configuration, TargetFrameworkFolder, "SMDesktopUI.exe");

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException(
                $"SMDesktopUI.exe was not found at '{exe}'. Build SMDesktopUI in {Configuration} first, " +
                "or set the SMDESKTOPUI_EXE environment variable to the exe path.");
        }

        return exe;
    }

    public static Application Launch()
    {
        var exe = ResolveExePath();

        // The app reads appsettings.json from the current working directory (see Bootstrapper),
        // so launch it from its own bin folder or startup throws.
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false
        };

        return Application.Launch(startInfo);
    }
}
