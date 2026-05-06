using System.IO;

namespace Loadarr.Sources.VimmsLair.Tests.Fixtures
{
    internal static class FixtureLoader
    {
        public static string Load(string name)
        {
            var dir = Path.Combine(Path.GetDirectoryName(typeof(FixtureLoader).Assembly.Location), "Fixtures");
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Fixture not found: {path}");
            return File.ReadAllText(path);
        }
    }
}
