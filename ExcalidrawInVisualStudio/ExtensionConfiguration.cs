using System.Drawing;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.PlatformUI;

namespace ExcalidrawInVisualStudio;

internal class ExtensionConfiguration
{
    public string GetLibraryPath()
    {
        var libraryPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        libraryPath = Path.Combine(libraryPath, "Excalidraw", "library.excalidrawlib");
        return libraryPath;
    }

    private bool IsColorLight(Color clr)
    {
        return 5 * clr.G + 2 * clr.R + clr.B > 8 * 128;
    }

    public string GetVsTheme()
    {
        return IsColorLight(VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey))
            ? "light"
            : "dark";
    }

    public string GetUserDataFolder()
    {
        return Path.Combine(Path.GetTempPath(), Assembly.GetExecutingAssembly().GetName().Name);
    }

    public string GetEditorSiteFolder()
    {
        var assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return Path.Combine(assemblyLocation!, "editor");
    }
}