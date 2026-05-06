using System.IO;
using System.Linq;
using System.Text;

namespace Loadarr.Services
{
    internal static class StringNormalize
    {
        public static string AlphaNumLower(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        public static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "rom";
            var invalid = Path.GetInvalidFileNameChars();
            return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        }
    }
}
