using System.IO;

namespace DatasetHub.Desktop;

internal static class ConfigFileLocator
{
    public static string DirectoryPath
    {
        get
        {
            var executable = new DirectoryInfo(AppContext.BaseDirectory);
            // Visual Studio outputs to <project>/run/<configuration>; use <project>/config.
            if (executable.Parent?.Name.Equals("run", StringComparison.OrdinalIgnoreCase) == true)
                return Path.Combine(executable.Parent.Parent!.FullName, "config");
            return Path.Combine(executable.FullName, "config");
        }
    }

    public static string ExamplePath => Path.Combine(DirectoryPath, "collector.local-example.json");
}
