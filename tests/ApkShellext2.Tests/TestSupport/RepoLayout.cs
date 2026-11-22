using System;
using System.IO;

namespace ApkShellext2.Tests
{
    /// <summary>
    /// Locates the repository root from the directory the test assembly runs in
    /// (tests\ApkShellext2.Tests\bin\&lt;Configuration&gt;\net48). Tests must read the
    /// checked-in files themselves - a copy dropped into the output directory would
    /// defeat a "single source" assertion - so we walk up until the marker pair
    /// (version.txt + ApkShellext2\ApkShellext2.csproj) is found.
    /// </summary>
    internal static class RepoLayout
    {
        private static readonly Lazy<string> s_root = new Lazy<string>(Locate);

        public static string Root
        {
            get { return s_root.Value; }
        }

        public static string PathOf(string relativePath)
        {
            return System.IO.Path.Combine(Root, relativePath);
        }

        private static string Locate()
        {
            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "version.txt")) &&
                    File.Exists(System.IO.Path.Combine(dir.FullName, "ApkShellext2", "ApkShellext2.csproj")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "repository root (version.txt + ApkShellext2\\ApkShellext2.csproj) not found above " +
                AppDomain.CurrentDomain.BaseDirectory +
                "; run the tests from a normal build under tests\\ApkShellext2.Tests\\bin.");
        }
    }
}
