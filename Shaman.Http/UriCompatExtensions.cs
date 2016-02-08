#if CORECLR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shaman
{
#if STANDALONE
    public static partial class HttpExtensionMethods
#else
    public static partial class ExtensionMethods
#endif
    {
        //
        // An internal shortcut into Uri extenisiblity API
        //
        internal static string GetParts(this Uri uri, UriComponents uriParts, UriFormat formatAs)
        {
            return uri.GetComponents(uriParts, formatAs);
        }

        //private static bool NotAny(Flags flags)
        //{
        //    return (_flags & flags) == 0;
        //}

        public static string GetLeftPart(this Uri uri, UriPartial part)
        {
            if (!uri.IsAbsoluteUri)
            {
                throw new InvalidOperationException("The URI must be absolute.");
            }

            // EnsureUriInfo();
            const UriComponents NonPathPart = (UriComponents.Scheme | UriComponents.UserInfo | UriComponents.Host | UriComponents.Port);

            switch (part)
            {
                case UriPartial.Scheme:

                    return uri.GetParts(UriComponents.Scheme | UriComponents.KeepDelimiter, UriFormat.UriEscaped);

                case UriPartial.Authority:

                    //if (NotAny(uri.Flags.AuthorityFound) || IsDosPath)
                    //{
                    //    // V1.0 compatibility.
                    //    // It not return an empty string but instead "scheme:" because it is a LEFT part.
                    //    // Also neither it should check for IsDosPath here

                    //    // From V1.0 comments:

                    //    // anything that didn't have "//" after the scheme name
                    //    // (mailto: and news: e.g.) doesn't have an authority
                    //    //

                    //    return string.Empty;
                    //}
                    return uri.GetParts(NonPathPart, UriFormat.UriEscaped);

                case UriPartial.Path:
                    return uri.GetParts(NonPathPart | UriComponents.Path, UriFormat.UriEscaped);

                case UriPartial.Query:
                    return uri.GetParts(NonPathPart | UriComponents.Path | UriComponents.Query, UriFormat.UriEscaped);
            }
            throw new ArgumentException("part");
        }

    }
}
#endif