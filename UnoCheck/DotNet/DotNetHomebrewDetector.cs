using System;

namespace DotNetCheck.DotNet
{
    internal static class DotNetHomebrewDetector
    {
        public static bool IsHomebrewInstall(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return path.StartsWith("/opt/homebrew", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/Cellar/dotnet/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
