using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shaman.Dom;
using System.IO;

namespace Shaman.Runtime
{
    [RestrictedAccess]
    public class PageExecutionResults
    {
        public List<PageExecutionRequest> Requests;
        public string DomHtml;
        public Uri DomUrl;
        public Uri RedirectUrl;
        public string Error;
        public string RequestedUrl;
        public List<KeyValuePair<string, string>> Cookies;



        public HtmlNode GetHtmlNode()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(DomHtml);
            doc.Tag = this;
            var req = Requests.First(x => x.IsFirst);
            if (RedirectUrl != null) req = Requests.FirstOrDefault(x => x.Url == DomUrl);
            if (req != null)
            {
#if NATIVE_HTTP
                if (req.ResponseHeaders == null) throw new System.Net.WebException("Unknown navigation error.");
#else
                if (req.ResponseHeaders == null) throw new System.Net.Reimpl.WebException("Unknown navigation error.");
#endif
                foreach (var header in req.ResponseHeaders)
                {
                    doc.DocumentNode.SetAttributeValue("header-" + header.Key, header.Value);
                }
            }


            foreach (var cook in Cookies)
            {
                doc.DocumentNode.SetAttributeValue("cookie-" + cook.Key, cook.Value);
            }
            doc.SetPageUrl(DomUrl);
            doc.DocumentNode.SetAttributeValue("date-retrieved", DateTime.UtcNow.ToString("o"));
            doc.DocumentNode.SetAttributeValue("requested-url", RequestedUrl);

            foreach (var noscript in doc.DocumentNode.DescendantsAndSelf("noscript").ToList())
            {
                noscript.Remove();
            }

            return doc.DocumentNode;

        }
#if false
        public Task PopulateRequestsAsync()
        {
            return this.Requests.Where(x => x.RequestBody == null).ForEachThrottledAsync(async x =>
            {
                try
                {
                    var req = new WebRequestOptions();
                    foreach (var header in x.RequestHeaders)
                    {
                        req.AddHeader(header.Key, header.Value);
                    }
                    using (var resp = await new LazyUri(x.Url).GetResponseAsync(req))
                    {
                        using (var sr = new StreamReader(await resp.Content.ReadAsStreamAsync()))
                        {
                            x.ResponseBody = await sr.ReadToEndAsync();
                        }
                    }
                }
                catch (Exception)
                {
                }
            }, 1);
        }
#endif
    }

    public class PageExecutionRequest
    {
        public int Id;
        public Uri Url;
        public string RequestBody;
        public string ResponseBody;
        public string Method;
        public long ResponseBodyLength;
        public int? StatusCode;
        public bool IsFirst;
        public List<KeyValuePair<string, string>> RequestHeaders;
        public List<KeyValuePair<string, string>> ResponseHeaders;

        public override string ToString()
        {
            return Url.ToString();
        }

        internal void PopulateHeaders(HtmlDocument doc)
        {
            throw Sanity.NotImplemented();
        }


        public string GetRequestHeader(string name)
        {
            return RequestHeaders != null ? RequestHeaders.FirstOrDefault(x => name.Equals(x.Key, StringComparison.OrdinalIgnoreCase)).Value : null;
        }
        public string GetResponseHeader(string name)
        {
            return ResponseHeaders != null ? ResponseHeaders.FirstOrDefault(x => name.Equals(x.Key, StringComparison.OrdinalIgnoreCase)).Value : null;
        }

    }
}
