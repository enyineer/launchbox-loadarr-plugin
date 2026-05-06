using System.Collections.Generic;
using System.Net.Http;

namespace Loadarr.Sources
{
    /// <summary>
    /// Builds the list of providers that the search UI should query.
    /// Order in this list = display order in the UI.
    /// </summary>
    internal static class SourceRegistry
    {
        public static IReadOnlyList<IRomSource> Build(HttpClient http)
        {
            return new List<IRomSource>
            {
                new VimmsLairSource(http),
            };
        }
    }
}
