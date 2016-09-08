using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shaman.Dom;
using Fizzler.Systems.HtmlAgilityPack;
using System.IO;


#if SMALL_LIB_AWDEE
using Shaman.Runtime;
namespace Shaman
#else
namespace Xamasoft
#endif
{
    /// <summary>
    /// Provides web-related extension methods.
    /// </summary>
    public static partial class
#if STANDALONE
 HttpExtensionMethods
#else
 ExtensionMethods
#endif
    {



        private static string TryCleanupJsonBlockingCode(string str, string prefix1, string prefix2, string end)
        {
            if (str.StartsWith(prefix1) || (prefix2 != null && str.StartsWith(prefix2)))
            {
                var start = str.IndexOf(end);
                // If it's -1, we are going to fail anyways
                return str.Substring(start + end.Length);
            }
            return null;
        }

        internal static string CleanupJsonp(string str)
        {
            if (str.IndexOf('\0', 0, Math.Min(str.Length, 40)) != -1) return str;
            string result;

            result = TryCleanupJsonBlockingCode(str, "for(", "for ", ");");
            if (result != null) return result;

            result = TryCleanupJsonBlockingCode(str, "while(", "while ", ");");
            if (result != null) return result;

            result = TryCleanupJsonBlockingCode(str, "throw ", null, ";");
            if (result != null) return result;


            if (str.StartsWith(")]}'")) return str.Substring(4);
            var searchStart = str.Length - 1;
            var maxToCheck = Math.Min(256, str.Length - 1);
            for (int i = 0; i < str.Length && i < 256; i++)
            {
                var ch = str[i];
                if (ch == '"' || ch == '{' || ch == '[' || ch == '\'') return str;
                else if (ch == '=')
                {
                    var exprEnd = str.LastIndexOf(';', searchStart, maxToCheck);
                    return exprEnd != -1 ? str.Substring(i + 1, exprEnd - (i + 1)) : str.Substring(i + 1);
                }
                else if (ch == '(')
                {
                    var exprEnd = str.LastIndexOf(')', searchStart, maxToCheck);
                    return str.Substring(i + 1, exprEnd - (i + 1));
                }
            }

            return str;
        }
    }
}
