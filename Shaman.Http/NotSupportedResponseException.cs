using System;
using System.Collections.Generic;
using System.Linq;
#if NATIVE_HTTP
using System.Net;
#else
using System.Net.Reimpl;
#endif
using System.Text;
using System.Threading.Tasks;
#if !STANDALONE
using HttpUtils = Shaman.Utils;
#endif

namespace Shaman.Runtime
{
    public class NotSupportedResponseException : WebException
    {

        public string ContentType { get; private set; }
        public LazyUri ResponseUrl { get; private set; }

        public NotSupportedResponseException(string retrievedContentType, LazyUri finalUrl)
            : base(
                  (retrievedContentType != null && retrievedContentType.Contains("html", StringComparison.OrdinalIgnoreCase) ?
                  "The server returned data which, although marked as " + retrievedContentType + ", doesn't look like actual HTML." :
                  "The server returned an unsupported Content-Type: " + retrievedContentType + ".") +
                  " If the response is supposed to be interpreted as plain text, add the #$assume-text=1 meta parameter. If it is HTML, add #$assume-html=1", HttpUtils.UnexpectedResponseType)
        {
            this.ContentType = retrievedContentType;
            this.ResponseUrl = finalUrl;
        }

    }
}
