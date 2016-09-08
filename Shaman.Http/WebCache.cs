using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shaman.Dom;
using Shaman.Runtime;
using System.IO;

namespace Shaman
{
    internal class WebCache
    {
        public string Result;
        public WebCacheDataType DataType;
        public LazyUri Url;
        public LazyUri PageUrl;
        public LazyUri RedirectUrl;
        public int ErrorCode;
        public string ExceptionType;
        public string ExceptionMessage;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        public Dictionary<string, string> Cookies = new Dictionary<string, string>();
        public string ContentType;
        public DateTime DateRetrieved;
        public string JsExecutionResults;

        internal HtmlNode RecreateNode(LazyUri url, WebRequestOptions cookieDestination, string cachePath)
        {
            if (this.ExceptionType != null) throw Caching.RebuildException(this, url);
            if (this.RedirectUrl != null && this.Result == null) return null;
            HtmlNode html;
            if (this.DataType == WebCacheDataType.Json) html = FizzlerCustomSelectors.JsonToHtml(this.Result, 0, null);
            else if (this.DataType == WebCacheDataType.Text)
            {
                var d = new HtmlDocument();
                html = d.DocumentNode;
                html.SetAttributeValue("plain-text", "1");
                html.AppendTextNode(this.Result);
            }
            else
            {
                html = this.Result.AsHtmlDocumentNode();
            }
            var docnode = html.OwnerDocument.DocumentNode;
            docnode.SetAttributeValue("from-cache", "1");
            if (this.Headers != null)
            {
                foreach (var header in this.Headers)
                {
                    docnode.SetAttributeValue("header-" + header.Key, header.Value);
                }
            }
            if (this.Cookies != null)
            {
                foreach (var cookie in this.Cookies)
                {
                    cookieDestination.AddCookie(cookie.Key, cookie.Value, PriorityCookie.PRIORITY_FromCache);
                }
            }

#if DESKTOP
            if (this.DateRetrieved == default(DateTime)) this.DateRetrieved = File.GetLastWriteTimeUtc(cachePath);
#endif
            docnode.SetAttributeValue("date-retrieved", this.DateRetrieved.ToString("o"));
            if (RedirectUrl != null)
                docnode.SetAttributeValue("redirect-url", this.RedirectUrl.AbsoluteUri);
            docnode.SetAttributeValue("requested-url", url.AbsoluteUri);
            html.OwnerDocument.SetPageUrl(this.PageUrl ?? this.Url);
            return html;
        }
    }

    internal enum WebCacheDataType : byte
    {
        Html,
        Json,
        Text
    }
}
