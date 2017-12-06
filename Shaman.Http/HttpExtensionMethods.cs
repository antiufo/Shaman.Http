using Fizzler.Systems.HtmlAgilityPack;
#if !SALTARELLE
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif
using Shaman.Dom;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
#if SALTARELLE
using System.Text.Saltarelle;
using LazyUri = System.Uri;
#else
using System.Text;
#if NATIVE_HTTP
using System.Net;
using System.Net.Http;
#else
using System.Net.Reimpl;
using System.Net.Reimpl.Http;
#endif
using HttpStatusCode = System.Net.HttpStatusCode;
#endif
using System.Threading.Tasks;
#if !STANDALONE
using HttpUtils = Shaman.Utils;
using HttpExtensionMethods = Shaman.ExtensionMethods;
#endif
using System.Text.RegularExpressions;
#if NET35
using HttpResponseMessage = System.Net.HttpWebResponse;
using HttpRequestMessage = System.Net.HttpWebRequest;
#else
using TaskEx = System.Threading.Tasks.Task;
#endif



namespace Shaman
{
#if STANDALONE
    public static partial class HttpExtensionMethods
#else
    public static partial class ExtensionMethods
#endif
    {
#if !SALTARELLE
        public static void AppendUriEncoded(this StringBuilder sb, string str)
        {
            for (int i = 0; i < str.Length; i++)
            {
                var ch = str[i];
                if (ch == ' ')
                {
                    sb.Append('+');
                }
                else if (ch == '#' || ch == '?' || ch == '&' || ch == '=' || ch == '%' || ch == '+' || ch < 0x21)
                {
                    sb.Append('%');
                    sb.Append(Hex[ch >> 4]);
                    sb.Append(Hex[ch & 0xF]);
                }
                else
                {
                    sb.Append(ch);
                }
            }

        }
        private const string Hex = "0123456789ABCDEF";


        public static void AppendHtmlEncoded(this StringBuilder sb, string text, int startIndex = 0, int endIndex = -1)
        {
            using (var s = new StringWriter(sb))
            {
                s.WriteHtmlEncoded(text, startIndex: startIndex, endIndex: endIndex);
            }
        }





#if STANDALONE
        static HttpExtensionMethods()
        {
            FizzlerCustomSelectors.RegisterAll();
        }
#endif
        internal static void AbortAndDispose(this HttpResponseMessage response)
        {
            response.Dispose();
        }


        internal class GetHtmlOrJsonAsyncResponse
        {
            public HtmlNode Node;
            public Uri RedirectUrl;
            public WebCache CacheData;
            public Exception Exception;
        }

        internal static async Task<GetHtmlOrJsonAsyncResponse> GetHtmlOrJsonAsync(LazyUri url, WebRequestOptions options,
#if NET35
        IDictionary<string, string> metaParameters,
#else
        IReadOnlyDictionary<string, string> metaParameters,
#endif 
        Credentials credentialsForVary, bool needsCache)
        {

        
            var messageBox = new HttpRequestMessageBox();
            var noredir = metaParameters.TryGetValue("$noredir") == "1";
            HttpResponseInfo info = null;
            long contentLength = -1;
            try
            {


                var cacheData = needsCache ? new WebCache() : null;


                if (metaParameters.TryGetValue("$pdftables") == "1")
                {
#if STANDALONE
                    throw new NotSupportedException("PDF table extraction is not supported in standalone Shaman.Http.");
#else
                    return await GetPdfTablesAsync(url, options, cacheData);
#endif
                }

                Uri redirectLocation = null;

                var sw = Stopwatch.StartNew();
                try
                {
                    await Utils.CreateTask(async () =>
                    {

                        info = await SendAsync(url, options, messageBox, alwaysCatchAndForbidRedirects: true, keepResponseAliveOnError: true);
#if NET35
                        contentLength = info.Response != null ? info.Response.ContentLength : -1;
#else
                        contentLength = (info.Response?.Content?.Headers.ContentLength).GetValueOrDefault(-1);
#endif

                        if (info.Exception != null)
                        {
                            await CheckErrorSelectorAsync(url, info, options, metaParameters);

                            if (info.Response != null)
                            {
                                info.Response.Dispose();

#if NET35
                                var vv = info.Response.Headers["Location"];
                                redirectLocation = vv != null ? HttpUtils.GetAbsoluteUri(url.PathConsistentUrl, vv) : null;
#else
                                redirectLocation = info.Response.Headers.Location;
#endif

                                if (redirectLocation.IsAbsoluteUri && redirectLocation.Scheme == HttpUtils.UriSchemeFile)
                                {
                                    if (!redirectLocation.OriginalString.StartsWith("/")) throw new ArgumentException("Redirect URL must either be absolute, or start with '/'.");
                                    redirectLocation = new Uri(url.Scheme + "://" + url.Authority + redirectLocation.OriginalString);
                                }
                                else if (!redirectLocation.IsAbsoluteUri)
                                {
                                    redirectLocation = new Uri((url.Scheme + "://" + url.Authority).AsUri(), redirectLocation);
                                }


                                if (cacheData != null)
                                {
                                    cacheData.RedirectUrl = redirectLocation != null ? new LazyUri(redirectLocation) : null;
                                    cacheData.ErrorCode = (int)info.Response.StatusCode;
                                }
                                if (!noredir && redirectLocation == null) throw new Exception("Redirect without Location header was received.");
                            }
                            
                            
                        }
                    }).WithTimeout(TimeSpan.FromMilliseconds(options.Timeout));
                }
                catch (AggregateException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }

                using (var response = info.Response)
                {
                    if (cacheData != null) cacheData.ErrorCode = (int)response.StatusCode;
                    IEnumerable<string> cookies;
#if NET35
                    cookies = response.Headers.GetValues("Set-Cookie");
#else
                    if (response.Headers.TryGetValues("Set-Cookie", out cookies))
#endif
                    if (cookies != null)
                    {
                        foreach (var cookie in cookies)
                        {
                            var p = cookie.IndexOf(';');
                            var keyval = p != -1 ? cookie.Substring(0, p) : cookie;
                            var eq = keyval.IndexOf('=');
                            if (eq != -1)
                            {
                                var key = keyval.Substring(0, eq).Trim();
                                var val = keyval.Substring(eq + 1).Trim();
                                options.AddCookie(key, val, PriorityCookie.PRIORITY_SetCookie);
                                if (cacheData != null) cacheData.Cookies[key] = val;

                            }
                        }
                    }
                    var html = await ParseHtmlAsync(info, noredir ? null : redirectLocation, cacheData, options, metaParameters, url);
                    if (cacheData != null) cacheData.PageUrl = html?.OwnerDocument.GetLazyPageUrl() != null ? html.OwnerDocument.GetLazyPageUrl() : null;
                    return new GetHtmlOrJsonAsyncResponse()
                    {
                        CacheData = cacheData,
                        RedirectUrl = redirectLocation,
                        Node = html
                    };
                }
            }
            catch (Exception ex)
            {
                return new GetHtmlOrJsonAsyncResponse()
                {
                    CacheData = needsCache ? Caching.GetWebCacheForException(ex, info?.RespondingUrl, contentLength) : null,
                    Exception = ex
                };
            }
            finally
            {
                messageBox.Dispose();

                if (info != null && info.Response != null)
                {
                    info.Response.Dispose();
                    info.Response = null;
                }
                info = null;
            }



        }

        internal static async Task CheckErrorSelectorAsync(LazyUri url, HttpResponseInfo info, WebRequestOptions options,
#if NET35
        IDictionary<string, string> metaParameters,
#else
        IReadOnlyDictionary<string, string> metaParameters,
#endif
            bool synchronous = false
        )
        {
            var webex = info.Exception as WebException;
            if (webex == null || webex.Status != HttpUtils.Error_UnexpectedRedirect)
            {
                using (info.Response)
                {
                    var errorSelector = metaParameters.TryGetValue("$error-status-selector") ?? metaParameters.TryGetValue("$error-selector");
                    if (errorSelector != null && info.Response != null)
                    {
                        var parsed = await ParseHtmlAsync(info, null, null, options, metaParameters, url, synchronous);
                        var err = parsed.TryGetValue(errorSelector);
                        if (err != null)
                        {
                            throw new WebException("The page reports: " + err, info.Exception);
                        }
                    }
                    throw info.Exception.Rethrow();
                }

            }
        }

        public static string GetCookieValue(this System.Net.CookieCollection cookies, string name)
        {
            var c = cookies[name];
            if (c == null) return null;
            return !string.IsNullOrEmpty(c.Value) ? c.Value : null;
        }
#if !NATIVE_HTTP
        public static string GetCookieValue(this System.Net.Reimpl.CookieCollection cookies, string name)
        {
            var c = cookies[name];
            if (c == null) return null;
            return !string.IsNullOrEmpty(c.Value) ? c.Value : null;
        }
#endif

        //        internal static void AppendUriEncoded(this NakedStringBuilder sb, string text)
        //        {
        //#if DESKTOP
        //            sb.Data = DirectUriEscapeChar.EscapeString(text, 0, text.Length, sb.Data, ref sb.Length, false, DirectUriEscapeChar.c_DummyChar, DirectUriEscapeChar.c_DummyChar, DirectUriEscapeChar.c_DummyChar);
        //#else
        //            var s = Uri.EscapeDataString(text);
        //            sb.Append(text);
        //#endif
        //        }



        internal static async Task<HtmlNode> ParseHtmlAsync(HttpResponseInfo info, Uri redirectLocation, WebCache cacheData, WebRequestOptions options,
#if NET35
        IDictionary<string, string> metaParameters,
#else
        IReadOnlyDictionary<string, string> metaParameters,
#endif
        LazyUri url, bool synchronous = false)
        {

            HtmlNode html = null;


            var response = info.Response;
            if (redirectLocation == null)
            {
                Encoding initialEncoding = Encoding.UTF8;

                if (options.ResponseEncoding != null)
                {
                    initialEncoding = options.ResponseEncoding;
                }
                else
                {
                    var encoding = metaParameters.TryGetValue("$response-encoding");
                    if (encoding != null)
                    {
                        initialEncoding = Encoding.GetEncoding(encoding);
                    }
                    else
                    {
#if WEBCLIENT
                        var charset = HttpUtils.GetCharSetFromContentType(response.Headers["Content-Type"]);
#else
                        var charset = response.Content.Headers.ContentType?.CharSet;
#endif
                        if (charset != null)
                        {
                            try
                            {
                                if (charset == "utf-8" || charset.Equals("\"utf-8\"", StringComparison.OrdinalIgnoreCase)) initialEncoding = Encoding.UTF8;
                                else initialEncoding = Encoding.GetEncoding(charset);
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }

#if NET35
                var content = response;
#else
                var content = response.Content;
#endif


                var contentType = (metaParameters != null ? metaParameters.TryGetValue("$content-type") : null) ??
#if WEBCLIENT
                    HttpUtils.GetMimeFromContentType(content.Headers["Content-Type"]);
#else
                    (content.Headers.ContentType != null ? content.Headers.ContentType.MediaType : null);
#endif
                var jsonToken = metaParameters != null ? metaParameters.TryGetValue("$json-token") : null;
                var jsonWrappedHtmlSelector = metaParameters != null ? metaParameters.TryGetValue("$json-wrapped-html") : null;
                var htmlWrappedJsonSelector = metaParameters != null ? metaParameters.TryGetValue("$html-wrapped-json") : null;
                var jsonContentType = (htmlWrappedJsonSelector == null && (jsonToken != null || jsonWrappedHtmlSelector != null) ||
                    (contentType != null && (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || contentType.Contains("script", StringComparison.OrdinalIgnoreCase))));
                var looksLikeHtml = metaParameters != null && metaParameters.TryGetValue("$assume-text") == "1";
                var plainText = metaParameters != null && metaParameters.TryGetValue("$assume-text") == "1";
                if (jsonWrappedHtmlSelector != null && htmlWrappedJsonSelector != null) throw new ArgumentException("Cannot have both $json-wrapped-html and $html-wrapped-json metaparameters. Use extraction attributes on individual fields for more complex selections.");
                //var remaining = TimeSpan.FromMilliseconds(Math.Max(0, options.Timeout - sw.ElapsedMilliseconds));
                if (cacheData != null)
                    cacheData.Url = url;

                if (contentType != null && contentType.StartsWith("image/")) throw new NotSupportedResponseException(contentType, url);

                var readfunc = new Func<Task>(
#if !NET35
                    async
#endif
                     () =>
                {
                    LazyTextReader lazy = null;
                    try
                    {
#if WEBCLIENT
                        var stream = content.GetResponseStream();
#else
                        Stream stream;
                        if (synchronous)
                        {
                            content.LoadIntoBufferAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                            stream = content.ReadAsStreamAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                        }
                        else
                        {
                            await content.LoadIntoBufferAsync().ConfigureAwait(false);
                            stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
                        }

#endif
                        lazy = new LazyTextReader(stream, initialEncoding);
                        if (!plainText && lazy.ContainsIndex(3) && lazy[0] == '%' && lazy[1] == 'P' && lazy[2] == 'D' && lazy[3] == 'F')
                            throw new NotSupportedResponseException("application/pdf", url);
                        if (!looksLikeHtml && !plainText)
                        {
                            looksLikeHtml = HttpUtils.LooksLikeHtml(lazy);
                        }

                        string allText = null;

                        if (!looksLikeHtml && !plainText && htmlWrappedJsonSelector == null)
                        {
                            try
                            {
                                string json;
                                if (allText == null)
                                {
                                    lazy.ReadToEnd();
                                    allText = lazy.ToString();
                                }
                                if (jsonToken != null)
                                {
                                    json = allText;
                                    var start = json.IndexOf(jsonToken);
                                    if (start == -1) throw new ExtractionException(sourceData: json, beginString: jsonToken);
                                    start += jsonToken.Length;
                                    json = json.Substring(start);
                                }
                                else
                                {
                                    json = CleanupJsonp(allText);
                                }




                                html = FizzlerCustomSelectors.JsonToHtml(json, 0, null);
                                html.OwnerDocument.SetPageUrl(url);



                                if (jsonWrappedHtmlSelector != null)
                                {
                                    var doc = FizzlerCustomSelectors.CreateDocument(html.OwnerDocument);
                                    var t = html.FindAll(jsonWrappedHtmlSelector);
                                    var gt = t.GetText() ?? string.Empty;
                                    html = FizzlerCustomSelectors.ReparseHtml(doc, gt, html.OwnerDocument);

                                    if (cacheData != null)
                                    {
                                        cacheData.DataType = WebCacheDataType.Html;
                                        cacheData.Result = gt;
                                    }

                                }
                                else
                                {
                                    if (cacheData != null)
                                    {
                                        cacheData.DataType = WebCacheDataType.Json;
                                        cacheData.Result = json;
                                    }
                                }




                            }
                            catch when (!jsonContentType)
                            {
                                if (!looksLikeHtml) throw new NotSupportedResponseException(contentType, url);
                            }
                        }

                        if (cacheData != null)
                            cacheData.Url = info.RespondingUrl ?? url;

                        if (html == null)
                        {
                            var d = new HtmlDocument();
                            if (plainText)
                            {
                                if (allText == null)
                                {
                                    lazy.ReadToEnd();
                                    allText = lazy.ToString();
                                }


                                if (cacheData != null)
                                {
                                    cacheData.Result = allText;
                                    cacheData.DataType = WebCacheDataType.Text;
                                }
                                html = d.DocumentNode;
                                html.AppendTextNode(allText);
                                html.SetAttributeValue("plain-text", "1");
                            }
                            else
                            {
                                if (allText != null) d.LoadHtml(allText);
                                else d.Load(lazy);
                                lazy.ReadToEnd();

                                if (cacheData != null)
                                {
                                    cacheData.Result = allText ?? lazy.ToString();
                                    cacheData.DataType = WebCacheDataType.Html;
                                }
                                html = d.DocumentNode;


                                if (htmlWrappedJsonSelector != null)
                                {
                                    var script = d.DocumentNode.FindSingle(htmlWrappedJsonSelector);
                                    if (script == null) throw new ExtractionException(node: d.DocumentNode, nodeQuery: htmlWrappedJsonSelector, message: "No matching nodes for $html-wrapped-json metaparameter.");
                                    var text = script.InnerText;
                                    string json;
                                    if (jsonToken != null)
                                    {
                                        json = text;
                                        var start = json.IndexOf(jsonToken);
                                        if (start == -1) throw new ExtractionException(sourceData: json, beginString: jsonToken);
                                        start += jsonToken.Length;
                                        json = json.Substring(start);
                                    }
                                    else
                                    {
                                        json = CleanupJsonp(allText);
                                    }

                                    if (cacheData != null)
                                    {
                                        cacheData.Result = json;
                                        cacheData.DataType = WebCacheDataType.Json;
                                    }

                                    html = FizzlerCustomSelectors.JsonToHtml(json, 0, null);
                                    html.OwnerDocument.SetPageUrl(url);


                                }
                            }

                        }






                        var docnode = html.OwnerDocument.DocumentNode;
                        var dateRetrieved = DateTime.UtcNow;
                        docnode.SetAttributeValue("date-retrieved", dateRetrieved.ToString("o"));
                        html.OwnerDocument.SetPageUrl((info != null ? info.RespondingUrl : null) ?? url);

#if WEBCLIENT
                        foreach(string key in response.Headers.Keys)
                        {    
                            var val = response.Headers.GetValues(key).FirstOrDefault();
                            
#else
                        foreach (var header in response.Headers.Union(response.Content.Headers))
                        {
                            var key = header.Key;
                            var val = header.Value.FirstOrDefault();
                            
#endif
                            
                            if (key == "Set-Cookie") continue;

                            if (val != null)
                            {
                                docnode.SetAttributeValue("header-" + key, val);

                                if (cacheData != null) cacheData.Headers[key] = val;

                            }
                        
                        }

                        if (cacheData != null) cacheData.DateRetrieved = dateRetrieved;

                    }
                    finally
                    {
                        if (lazy != null) lazy.Dispose();
                        lazy = null;
                    }
                });

                if (synchronous) readfunc().AssumeCompleted();
                else await TaskEx.Run(readfunc);

                // if (redirectLocation == null) html = (await response.Content.ReadAsStringAsync()).AsHtmlDocumentNode();

            }
            return html;
        }

        internal class HttpRequestMessageBox : IDisposable
        {
            public HttpRequestMessage Message;
            public HttpRequestMessage PrebuiltRequest;
            public HttpResponseMessage PrebuiltResponse;

            public void Dispose()
            {
                if (Message != null)
                {
#if !WEBCLIENT
                    Message.Dispose();
#endif
                    Message = null;
                }
            }
        }


#if STANDALONE

        public static Uri AsUri(this string url)
        {
            return new Uri(url);
        }
        public static Uri AsUri(this string url, bool allowNull)
        {
            return url != null ? new Uri(url) : null;
        }

#endif

        public static LazyUri AsLazyUri(this string url)
        {
            return new LazyUri(url);
        }
        private async static Task<HtmlNode> GetHtmlNodeAsyncImpl2(this LazyUri lazyurl, WebRequestOptions preprocessedOptions,
#if NET35
        IDictionary<string, string> metaParameters,
#else
         IReadOnlyDictionary<string, string> metaParameters,
#endif
          bool hasExtraOptions, Credentials credentialsForVary, Action<HtmlNode> additionalChecks)
        {
            var cachePath = Caching.GetWebCachePath(HttpUtils.GetVaryUrl(lazyurl, metaParameters, credentialsForVary), hasExtraOptions, true);
            var originalLazy = lazyurl;
#if !STANDALONE
            if (lazyurl.IsHostedOn("proxy.shaman.io"))
            {
                if (lazyurl.Host == "bingcache.proxy.shaman.io")
                {
                    var original = lazyurl.PathAndQuery.Substring(1).AsUri();
                    var results = await ObjectManager.GetEntities<Shaman.Connectors.Bing.WebResult>().RemoteSearch("\"" + original.ToString() + "\"").GetFirstPageAsync();
                    if (results != null)
                    {
                        var p = results.FirstOrDefault();
                        if (p.Url == original && p.CacheUrl != null)
                        {
                            return await GetHtmlNodeAsync(p.CacheUrl);
                        }
                    }
                }
                throw new Exception("Unsupported shaman proxy.");
            }
#endif

            if (metaParameters != null && metaParameters.TryGetValue("$js") == "1")
            {
                var fragment = lazyurl.Fragment;
                var firstMeta = fragment.IndexOf("&$");

                var fragmentPos = lazyurl.AbsoluteUri.IndexOf("#");

                lazyurl = new LazyUri(lazyurl.AbsoluteUri.Substring(0, fragmentPos + (firstMeta != -1 ? firstMeta : 0)));

                HtmlNode node = null;

                await Utils.CheckLocalFileAccessAsync(lazyurl);
#if DESKTOP
                if (cachePath != null)
                {
                    var data = Caching.TryReadCacheFile(cachePath);
                    if (data != null && (data.ExceptionType == null || !Caching.IgnoreCachedFailedRequests))
                    {
                        Utils.RaiseWebRequestEvent(lazyurl, true);
                        if (data.ExceptionType != null)
                        {
                            var ex = Caching.RebuildException(data, lazyurl); 
                            preprocessedOptions.HtmlRetrieved?.Invoke(null, (data.PageUrl ?? data.RedirectUrl ?? data.Url).Url, ex);
                            ex.Rethrow();
                        }
                        var jsexecResults = data.JsExecutionResults != null ? JsonConvert.DeserializeObject<PageExecutionResults>(data.JsExecutionResults) : null;
                        node = data.RecreateNode(lazyurl, preprocessedOptions, cachePath);
                        node.OwnerDocument.Tag = jsexecResults;
                    }
                }
#endif
                if (node == null)
                {
#if DESKTOP
                    try
                    {
                        var r = await HttpUtils.GetJavascriptProcessedPageAsync(originalLazy, lazyurl, metaParameters);
                        if (preprocessedOptions != null) preprocessedOptions.PageExecutionResults = r;
                        node = r.GetHtmlNode();
                        EnsurePageConstraints(node, metaParameters);
                        additionalChecks?.Invoke(node);
                        if (cachePath != null)
                        {
                            Caching.SaveCache(cachePath, new WebCache()
                            {
                                Url = lazyurl,
                                PageUrl = node.OwnerDocument.GetLazyPageUrl(),
                                Result = node.OwnerDocument.WriteTo(),
                                JsExecutionResults = JsonConvert.SerializeObject(r, Formatting.None)
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        if (cachePath != null)
                        {
                            var t = Caching.GetWebCacheForException(ex, lazyurl, -1);
                            preprocessedOptions.HtmlRetrieved?.Invoke(null, lazyurl.Url, ex);
                            Caching.SaveCache(cachePath, t);
                        }
                        throw;
                    }
#else
                    throw new NotSupportedException("JavaScript preprocessing not available on this platform.");
#endif
                }

                MaybeKeepReturnedPage(lazyurl, node);
                node.OwnerDocument.DocumentNode.SetAttributeValue("requested-url", originalLazy.AbsoluteUri);
                return node;
            }
            else
            {

#if DESKTOP
                var p = Caching.TryReadFromCache(cachePath, lazyurl, preprocessedOptions);
                if (p != null)
                {
                    await TaskEx.Yield();
                    return p;
                }
#endif
                var numRedirects = 0;
                var hasProcessedFormButton = false;
                lazyurl = lazyurl.Clone();
                while (true)
                {
                    var result = await GetHtmlOrJsonAsync(lazyurl, preprocessedOptions, metaParameters, credentialsForVary, cachePath != null || Caching.IsWebCacheEnabled());
                    if (result.Exception != null)
                    {
#if DESKTOP
                        Caching.SaveCache(cachePath, result.CacheData);
#endif
                        preprocessedOptions.HtmlRetrieved?.Invoke(null, lazyurl.Url, result.Exception);
                        throw result.Exception.Rethrow();
                    }
                    var page = result.Node;
                    var redirectLocation = result.RedirectUrl;

                    try
                    {
                        if (page != null)
                        {

                            if (!hasProcessedFormButton)
                            {
                                var formButtonName = metaParameters.TryGetValue("$formbutton");
                                var formButtonSelector = metaParameters.TryGetValue("$formbuttonsel");
                                if (formButtonSelector != null || formButtonName != null)
                                {
                                    HtmlNode button;
                                    if (formButtonSelector != null)
                                    {
                                        throw new ArgumentException("$formbuttonsel is only allowed when $js is set to 1. Use $formbutton instead (with the name of the button instead of its selector)");
                                    }
                                    else if (formButtonName != null)
                                    {
                                        button = page.DescendantsAndSelf().FirstOrDefault(x => x.Id == formButtonName || x.GetAttributeValue("name") == formButtonName);
                                        if (button == null) throw new ExtractionException(message: "No element has the name or ID specified by the $formbutton metaparameter.");
                                    }
                                    else
                                    {
                                        throw Sanity.ShouldntHaveHappened();
                                    }

                                    if (metaParameters.Any(x => x.Key.StartsWith("$formsel-"))) throw new ArgumentException("$formsel-* metaparameters are only allowed when $js is set to 1. Use $form-* instead (with the name of the field instead of its selector)");



                                    var parameters = metaParameters.Where(x => x.Key.StartsWith("$form-")).Select(x => new KeyValuePair<string, string>(x.Key.Substring(6), x.Value)).ToList();
                                    var tuple = HttpUtils.SetUpOptionsFromFormButton(button, preprocessedOptions, parameters);
                                    lazyurl = tuple.Url;
                                    // TODO tuple.Item2 is ignored
                                    var preserve = metaParameters.Where(x => x.Key == "$forbid-selector" || x.Key == "$assert-selector" || x.Key == "$error-selector" || x.Key == "$error-status-selector").ToList();
                                    metaParameters = ProcessMetaParameters(lazyurl, preprocessedOptions);
                                    //Console.WriteLine(lazyurl);

                                    if (preserve.Any())
                                    {
                                        var m = metaParameters.ToDictionary(x => x.Key, x => x.Value);
                                        foreach (var item in preserve)
                                        {
                                            m[item.Key] = item.Value;
                                        }
                                        metaParameters = m;
                                    }
                                    continue;
                                }

                            }

                            EnsurePageConstraints(page, metaParameters);
                            additionalChecks?.Invoke(page);
                            MaybeKeepReturnedPage(lazyurl, page);
                        }

                        if (redirectLocation == null)
                        {
                            //var html = page.ChildNodes.FirstOrDefault(x => x.Name == "html");
                            //var head = (html ?? page).ChildNodes.FirstOrDefault(x => x.Name == "head");
                            var follownoscript = metaParameters.TryGetValue("follownoscript") == "1";
                            var metaRedirect = page.Descendants().FirstOrDefault(x => x.TagName == "meta" && string.Equals(x.GetAttributeValue("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase) && (follownoscript || !x.Ancestors().Any(y => y.TagName == "noscript")));
                            if (metaRedirect != null)
                            {
                                var value = metaRedirect.GetAttributeValue("content");
                                if (value != null)
                                {
                                    var urlString = value.TryCapture(@"(?:url|URL|Url)\s*=\s*[\'""]?(.+?)[\'""]?\s*\;?\s*$");
                                    if (urlString != null)
                                    {
                                        var time = value.TryCapture(@"^\s*(\d+)[\s\,\;]");
                                        if (time == null || int.Parse(time) <= 10)
                                            redirectLocation = new Uri(lazyurl.PathConsistentUrl, urlString);
                                    }
                                }
                            }
                        }

                        if (redirectLocation != null)
                        {
                            if (metaParameters.TryGetValue("$noredir") == "1")
                            {
                                page.OwnerDocument.DocumentNode.SetAttributeValue("redirect-url", redirectLocation.AbsoluteUri);
                            }
                            else
                            {
                                if (!preprocessedOptions.AllowRedirects)
                                    throw new WebException("An unexpected redirect was received from the server.", HttpUtils.Error_UnexpectedRedirect);
                                numRedirects++;
                                if (numRedirects >= 5) throw new WebException("The maximum number of http-equiv redirects has been reached.", HttpUtils.Error_MaximumNumberOfRedirectsExceeded);

                                preprocessedOptions.PostData = null;
                                preprocessedOptions.PostString = null;
                                lazyurl = new LazyUri(redirectLocation);
                                continue;
                            }
                        }

                        page.OwnerDocument.DocumentNode.SetAttributeValue("requested-url", originalLazy.AbsoluteUri);
                    }
                    catch (Exception ex) when (preprocessedOptions.HtmlRetrieved != null)
                    {
                        preprocessedOptions.HtmlRetrieved(null, lazyurl.Url, ex);
                        throw;
                    }
#if DESKTOP
                    Caching.SaveCache(cachePath, result.CacheData);
#endif

                    return page;
                }
            }

        }


        public static string GetQueryParameter(this Uri url, string name)
        {
            return HttpUtils.GetParameters(url.Query).FirstOrDefault(x => x.Key == name).Value;
        }
        public static string GetFragmentParameter(this Uri url, string name)
        {
            return HttpUtils.GetParameters(url.Fragment).FirstOrDefault(x => x.Key == name).Value;
        }

        public static IEnumerable<KeyValuePair<string, string>> GetQueryParameters(this Uri url)
        {
            return HttpUtils.GetParameters(url.Query);
        }

        public static IEnumerable<KeyValuePair<string, string>> GetFragmentParameters(this Uri url)
        {
            return HttpUtils.GetParameters(url.Fragment);
        }



#if STANDALONE
        internal static void MaybeKeepReturnedPage(LazyUri url, HtmlNode node)
        {

        }
#endif
        private async static Task<HtmlNode> GetHtmlNodeAsyncImpl(this LazyUri url, WebRequestOptions preprocessedOptions,
#if NET35
        IDictionary<string, string> metaParameters,
#else
        IReadOnlyDictionary<string, string> metaParameters,
#endif
        bool hasExtraOptions, 
        Credentials credentialsForVary,
        Action<HtmlNode> additionalChecks
        )
        {
#if !STANDALONE
            using (var timing = Timing.Create(Timing.TimingCategory.Http, url))
#endif
            {

                var t = 10;
                while (true)
                {
                    try
                    {
                        var p = await GetHtmlNodeAsyncImpl2(url, preprocessedOptions, metaParameters, hasExtraOptions, credentialsForVary, additionalChecks);
                        foreach (var cookie in preprocessedOptions.CookiesList)
                        {
                            p.OwnerDocument.DocumentNode.SetAttributeValue("cookie-" + cookie.Name, cookie.Value);
                        }
                        if (p.OwnerDocument.DocumentNode.GetAttributeValue("from-cache") != "1")
                        {
#if !STANDALONE
                            if (SuccessfulWebRequestCounter != null)
                            {
                                SuccessfulWebRequestCounter.OnEvent();
                            }
#endif
                        }
#if !STANDALONE
                        timing.Complete();
#endif
                        preprocessedOptions.HtmlRetrieved?.Invoke(p, null, null);
                        return p;
                    }
                    catch (Exception) when (KeepRetryingFailedRequests)
                    {
#if DESKTOP
                        var c = Caching.GetWebCachePath(url, hasExtraOptions, false);
                        if (c != null)
                        {
                            File.Delete(c);
                        }
#endif
                    }
                    t *= 2;
                    await TaskEx.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }

        public static bool KeepRetryingFailedRequests {[RestrictedAccess] get;[RestrictedAccess] set; }

#if STANDALONE
        public static Task<HtmlNode> GetHtmlNodeAsync(this Uri url, WebRequestOptions options = null, Runtime.CookieContainer cookieContainer = null)
#else
        public static Task<HtmlNode> GetHtmlNodeAsync(this Uri url, WebRequestOptions options, Runtime.CookieContainer cookieContainer)        
#endif
        {
            return GetHtmlNodeAsync(new LazyUri(url), options, cookieContainer);
        }

#if STANDALONE
        public static async Task<HtmlNode> GetHtmlNodeAsync(this LazyUri url, WebRequestOptions options = null, Runtime.CookieContainer cookieContainer = null)
#else
        public static async Task<HtmlNode> GetHtmlNodeAsync(this LazyUri url, WebRequestOptions options, Runtime.CookieContainer cookieContainer)
#endif
        {
            var hasExtraOptions = options != null && !options.AllowCachingEvenWithCustomRequestOptions;
            if (options == null) options = new WebRequestOptions();

            var metaParameters = ProcessMetaParameters(url, options) ?? new Dictionary<string, string>();

#if !STANDALONE
            string siteIdentifier = null;
#endif

            Credentials credentials = null;

            var isolated = cookieContainer as IsolatedCookieContainer;
            if (isolated != null)
            {
                foreach (var c in isolated._cookies)
                {
                    options.AddCookie(c.Key, c.Value, PriorityCookie.PRIORITY_IsolatedCookieContainerInitial);
                }
                if (isolated.CacheVaryKey != null) url.AppendFragmentParameter("$varyisolatedcookies", isolated.CacheVaryKey);
                else hasExtraOptions = true;
                var page = await GetHtmlNodeAsyncImpl(url, options, metaParameters, hasExtraOptions, credentials, null);
                isolated._cookies = options.CookiesList.ToDictionary(x => x.Name, x => x.Value);
                isolated.MaybeSave();
                return page;
            }



            // these fields will be nulled by redirects
            var bakPostData = options.PostData;
            var bakPostString = options.PostString;

#if !STANDALONE
            var siteInfo = cookieContainer as SiteInfo;
            if (siteInfo != null)
            {
                siteIdentifier = siteInfo.SiteIdentifier;
                credentials = await ObjectManager.GetCredentialsAsync(siteIdentifier);

                if (credentials.LastCookies != null)
                {
                    foreach (var c in Utils.GetParameters(credentials.LastCookies))
                    {
                        options.AddCookie(c.Key, c.Value, PriorityCookie.PRIORITY_SiteInfoInitial);
                    }
                }


                try
                {
                    var oldcookies = options.CookiesList.ToDictionary(x => x.Name, x => x.Value);
                    var page = await GetHtmlNodeAsyncImpl(url, options, metaParameters, hasExtraOptions, credentials, p => VerifyAuthentication(p, siteInfo, url, siteIdentifier));


                    credentials.LastCookies = Utils.ParametersToString(options.CookiesList.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)));
                    var newcookies = options.CookiesList.ToDictionary(x => x.Name, x => x.Value);
                    if (siteInfo.HasSessionIdChanged(oldcookies, newcookies))
                    {

                        var task = siteInfo.OnSessionCreatedAsync(newcookies);
                        if (task != null) await task;
                        credentials.LastLoginDate = DateTime.UtcNow;
                        ObjectManager.SaveCredentials(credentials);
                    }


                    return page;

                }
                catch (WebException ex)
                {
                    var s = ex.GetResponseStatusCode();
                    Caching.SetDummyResponseWithUrl(ex, url, s);
                    if (s != HttpStatusCode.Forbidden && s != HttpStatusCode.Unauthorized && s != (HttpStatusCode)0) throw;
                }
                catch (WebsiteAuthenticationException)
                {
                }







                var username = credentials.UserName;
                if (username == null && siteInfo.RequiresCredentials) throw new WebsiteAuthenticationException("No username has been provided.") { SiteInfo = siteInfo, SiteIdentifier = siteIdentifier };
                var password = credentials.Password;
                var returnedCookies = await siteInfo.LoginAsync(url.Url, username, password);


                foreach (var item in returnedCookies)
                {
                    options.CookiesList?.RemoveWhere(x => x.Name == item.Key);
                    options.AddCookie(item.Key, item.Value, PriorityCookie.PRIORITY_Login);
                }


                credentials.LastCookies = Utils.ParametersToString(options.CookiesList.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)));
                credentials.LastLoginDate = DateTime.UtcNow;

                ObjectManager.SaveCredentials(credentials);




                var task2 = siteInfo.OnSessionCreatedAsync(options.CookiesList.ToDictionary(x => x.Name, x => x.Value));
                if (task2 != null) await task2;

#if DESKTOP
                var path = Caching.GetWebCachePath(HttpUtils.GetVaryUrl(url, metaParameters, credentials), hasExtraOptions, false);
                if (path != null) File.Delete(path);
#endif

            }

#endif
            {
                options.PostData = bakPostData;
                options.PostString = bakPostString;
                try
                {
                    var page = await GetHtmlNodeAsyncImpl(url, options, metaParameters, hasExtraOptions, credentials,
#if STANDALONE
 null

#else
 p => VerifyAuthentication(p, siteInfo, url, siteIdentifier)
#endif
                        );
                    return page;
                }
                catch (WebException ex)
                {
                    var s = ex.GetResponseStatusCode();
                    Caching.SetDummyResponseWithUrl(ex, url, s);
#if STANDALONE
                    throw;
#else
                    if (s == HttpStatusCode.Forbidden || s == HttpStatusCode.Unauthorized)
                    {
                        var wea = new WebsiteAuthenticationException("The server returned: " + s);
                        wea.RequestedUrl = url;
                        wea.SiteIdentifier = siteIdentifier;
                        wea.SiteInfo = siteInfo;
                        throw wea;
                    }
                    else throw;
#endif
                }
            }
        }


        public static async Task<HttpResponseMessage> GetResponseAsync(this LazyUri url, WebRequestOptions options = null)
        {
            if (options == null) options = new WebRequestOptions();
            var metaparameters = ProcessMetaParameters(url, options);
            var r = await url.SendAsync(options, null);
            if (r.Exception != null) throw r.Exception;
#if NET35
            if( r.Response.StatusCode < (HttpStatusCode)200 || r.Response.StatusCode > (HttpStatusCode)299) {
                throw new WebException("The server returned: " + r.Response.StatusCode, null, WebExceptionStatus.ProtocolError, r.Response);
            }
#else
            r.Response.EnsureSuccessStatusCode();
#endif


#if !NET35
            if (metaparameters.TryGetValue("$forbid-html") == "1")
            {
                var contentType = r.Response.Content.Headers.ContentType;
                if (contentType != null && contentType.MediaType != null)
                {
                    var t = contentType.MediaType;
                    if (t.Contains("/html") || t.Contains("/xhtml"))
                    {
                        using (var reader = new LazyTextReader(await r.Response.Content.ReadAsStreamAsync(), Encoding.UTF8))
                        {
                            var doc = new HtmlDocument();
                            doc.Load(reader);
                            doc.SetPageUrl(r.RespondingUrl);
                            throw new NotSupportedResponseException(contentType.ToString(), r.RespondingUrl)
                            {
                                Page = doc.DocumentNode
                            };
                        }

                        
                    }
                }
            }
#endif

#if NET35
            var length = r.Response.ContentLength;
#else
            var length = r.Response.Content.Headers.ContentLength.GetValueOrDefault(-1);
#endif
            if (length != -1)
            {
                var forbiddenSizes = metaparameters.TryGetValue("$forbid-size");
                CheckForbiddenSizeRanges(length, forbiddenSizes);
            }
            if (r.RespondingUrl != null)
            {
                var forbiddenRedirect = metaparameters.TryGetValue("$forbid-redirect-match");
                if (forbiddenRedirect != null)
                {
                    if (Regex.IsMatch(r.RespondingUrl.AbsoluteUri, forbiddenRedirect))
                    {
                        throw new WebException("Unexpected responding URL.", HttpUtils.Error_ForbiddenRedirectMatch); 
                    }
                }
            }
            return r.Response;
        }

        public static void CheckForbiddenSizeRanges(long actualLength, string forbiddenSizes)
        {
            if (actualLength != -1)
            {
                
                if (forbiddenSizes != null)
                {
                    foreach (var item in forbiddenSizes.SplitFast(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var interval = item.SplitFast('-', StringSplitOptions.None);
                        if (interval.Length == 1)
                        {
                            if (actualLength != long.Parse(interval[0])) continue;
                        }
                        else if (interval.Length == 2)
                        {
                            if (interval[0].Length != 0 && actualLength < long.Parse(interval[0])) continue;
                            if (interval[1].Length != 0 && actualLength > long.Parse(interval[1])) continue;
                        }
                        throw new WebException("Invalid file length.", HttpUtils.Error_SizeOutsideAcceptableRange);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the HTTP status code from a <see cref="WebException"/>.
        /// </summary>
        /// <param name="ex"></param>
        /// <returns></returns>
        [AllowNumericLiterals]
        public static HttpStatusCode GetResponseStatusCode(this WebException ex)
        {
            var response = ex.Response as HttpWebResponse;
            if (response != null) return response.StatusCode;

            var converted = (int)ex.Status;
            if (converted >= 100 && converted <= 999) return (HttpStatusCode)converted;

            return default(HttpStatusCode);
        }


#endif


            public static bool IsHostedOn(this Uri url, string baseHost)
        {

            return IsHostedOn(url.Host, baseHost);
        }

        public static bool IsHostedOnAndPathStartsWith(this Uri url, string baseHost, string pathPrefix)
        {
            return IsHostedOn(url.Host, baseHost) && url.AbsolutePath.StartsWith(pathPrefix);
        }
#if !SALTARELLE
        public static bool IsHostedOn(this LazyUri url, string baseHost)
        {
            return IsHostedOn(url.Host, baseHost);
        }

#endif
        private static bool IsHostedOn(this string fullHost, string baseHost)
        {

            if (baseHost.Length > fullHost.Length) return false;
            if (baseHost.Length == fullHost.Length)
                return string.Equals(baseHost, fullHost,
#if SALTARELLE
 true);
#else
 StringComparison.OrdinalIgnoreCase);
#endif

            var k = fullHost[fullHost.Length - baseHost.Length - 1];

            if (k == '.')
                return
#if SALTARELLE
 fullHost.ToLower().EndsWith(baseHost.ToLower());
#else
 fullHost.EndsWith(baseHost, StringComparison.OrdinalIgnoreCase);
#endif
            else return false;

        }
                public static bool IsPlainText(this HtmlDocument document)
        {
            return document.DocumentNode.GetAttributeValue("plain-text") == "1";
        }
#if !SALTARELLE
        /// <summary>
        /// Returns the value of the specified query attribute.
        /// </summary>
        /// <param name="url">The url.</param>
        /// <param name="name">The key of the attribute.</param>
        /// <returns>The value of the attribute, or null if it is not found.</returns>
        /// <example>new Uri("http://example.com/index?page=3&amp;view=summary").GetParameter("page");</example>
        public static string GetParameter(this Uri url, string name)
        {
            var attr = url.GetParametersEnumerable().FirstOrDefault(x => x.Key == name);
            // It's a struct - won't be null
            return attr.Value;
        }
        public static Uri GetLeftPartPathUri(this Uri url, int count)
        {
            var z = GetLeftPartPath(url, count);
            return (url.GetLeftPart(UriPartial.Authority) + z).AsUri();
        }

        public static string GetLeftPartPath(this Uri url, int count)
        {
            var s = url.AbsolutePath.AsValueString().Split('/');
            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            for (int i = 0; i < count + 1; i++)
            {
                if (i != 0) sb.Append('/');
                sb.AppendValueString(s[i]);
            }
            return ReseekableStringBuilder.GetValueAndRelease(sb);
        }
        public static string GetPathComponent(this Uri url, int index)
        {
            var s = url.AbsolutePath.AsValueString().Split('/');
            if (s.Length <= index + 1) return null;
            return s[index + 1].ToClrString();
        }


        public static Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            return (Task<T>)WithTimeoutInternal<T>(task, timeout, true);
        }


        public static Task WithTimeout(this Task task, TimeSpan timeout)
        {
            return WithTimeoutInternal<bool>(task, timeout, false);
        }

        public static CancellableTimout CancelAfter<T>(this TaskCompletionSource<T> tcs, TimeSpan timeout)
        {
            var t = CancellableTimout.ScheduleUnsafe(() =>
            {
                tcs.TrySetException(new TimeoutException());
            }, timeout);
            tcs.Task.ContinueWith((s) =>
            {
                t.Dispose();
            });
            return t;
        }

        private static Task WithTimeoutInternal<T>(Task task, TimeSpan timeout, bool hasResult)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.CancelAfter(timeout);

            task.ContinueWith(originalTask =>
            {
                if (originalTask.IsFaulted) tcs.TrySetException(originalTask.Exception);
                else if (originalTask.IsCanceled) tcs.TrySetCanceled();
                else
                {
                    if (hasResult) tcs.TrySetResult(((Task<T>)task).Result);
                    else tcs.TrySetResult(default(T));
                }
            });

            return tcs.Task;
        }


        public static string GetLeftPart_UriPartial_Query(this Uri url)
        {
            if (url.Fragment.Length == 0) return url.AbsoluteUri;
            var b = url.GetLeftPart(UriPartial.Path);
            if (b == null) return null;
            return b + url.Query;
        }

        /// <summary>
        /// Returns the query attributes.
        /// </summary>
        public static IDictionary<string, string> GetParameters(this Uri url)
        {
            var dict = new Dictionary<string, string>();
            foreach (var item in url.GetParametersEnumerable())
            {
                dict[item.Key] = item.Value;
            }
            return dict;
        }

        /// <summary>
        /// Returns the query attributes.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> GetParametersEnumerable(this Uri url)
        {
            if (url == null) throw new ArgumentNullException();
            var query = url.Query;
            if (string.IsNullOrEmpty(query)) return Enumerable.Empty<KeyValuePair<string, string>>();
            return HttpUtils.GetParameters(query);
        }
#endif



       

        public static HtmlNode NextSibling(this HtmlNode node, string nodeName)
        {

            while (true)
            {
                var sib = node.NextSibling;
                if (sib == null) return null;
                if (sib.NodeType == HtmlNodeType.Element && sib.TagName == nodeName) return sib;
                node = sib;
            }

        }


        public static bool IsHeading(this Shaman.Dom.HtmlNode node)
        {
            if (node == null) throw new ArgumentNullException();
            var name = node.TagName;
            if (name.Length != 2) return false;
            if (name[0] != 'h') return false;
#if SALTARELLE
            if (!AwdeeUtils.IsDigit(name[1])) return false;
#else
            if (!char.IsDigit(name[1])) return false;
#endif
            return true;
        }

        public static string GetIntuitiveXPath(this HtmlNode node)
        {
            if (node.TagName == "#document") return "(document)";

            var parents = new List<HtmlNode>();
            var p = node;
            while (p.ParentNode != null && p.ParentNode.NodeType != HtmlNodeType.Document)
            {
                parents.Add(p);
                p = p.ParentNode;
            }
            var first = true;
            parents.Reverse();
            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            foreach (var item in parents)
            {
                if (!first)
                    sb.Append(" > ");

                first = false;
                var id = item.Id;

                sb.Append(item.TagName);

                if (!string.IsNullOrEmpty(id))
                {
                    sb.Append('#');
                    sb.Append(id);
                }
                else
                {
                    foreach (var name in item.ClassList.Take(5))
                    {
                        sb.Append('.');
                        sb.Append(name.TrimSize(10, 13, true));
                    }
                }

            }
            return ReseekableStringBuilder.GetValueAndRelease(sb);

        }

        public static HtmlNode AsHtmlDocumentNode(this string html)
        {
            var d = new HtmlDocument();
            d.LoadHtml(html);
            return d.DocumentNode;
        }

        public static HtmlNode AsHtmlNode(this string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            if (doc.DocumentNode.ChildNodes.Count == 1) return doc.DocumentNode.FirstChild;
            return doc.DocumentNode;
        }

        public static Uri TryGetImageUrl(this HtmlNode finalNode)
        {
            Uri url = null;
            foreach (var attr in finalNode.Attributes)
            {
                var v = TryGetImageUrl(finalNode, attr);
                if (v != null)
                {
                    url = v;
                    if (attr.Name != "src") return url;
                }
            }
            return url;
        }

        internal static Uri TryGetImageUrl(HtmlNode node, HtmlAttribute attr)
        {
            var name = attr.Name;
            if (name != "srcset" && (name.Contains("src") || name.Contains("lazy") || name.Contains("img") || name.Contains("original") || name.StartsWith("data-")))
            {
                var str = attr.Value;
                if (string.IsNullOrEmpty(str)) return null;

                if (name != "src")
                {
                    if (str[0] == '{' || str[0] == '[' || str[0] == '<') return null;
                }
                Uri u = null;
                if (name.StartsWith("data-") && !(str.StartsWith("http://") || str.StartsWith("https://") || str.StartsWith("/"))) return null;
                try
                {
                    u = HttpUtils.GetAbsoluteUri(node.OwnerDocument.GetLazyBaseUrl(), str);
                    if (name != "src")
                    {
                        var path = u.AbsolutePath.ToLowerFast();
                        if (!(path.EndsWith(".jpg") ||
                            path.EndsWith(".jpeg") ||
                            path.EndsWith(".png")) ||
                            path.EndsWith(".gif"))
                            return null;
                    }
                }
                catch
                {
                    return null;
                }
                if (u != null)
                {
                    if (u.Scheme != HttpUtils.UriSchemeHttp && u.Scheme != HttpUtils.UriSchemeHttps) u = null;
                    else
                    {
                        return u;
                    }
                }


            }
            return null;
        }

        public static void WriteHtmlEncoded(this TextWriter sb, string text, bool newLinesToBr = false, int startIndex = 0, int endIndex = -1)
        {
            if (text == null) return;
            if (endIndex == -1) endIndex = text.Length;
            if (startIndex == endIndex)
            {
                return;
            }

            for (int i = startIndex; i < endIndex; i++)
            {
                char c = text[i];

                if (c == '&')
                {
                    sb.Write("&amp;");
                }
                else if (c == '<')
                {
                    sb.Write("&lt;");
                }
                else if (c == '>')
                {
                    sb.Write("&gt;");
                }
                else if (c == '"')
                {
                    sb.Write("&quot;");
                }
                else if (c == '\'')
                {
                    sb.Write("&apos;");
                }
                else if (c == '\n' && newLinesToBr)
                {
                    sb.Write("<br>\n");
                }
                else
                {
                    sb.Write(c);
                }
            }
        }


        public static void MakeAbsoluteUrls(this HtmlNode node)
        {
            MakeAbsoluteUrlsInternal(node, node.OwnerDocument.GetLazyBaseUrl());
        }




        internal static void MakeAbsoluteUrlsInternal(this HtmlNode node, LazyUri baseUrl)
        {
            try
            {
                HttpUtils.MakeAbsoluteAttribute(node, "href", baseUrl);
                HttpUtils.MakeAbsoluteAttribute(node, "src", baseUrl);
                HttpUtils.MakeAbsoluteAttribute(node, "action", baseUrl);
                HttpUtils.MakeAbsoluteAttribute(node, "poster", baseUrl);
            }
            catch
            {
#if !SMALL_LIB_AWDEE
                LogNonCriticalException(ex, baseUrl, node);
#endif
            }

            if (!node.HasChildNodes) return;

            foreach (var subnode in node.ChildNodes)
            {
                MakeAbsoluteUrlsInternal(subnode, baseUrl);
            }
        }

        public static Uri TryGetLinkUrl(this HtmlNode node)
        {
            if (node == null) throw new ArgumentNullException();
            try
            {
                var baseUrl = node.OwnerDocument.GetLazyBaseUrl();
                var href = node.GetAttributeValue("href");
                if (href != null)
                {
                    return HttpUtils.GetAbsoluteUrlInternal(baseUrl, href, dontThrow: true);
                }

                var src = node.GetAttributeValue("src");
                if (src != null)
                {
                    if (node.TagName == "img") return TryGetImageUrl(node);
                    return HttpUtils.GetAbsoluteUrlInternal(baseUrl, src, dontThrow: true);
                }
                if (node.OwnerDocument.IsJson() || node.OwnerDocument.IsXml())
                {
                    var t = node.GetText();
                    if (t == null) return null;
                    return HttpUtils.GetAbsoluteUri(node.OwnerDocument.GetLazyPageUrl(), t);
                }
            }
            catch
            {
            }
            return null;
        }

        public static Uri TryGetLinkUrl(this HtmlNode node, string selector)
        {
            var subnode = node.FindSingle(selector);
            if (subnode == null) return null;
            return TryGetLinkUrl(subnode);
        }

        public static Uri GetLinkUrl(this HtmlNode node)
        {
            var result = TryGetLinkUrl(node);
            if (result == null) throw new UnparsableDataException() { SourceDataNode = node };
            return result;
        }

        public static Uri GetLinkUrl(this HtmlNode node, string selector)
        {
            var result = TryGetLinkUrl(node, selector);
            if (result == null) throw new UnparsableDataException(nodeQuery: selector) { SourceDataNode = node };
            return result;
        }

        private enum TextStatus
        {
            Start,
            MustInsertSpaceBeforeNextVisibleChar,
            MustInsertNewLineBeforeNextVisibleChar,
            LastCharWasVisible
        }


        public static string GetText(this HtmlNode node)
        {

            if (node == null) throw new ArgumentNullException();
            if (node.NodeType == HtmlNodeType.Text) return GetText((HtmlTextNode)node);

            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();

            TextStatus status = TextStatus.Start;
            AppendText(node, sb, ref status);
            TrimLastWhitespaceCharacters(sb);
            var s = sb.ToFinalString();
            ReseekableStringBuilder.Release(sb);
            return s;
        }


        public static string GetText(HtmlTextNode node)
        {
            var internalText = node.Text;

            int startOfText = -1;
            for (int i = 0; i < internalText.Length; i++)
            {
                if (!IsWhiteSpace(internalText[i]))
                {
                    startOfText = i;
                    break;
                }
            }
            if (startOfText == -1) return null;

            var endOfText = -1;
            for (int i = internalText.Length - 1; i >= 0; i--)
            {
                if (!IsWhiteSpace(internalText[i]))
                {
                    endOfText = i + 1;
                    break;
                }
            }
            var needsStringBuilder = false;
            var prevWasWhite = false;
            for (int i = startOfText; i < endOfText; i++)
            {
                var iswhite = IsWhiteSpace(internalText[i]);
                if (iswhite)
                {
                    if (prevWasWhite || internalText[i] != ' ')
                    {
                        needsStringBuilder = true;
                        break;
                    }
                }
                prevWasWhite = iswhite;
            }

            if (!needsStringBuilder)
            {
                return internalText.Substring(startOfText, endOfText - startOfText);
            }

            TextStatus status = TextStatus.Start;
            StringBuilder sb = ReseekableStringBuilder.AcquirePooledStringBuilder();

            for (int i = startOfText; i < endOfText; i++)
            {
                var ch = internalText[i];
                if (IsWhiteSpace(ch))
                {
                    if (status == TextStatus.LastCharWasVisible)
                    {
                        status = TextStatus.MustInsertSpaceBeforeNextVisibleChar;
                    }
                }
                else
                {
                    if (status == TextStatus.MustInsertSpaceBeforeNextVisibleChar) sb.Append(' ');

                    sb.Append(ch);
                    status = TextStatus.LastCharWasVisible;
                }
            }
            TrimLastWhitespaceCharacters(sb);
            var s = sb.ToFinalString();
            ReseekableStringBuilder.Release(sb);
            return s;
            

        }




        private static bool IsWhiteSpace(char ch)
        {
            return ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
        }

        public static string GetFirstLevelText(this HtmlNode node, bool includeLinks = false, bool includeBold = true)
        {
            if (!node.HasChildNodes) return null;
            return node.ChildNodes.Where(delegate (HtmlNode child)
            {
                if (child.NodeType == HtmlNodeType.Text) return true;
                var name = child.TagName;
                if (name == "br") return true;
                if (name == "p") return true;

                if (name == "a" && includeLinks) return true;
                if (name == "b" && includeBold) return true;
                if (name == "strong" && includeBold) return true;

                return false;
            }).GetText();
        }

        public static string GetText(this IEnumerable<HtmlNode> nodes)
        {

            if (nodes == null) throw new ArgumentNullException();

            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();

            TextStatus status = TextStatus.Start;

            foreach (var node in nodes)
            {
                if (ShouldIgnoreNonFirstLevelNodeForInnerText(node)) continue;
                AppendText(node, sb, ref status);
            }
            TrimLastWhitespaceCharacters(sb);
            var s = sb.ToFinalString();
            ReseekableStringBuilder.Release(sb);
            return s;
        }

        private static bool ShouldIgnoreNonFirstLevelNodeForInnerText(HtmlNode node)
        {
            return node.TagName == "script" || node.TagName == "style" || node.NodeType == HtmlNodeType.Comment;
        }

        private static string ToFinalString(this StringBuilder s)
        {
            var length = s.Length;
            if (s.Length == 0) return null;
#if SALTARELLE
            return s.ToString();
#else
            return s.SubstringCached(0);
#endif
        }

        private static void TrimLastWhitespaceCharacters(StringBuilder sb)
        {
#if SALTARELLE
            var s = sb.ToString();
            var length = s.Length;
            for (int i = length - 1; i >= 0; i--)
            {
                var ch = s[i];
                if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n') length--;
                else break;
            }
            if (length != s.Length)
            {
                sb.Clear();
                sb.Append(s.Substring(0, length));
            }
#else
            var initialLength = sb.Length;
            for (int i = initialLength - 1; i >= 0; i--)
            {
                var ch = sb[i];
                if (IsWhiteSpace(ch)) sb.Length--;
                else break;
            }
#endif
        }

        private static void AppendText(this HtmlNode node, StringBuilder sb, ref TextStatus status)
        {
            var textNode = node as HtmlTextNode;

            if (textNode != null)
            {
                var internalText = textNode.Text;
                for (int i = 0; i < internalText.Length; i++)
                {
                    var ch = internalText[i];
                    if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                    {
                        if (status == TextStatus.LastCharWasVisible)
                        {
                            status = TextStatus.MustInsertSpaceBeforeNextVisibleChar;
                        }
                    }
                    else
                    {
                        if (status == TextStatus.MustInsertNewLineBeforeNextVisibleChar) OnNewLine(sb, ref status);
                        else if (status == TextStatus.MustInsertSpaceBeforeNextVisibleChar) sb.Append(' ');

                        sb.Append(ch);
                        status = TextStatus.LastCharWasVisible;
                    }
                }
                return;
            }
            var isDisplayBlock = Configuration_DisplayBlockElements.Contains(node.TagName);

            if (status != TextStatus.Start)
            {

                if (isDisplayBlock)
                {
                    status = TextStatus.MustInsertNewLineBeforeNextVisibleChar;
                }
                else if (status == TextStatus.LastCharWasVisible)
                {
                    if (node.TagName == "td")
                    {
                        status = TextStatus.MustInsertSpaceBeforeNextVisibleChar;
                    }
                }
            }

            if (node.TagName == "br" || node.TagName == "p")
            {
                OnNewLine(sb, ref status);
            }


            if (node.HasChildNodes)
            {
                foreach (var subnode in node.ChildNodes)
                {
                    if (ShouldIgnoreNonFirstLevelNodeForInnerText(subnode)) continue;
                    subnode.AppendText(sb, ref status);
                }

                if (node.TagName == "p") //End of tag
                {
                    OnNewLine(sb, ref status);
                }

            }

            if (isDisplayBlock && status != TextStatus.Start)
            {
                status = TextStatus.MustInsertNewLineBeforeNextVisibleChar;
            }




        }

#if !SALTARELLE
        [Configuration]
#endif
        private readonly static string[] Configuration_DisplayBlockElements = new[] { "div", "article", "li", "ul" };

        private static void OnNewLine(StringBuilder sb, ref TextStatus status)
        {
            switch (status)
            {
                case TextStatus.Start:
                    // Do nothing
                    break;
                case TextStatus.MustInsertSpaceBeforeNextVisibleChar:
                    status = TextStatus.MustInsertNewLineBeforeNextVisibleChar;
                    break;
                case TextStatus.MustInsertNewLineBeforeNextVisibleChar:
                    sb.Append('\n');
                    break;
                case TextStatus.LastCharWasVisible:
                    status = TextStatus.MustInsertNewLineBeforeNextVisibleChar;
                    break;
            }

        }





        public static IEnumerable<HtmlNode> FindAll(this HtmlNode context, string selector)
        {
            if (selector == null || context == null) throw new ArgumentNullException();
#if !SKIP_FORMAT_FUNCTION
            if (selector.StartsWith(":code")) return FindWithCode(context, selector);
#endif
#if !SKIP_FIND_XPATH
            if (selector.StartsWith(":xpath")) return FindWithXPath(context, selector);
#endif
            return context.QuerySelectorAll(selector);
        }


        public static HtmlNode FindSingle(this HtmlNode context, string selector)
        {
            if (selector == null || context == null) throw new ArgumentNullException();
#if !SKIP_FORMAT_FUNCTION
            if (selector.StartsWith(":code")) return FindWithCode(context, selector).FirstOrDefault();
#endif
#if !SKIP_FIND_XPATH
            if (selector.StartsWith(":xpath")) return FindWithXPath(context, selector).FirstOrDefault();
#endif
            return context.QuerySelector(selector);
        }


#if !SKIP_FIND_XPATH
        private static IEnumerable<HtmlNode> FindWithXPath(HtmlNode context, string selector)
        {
            const int len = 7; // ":xpath(".Length;
            var sel = selector.Substring(len, selector.Length - len - 1);
            IEnumerable<HtmlNode> Enumerate()
            {
                var iter = new HtmlNodeNavigator(context).Select(sel);
                while (iter.MoveNext())
                {
                    var n = (HtmlNodeNavigator)iter.Current;
                    var z = n.CurrentNode;

                    yield return z;
                }
                yield break;
            };
            
            return Enumerate();
        }
#endif

#if !SKIP_FORMAT_FUNCTION
        [StaticFieldCategory(StaticFieldCategory.Cache)]
        private static Dictionary<string, FormatFunction> FormatFunctionCache = new Dictionary<string, FormatFunction>();

        private static IEnumerable<HtmlNode> FindWithCode(HtmlNode context, string selector)
        {
            FormatFunction k;
            lock (FormatFunctionCache)
            {
                k = FormatFunctionCache.TryGetValue(selector);
                if (k == null)
                {
                    var z = selector.AsValueString().Substring(5).Trim();
                    if (z.Length >= 2 && z[0] == '(' && z[z.Length - 1] == ')') z = z.Substring(1, z.Length - 2);
                    k = FormatFunction.Parse(z.ToClrString());
                    FormatFunctionCache[selector] = k;
                }
            }
            var exec = new FormatFunctionExecutor();
            exec.Variables = new Dictionary<string, object>();
            exec.Variables["node"] = context;
            var m = exec.ExecuteAsync(k).AssumeCompleted();
            if (m == null) return Enumerable.Empty<HtmlNode>();
            if (m is HtmlNode) return new[] { (HtmlNode)m };
            if (m is IEnumerable<HtmlNode>) return (IEnumerable<HtmlNode>)m;
            if (m is string) return new[] { FizzlerCustomSelectors.WrapText(context, (string)m) };
            if (m is IEnumerable<string>) return ((IEnumerable<string>)m).Select(x => FizzlerCustomSelectors.WrapText(context, x));
            throw new NotSupportedException(":code() function should return string(s) or HtmlNode(s).");
        }
#endif
#if !SALTARELLE

        private static void EnsurePageConstraints(HtmlNode node,
#if NET35
        IDictionary<string, string> metaParameters)
#else
        IReadOnlyDictionary<string, string> metaParameters)
#endif
        {
            if (metaParameters != null)
            {
                var mustHave = metaParameters.TryGetValue("$assert-selector");
                if (mustHave != null && node.FindSingle(mustHave) == null) throw new WebException("The retrieved page does not contain an element that matches " + mustHave, HttpUtils.Error_AssertSelectorMissing);

                var mustForbit = metaParameters.TryGetValue("$forbid-selector");
                if (mustForbit != null && node.FindSingle(mustForbit) != null) throw new WebException("The retrieved page contains an element that matches the forbidden selector " + mustForbit, HttpUtils.Error_ForbiddenSelectorExists);

                var errorSelector = metaParameters.TryGetValue("$error-selector");
                var error = errorSelector != null ? node.TryGetValue(errorSelector) : null;
                if (error != null) throw new WebException("The page reports: " + error, HttpUtils.Error_ErrorSelectorMatched);
            }
        }
#endif

        public static string TrimSize(this string str, int size)
        {
            if (str == null) throw new ArgumentNullException();
            if (size < 1) throw new ArgumentException();
            if (str.Length > size) return str.Substring(0, size);
            else return str;
        }

        public static string TrimSize(this string str, int size, int allowedExtraChars, bool hypens)
        {
            if (str == null) throw new ArgumentNullException();
            if (size < 1) throw new ArgumentException();
            if (allowedExtraChars < 0) throw new ArgumentException();
            if (str.Length > size + allowedExtraChars)
            {
                str = str.Substring(0, size);
                if (hypens) str += "…";
                return str;
            }
            else
            {
                return str;
            }
        }

#if !SALTARELLE
        [RestrictedAccess]
        public static Task<T> GetJsonAsync<T>(this Uri url, WebRequestOptions options = null)
        {
            return new LazyUri(url).GetJsonAsync<T>(options);
        }
        [RestrictedAccess]
        public async static Task<T> GetJsonAsync<T>(this LazyUri url, WebRequestOptions options = null)
        {
            var str = await url.GetStringAsync(options);
            if (typeof(JToken).GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo())) return (T)(object)HttpUtils.ReadJsonToken(str);
            return JsonConvert.DeserializeObject<T>(str);
        }


        public static Task<string> GetStringAsync(this Uri url, WebRequestOptions options = null)
        {
            return new LazyUri(url).GetStringAsync(options);
        }

        public async static Task<string> GetStringAsync(this LazyUri url, WebRequestOptions options = null)
        {
            var p = url.AbsoluteUri;
            var q = "$assume-text=1";
            if (string.IsNullOrEmpty(url.Fragment)) p += "#" + q;
            else p += "&" + q;

            return (await p.AsLazyUri().GetHtmlNodeAsync(options)).OwnerDocument.GetPlainText();
        }

        [RestrictedAccess]
        public static Task<HttpResponseMessage> GetAsync(this Uri url, WebRequestOptions options = null)
        {
            var u = new LazyUri(url);
            return u.GetResponseAsync(options);
        }

        public static async Task<Stream> GetStreamAsync(this Uri url, WebRequestOptions options = null)
        {
            var u = new LazyUri(url);
            var response = await u.GetResponseAsync(options);
#if NET35
            return response.GetResponseStream();
#else
            return await response.Content.ReadAsStreamAsync();
#endif
        }


        internal static 
#if NET35
        IDictionary<string, string>
#else
        IReadOnlyDictionary<string, string>
#endif
        ProcessMetaParameters(LazyUri url, WebRequestOptions options)
        {
            Dictionary<string, JToken> jsonPostObjects = null;
            Dictionary<string, JToken> jsonQueryObjects = null;
            JToken jsonPostSingleJson = null;
            var metaParameters = url.FragmentParameters.Where(x => x.Key.StartsWith("$")).ToDictionary();
            if (metaParameters.Any())
            {


                if (metaParameters.TryGetValue("$js") == "1")
                {
                    return metaParameters;
                }
                else
                {

                    if (options == null) options = new WebRequestOptions();
                    foreach (var item in metaParameters)
                    {
                        var key = item.Key;
                        if (key == "$method")
                        {
                            options.Method = item.Value;
                        }
                        else if (key.StartsWith("$cookie-"))
                        {
                            options.AddCookie(key.Substring(8), item.Value, PriorityCookie.PRIORITY_MetaParameter);
                        }
                        else if (key.StartsWith("$post-"))
                        {
                            options.AddPostField(key.Substring(6), item.Value);
                        }
                        else if (key == "$post")
                        {
                            options.PostString = item.Value;
                        }
                        else if (key.StartsWith("$json-post.") || key == "$json-post~")
                        {
                            SetJsonMetaparameter(ref jsonPostSingleJson, key.Substring(10), item.Value);
                        }
                        else if (key.StartsWith("$json-post-"))
                        {
                            if (jsonPostObjects == null) jsonPostObjects = new Dictionary<string, JToken>();
                            AddJsonPartialField(jsonPostObjects, key.Substring(11), item.Value);
                        }
                        else if (key.StartsWith("$json-query-"))
                        {
                            if (jsonQueryObjects == null) jsonQueryObjects = new Dictionary<string, JToken>();
                            AddJsonPartialField(jsonQueryObjects, key.Substring(12), item.Value);
                        }
                        else if (key.StartsWith("$json-query"))
                        {
                            throw new NotSupportedException("$json-query-paramname parameters must specified a parameter name.");
                        }
                        else if (key.StartsWith("$header-"))
                        {
                            var name = key.Substring(8);
                            if (name == "User-Agent") options.UserAgent = item.Value;
                            else if (name == "Referer")
                            {
                                options.Referrer = HttpUtils.GetAbsoluteUriAsString(url, item.Value).AsUri();
                            }
                            else options.AddHeader(name, item.Value);
                        }
                        else if (key == "$allow-redir")
                        {
                            if (item.Value == "0") options.AllowRedirects = false;
                        }
                        else if (key == "$timeout")
                        {
                            options.Timeout = int.Parse(item.Value);
                            options.TimeoutSecondRetrialAfterError = null; // options.Timeout;
                            options.TimeoutStartSecondRetrial = null;
                        }
                        else if (key == "$waitbefore")
                        {
                            options.WaitBefore = TimeSpan.FromMilliseconds(int.Parse(item.Value));
                        }
                        else if (key == "$xhr-request" && item.Value == "1")
                        {
                            options.AddHeader("X-Requested-With", "XMLHttpRequest");
                        }
                    }
                    if (jsonPostObjects != null)
                    {
                        foreach (var item in jsonPostObjects)
                        {
                            options.AddPostField(item.Key, item.Value.ToString(Newtonsoft.Json.Formatting.None));
                        }
                    }
                    if (jsonQueryObjects != null)
                    {
                        foreach (var item in jsonQueryObjects)
                        {
                            options.AddQueryParameter(item.Key, item.Value.ToString(Formatting.None));
                        }
                    }
                    if (jsonPostSingleJson != null)
                    {
                        options.PostString = jsonPostSingleJson.ToString(Newtonsoft.Json.Formatting.None);
                        if (metaParameters.TryGetValue("header-Content-Type") == null)
                        {
                            options.AddHeader("Content-Type", "application/json");
                        }

                    }
                }
            }
            return metaParameters;

        }

        private static bool IsJsonArray(ValueString key, string value)
        {
            if (key.EndsWith("~") && value == "--" && key.IndexOf('.', 1) == -1) return true;
            return key.ContainsIndex(1) && key[0] == '.' && char.IsDigit(key[1]);
        }
        private static JToken CreateJsonContainer(ValueString key, string value)
        {
            if (IsJsonArray(key, value)) return new JArray();
            return new JObject();
        }

        private static char[] JsonFieldSeparators = new char[] { '.', '~' };

        private static void AddJsonPartialField(Dictionary<string, JToken> dict, string path, string value)
        {
            var idx = path.IndexOfAny(JsonFieldSeparators);
            if (idx == -1) idx = path.Length;
            var key = path.Substring(0, idx);
            var obj = dict.TryGetValue(key);
            var old = obj;
            SetJsonMetaparameter(ref obj, path.Substring(idx), value);
            if (old != obj) dict[key] = obj;
        }

        private static void SetJsonMetaparameter(ref JToken obj, string path, string value)
        {
            if (path.StartsWith("..") && char.IsDigit(path[2]))
            {
                var jarr = obj as JArray;
                if (jarr == null)
                {
                    if (obj == null || obj.Type == JTokenType.Undefined) obj = jarr = new JArray();
                    else throw new FormatException("Inconsistent JSON metaparameter types.");
                }
                var end = path.IndexOfAny(JsonFieldSeparators, 2);
                if (end == -1) end = path.Length;
                var index = ValueString.ParseInt32(path.SubstringValue(2, end - 2));
                while (!jarr.ContainsIndex(index)) jarr.Add(JValue.CreateUndefined());
                var sub = jarr[index];
                var old = sub;
                SetJsonMetaparameter(ref sub, path.Substring(end), value);
                if (sub != old) jarr[index] = sub;
            }
            else if (path.StartsWith("."))
            {
                var jobj = obj as JObject;
                if (jobj == null)
                {
                    if (obj == null || obj.Type == JTokenType.Undefined) obj = jobj = new JObject();
                    else throw new FormatException("Inconsistent JSON metaparameter types.");
                }
                var end = path.IndexOfAny(JsonFieldSeparators, 1);
                if (end == -1) end = path.Length;
                var key = path.Substring(1, end - 1);
                var sub = jobj[key];
                var old = sub;
                SetJsonMetaparameter(ref sub, path.Substring(end), value);
                if (sub != old) jobj[key] = sub;
            }
            else if (path == "~")
            {
                if (value == "-") obj = new JObject();
                else if (value == "--") obj = new JArray();
                else obj = HttpUtils.ReadJsonToken(value);
            }
            else if (path == string.Empty)
            {
                obj = new JValue(value);
            }
            else
            {
                throw new FormatException("Bad JSON metaparameter.");
            }
        }

        private static JToken GetPropertyOrElement(JToken obj, string key)
        {
            var jobj = obj as JObject;
            return jobj != null ? jobj.TryGetJToken(key) : ((JArray)obj)[ValueString.ParseInt32(key.SubstringValue(1))];
        }
        private static void SetPropertyOrElement(JToken obj, string key, JToken value)
        {
            var jobj = obj as JObject;
            if (jobj != null) jobj[key] = value;
            else ((JArray)obj)[ValueString.ParseInt32(key.SubstringValue(1))] = value;
        }

#endif

#if STANDALONE
        internal static bool ContainsIndex<T>(this List<T> array, int index)
        {
            return index >= 0 && index < array.Count;
        }
#endif


        public static LazyUri GetLazyPageUrl(this HtmlDocument doc)
        {
#if SALTARELLE
            return doc.PageUrl;
#else
            if (doc._pageUrlCustom != null) return (LazyUri)doc._pageUrlCustom;
            var m = doc.PageUrl;
            if (m != null)
            {
                var z = new LazyUri(m);
                doc._pageUrlCustom = z;
                return z;
            }
            return null;
#endif
        }
        public static LazyUri GetLazyBaseUrl(this HtmlDocument doc)
        {
#if SALTARELLE
            return doc.BaseUrl;
#else
            if (doc._baseUrlCustom != null) return (LazyUri)doc._baseUrlCustom;
            LazyUri _baseUrl = null;
            var b = doc.DocumentNode.GetAttributeValue("base-url");
            if (b == null)
            {
                foreach (var basenode in doc.DocumentNode.DescendantsAndSelf("base"))
                {
                    var h = basenode.GetAttributeValue("href");
                    if (h != null)
                    {
                        try
                        {
                            _baseUrl = new LazyUri(HttpUtils.GetAbsoluteUrlInternal(doc.GetLazyPageUrl(), h));
                            b = _baseUrl.AbsoluteUri;
                        }
                        catch
                        {
                        }
                        break;
                    }
                }
                if (b == null)
                {
                    _baseUrl = doc.GetLazyPageUrl();
                    b = _baseUrl != null ? _baseUrl.AbsoluteUri : string.Empty;
                }
                doc.DocumentNode.SetAttributeValue("base-url", b);
            }
            else
            {
                if (string.IsNullOrEmpty(b)) return null;
                _baseUrl = new LazyUri(b);
            }
            doc._baseUrlCustom = _baseUrl;
            return _baseUrl;
#endif
        }

        public static bool IsJson(this HtmlDocument document)
        {
            return document.DocumentNode.GetAttributeValue("awdee-converted-json") == "1";
        }

        public static bool IsXml(this HtmlDocument document)
        {
            return ((document.OptionParseAsXml || document.DocumentNode.ChildNodes.Any(x => x.TagName == "?xml")) && !document.DocumentNode.ChildNodes.Any(x => x.TagName == "html"));
        }


        public static void SetPageUrl(this HtmlDocument document, Uri url)
        {
            document.PageUrl = url;
#if !SALTARELLE
            document._pageUrlCustom = null;
#endif
        }
#if !SALTARELLE
        public static void SetPageUrl(this HtmlDocument document, LazyUri url)
        {
            document.ClearPageUrlCache();
            document._pageUrlCustom = url;
        }
#endif


        [StaticFieldCategory(StaticFieldCategory.Stable)]
        internal 
#if !SALTARELLE
        volatile 
#endif

        static Dictionary<string, string> mimeToExtension;
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        internal static Dictionary<string, string> extensionToMime;
        internal static void InitializeMimesDictionary()
        {
            if (mimeToExtension == null)
            {
                lock (typeof(HttpExtensionMethods))
                {
                    if (mimeToExtension == null)
                    {
                        mimeToExtension = new Dictionary<string, string>();
                        extensionToMime = new Dictionary<string, string>();

                        AddMimeExtensionCouple("application/fractals", ".fif");
                        AddMimeExtensionCouple("application/futuresplash", ".spl");
                        AddMimeExtensionCouple("application/hta", ".hta");
                        AddMimeExtensionCouple("application/mac-binhex40", ".hqx");
                        AddMimeExtensionCouple("application/ms-vsi", ".vsi");
                        AddMimeExtensionCouple("application/msaccess", ".accdb");
                        AddMimeExtensionCouple("application/msaccess.AddMimeExtensionCouplein", ".accda");
                        AddMimeExtensionCouple("application/msaccess.cab", ".accdc");
                        AddMimeExtensionCouple("application/msaccess.exec", ".accde");
                        AddMimeExtensionCouple("application/msaccess.ftemplate", ".accft");
                        AddMimeExtensionCouple("application/msaccess.runtime", ".accdr");
                        AddMimeExtensionCouple("application/msaccess.template", ".accdt");
                        AddMimeExtensionCouple("application/msaccess.webapplication", ".accdw");
                        AddMimeExtensionCouple("application/msonenote", ".one");
                        AddMimeExtensionCouple("application/msword", ".doc");
                        AddMimeExtensionCouple("application/opensearchdescription+xml", ".osdx");
                        AddMimeExtensionCouple("application/oxps", ".oxps");
                        AddMimeExtensionCouple("application/pdf", ".pdf");
                        AddMimeExtensionCouple("application/pkcs10", ".p10");
                        AddMimeExtensionCouple("application/pkcs7-mime", ".p7c");
                        AddMimeExtensionCouple("application/pkcs7-signature", ".p7s");
                        AddMimeExtensionCouple("application/pkix-cert", ".cer");
                        AddMimeExtensionCouple("application/pkix-crl", ".crl");
                        AddMimeExtensionCouple("application/postscript", ".ps");
                        AddMimeExtensionCouple("application/vnd.ms-excel", ".xls");
                        AddMimeExtensionCouple("application/vnd.ms-excel.12", ".xlsx");
                        AddMimeExtensionCouple("application/vnd.ms-excel.AddMimeExtensionCouplein.macroEnabled.12", ".xlam");
                        AddMimeExtensionCouple("application/vnd.ms-excel.sheet.binary.macroEnabled.12", ".xlsb");
                        AddMimeExtensionCouple("application/vnd.ms-excel.sheet.macroEnabled.12", ".xlsm");
                        AddMimeExtensionCouple("application/vnd.ms-excel.template.macroEnabled.12", ".xltm");
                        AddMimeExtensionCouple("application/vnd.ms-officetheme", ".thmx");
                        AddMimeExtensionCouple("application/vnd.ms-pki.certstore", ".sst");
                        AddMimeExtensionCouple("application/vnd.ms-pki.pko", ".pko");
                        AddMimeExtensionCouple("application/vnd.ms-pki.seccat", ".cat");
                        AddMimeExtensionCouple("application/vnd.ms-pki.stl", ".stl");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint", ".ppt");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint.12", ".pptx");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint.AddMimeExtensionCouplein.macroEnabled.12", ".ppam");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint.presentation.macroEnabled.12", ".pptm");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint.slide.macroEnabled.12", ".sldm");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint.slideshow.macroEnabled.12", ".ppsm");
                        AddMimeExtensionCouple("application/vnd.ms-powerpoint.template.macroEnabled.12", ".potm");
                        AddMimeExtensionCouple("application/vnd.ms-publisher", ".pub");
                        AddMimeExtensionCouple("application/vnd.ms-visio.viewer", ".vdx");
                        AddMimeExtensionCouple("application/vnd.ms-word.document.12", ".docx");
                        AddMimeExtensionCouple("application/vnd.ms-word.document.macroEnabled.12", ".docm");
                        AddMimeExtensionCouple("application/vnd.ms-word.template.12", ".dotx");
                        AddMimeExtensionCouple("application/vnd.ms-word.template.macroEnabled.12", ".dotm");
                        AddMimeExtensionCouple("application/vnd.ms-wpl", ".wpl");
                        AddMimeExtensionCouple("application/vnd.ms-xpsdocument", ".xps");
                        AddMimeExtensionCouple("application/vnd.oasis.opendocument.presentation", ".odp");
                        AddMimeExtensionCouple("application/vnd.oasis.opendocument.spreadsheet", ".ods");
                        AddMimeExtensionCouple("application/vnd.oasis.opendocument.text", ".odt");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.presentationml.slide", ".sldx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.presentationml.slideshow", ".ppsx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.presentationml.template", ".potx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.spreadsheetml.template", ".xltx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx");
                        AddMimeExtensionCouple("application/vnd.openxmlformats-officedocument.wordprocessingml.template", ".dotx");
                        AddMimeExtensionCouple("application/x-bittorrent", ".torrent");
                        AddMimeExtensionCouple("application/x-bittorrent-app", ".btapp");
                        AddMimeExtensionCouple("application/x-bittorrent-appinst", ".btinstall");
                        AddMimeExtensionCouple("application/x-bittorrent-key", ".btkey");
                        AddMimeExtensionCouple("application/x-bittorrent-skin", ".btskin");
                        AddMimeExtensionCouple("application/x-bittorrentsearchdescription+xml", ".btsearch");
                        AddMimeExtensionCouple("application/x-bridge-url", ".adobebridge");
                        AddMimeExtensionCouple("application/x-compress", ".z");
                        AddMimeExtensionCouple("application/x-compressed", ".tgz");
                        AddMimeExtensionCouple("application/x-gzip", ".gz");
                        AddMimeExtensionCouple("application/x-java-jnlp-file", ".jnlp");
                        AddMimeExtensionCouple("application/x-jtx+xps", ".jtx");
                        AddMimeExtensionCouple("application/x-latex", ".latex");
                        AddMimeExtensionCouple("application/x-mix-transfer", ".nix");
                        AddMimeExtensionCouple("application/x-mplayer2", ".asx");
                        AddMimeExtensionCouple("application/x-ms-application", ".application");
                        AddMimeExtensionCouple("application/x-ms-vsto", ".vsto");
                        AddMimeExtensionCouple("application/x-ms-wmd", ".wmd");
                        AddMimeExtensionCouple("application/x-ms-wmz", ".wmz");
                        AddMimeExtensionCouple("application/x-ms-xbap", ".xbap");
                        AddMimeExtensionCouple("application/x-mswebsite", ".website");
                        AddMimeExtensionCouple("application/x-pkcs12", ".p12");
                        AddMimeExtensionCouple("application/x-pkcs7-certificates", ".p7b");
                        AddMimeExtensionCouple("application/x-pkcs7-certreqresp", ".p7r");
                        AddMimeExtensionCouple("application/x-shockwave-flash", ".swf");
                        AddMimeExtensionCouple("application/x-stuffit", ".sit");
                        AddMimeExtensionCouple("application/x-tar", ".tar");
                        AddMimeExtensionCouple("application/x-troff-man", ".man");
                        AddMimeExtensionCouple("application/x-wmplayer", ".asx");
                        AddMimeExtensionCouple("application/x-x509-ca-cert", ".cer");
                        AddMimeExtensionCouple("application/x-zip-compressed", ".zip");
                        AddMimeExtensionCouple("application/xaml+xml", ".xaml");
                        AddMimeExtensionCouple("application/xhtml+xml", ".xht");
                        AddMimeExtensionCouple("application/xml", ".xml");
                        AddMimeExtensionCouple("application/xps", ".xps");
                        AddMimeExtensionCouple("application/zip", ".zip");
                        AddMimeExtensionCouple("audio/3gpp", ".3gp");
                        AddMimeExtensionCouple("audio/3gpp2", ".3g2");
                        AddMimeExtensionCouple("audio/aiff", ".aiff");
                        AddMimeExtensionCouple("audio/basic", ".au");
                        AddMimeExtensionCouple("audio/ec3", ".ec3");
                        AddMimeExtensionCouple("audio/mid", ".mid");
                        AddMimeExtensionCouple("audio/midi", ".mid");
                        AddMimeExtensionCouple("audio/mp3", ".mp3");
                        AddMimeExtensionCouple("audio/mp4", ".m4a");
                        AddMimeExtensionCouple("audio/mpeg", ".mp3");
                        AddMimeExtensionCouple("audio/mpegurl", ".m3u");
                        AddMimeExtensionCouple("audio/mpg", ".mp3");
                        AddMimeExtensionCouple("audio/vnd.dlna.adts", ".adts");
                        AddMimeExtensionCouple("audio/vnd.dolby.dd-raw", ".ac3");
                        AddMimeExtensionCouple("audio/wav", ".wav");
                        AddMimeExtensionCouple("audio/x-aiff", ".aiff");
                        AddMimeExtensionCouple("audio/x-mid", ".mid");
                        AddMimeExtensionCouple("audio/x-midi", ".mid");
                        AddMimeExtensionCouple("audio/x-mp3", ".mp3");
                        AddMimeExtensionCouple("audio/x-mpeg", ".mp3");
                        AddMimeExtensionCouple("audio/x-mpegurl", ".m3u");
                        AddMimeExtensionCouple("audio/x-mpg", ".mp3");
                        AddMimeExtensionCouple("audio/x-ms-wax", ".wax");
                        AddMimeExtensionCouple("audio/x-ms-wma", ".wma");
                        AddMimeExtensionCouple("audio/x-wav", ".wav");
                        AddMimeExtensionCouple("image/bmp", ".bmp");
                        AddMimeExtensionCouple("image/gif", ".gif");
                        AddMimeExtensionCouple("image/jpeg", ".jpeg");
                        AddMimeExtensionCouple("image/jpeg", ".jpg");
                        AddMimeExtensionCouple("image/pjpeg", ".jpg");
                        AddMimeExtensionCouple("image/png", ".png");
                        AddMimeExtensionCouple("image/svg+xml", ".svg");
                        AddMimeExtensionCouple("image/tiff", ".tiff");
                        AddMimeExtensionCouple("image/vnd.ms-photo", ".wdp");
                        AddMimeExtensionCouple("image/x-emf", ".emf");
                        AddMimeExtensionCouple("image/x-icon", ".ico");
                        AddMimeExtensionCouple("image/x-png", ".png");
                        AddMimeExtensionCouple("image/x-wmf", ".wmf");
                        AddMimeExtensionCouple("image/webp", ".webp");
                        AddMimeExtensionCouple("image/jxr", ".wdp");
                        AddMimeExtensionCouple("image/jxr", ".hdp");
                        AddMimeExtensionCouple("image/jxr", ".jxr");
                        AddMimeExtensionCouple("midi/mid", ".mid");
                        AddMimeExtensionCouple("model/vnd.dwfx+xps", ".dwfx");
                        AddMimeExtensionCouple("model/vnd.easmx+xps", ".easmx");
                        AddMimeExtensionCouple("model/vnd.edrwx+xps", ".edrwx");
                        AddMimeExtensionCouple("model/vnd.eprtx+xps", ".eprtx");
                        AddMimeExtensionCouple("pkcs10", ".p10");
                        AddMimeExtensionCouple("pkcs7-mime", ".p7m");
                        AddMimeExtensionCouple("pkcs7-signature", ".p7s");
                        AddMimeExtensionCouple("pkix-cert", ".cer");
                        AddMimeExtensionCouple("pkix-crl", ".crl");
                        AddMimeExtensionCouple("text/calendar", ".ics");
                        AddMimeExtensionCouple("text/css", ".css");
                        AddMimeExtensionCouple("text/directory", ".vcf");
                        AddMimeExtensionCouple("text/directory;profile=vCard", ".vcf");
                        AddMimeExtensionCouple("text/html", ".htm");
                        AddMimeExtensionCouple("text/plain", ".sql");
                        AddMimeExtensionCouple("text/scriptlet", ".wsc");
                        AddMimeExtensionCouple("text/vcard", ".vcf");
                        AddMimeExtensionCouple("text/x-component", ".htc");
                        AddMimeExtensionCouple("text/x-ms-contact", ".contact");
                        AddMimeExtensionCouple("text/x-ms-iqy", ".iqy");
                        AddMimeExtensionCouple("text/x-ms-odc", ".odc");
                        AddMimeExtensionCouple("text/x-ms-rqy", ".rqy");
                        AddMimeExtensionCouple("text/x-vcard", ".vcf");
                        AddMimeExtensionCouple("text/xml", ".xml");
                        AddMimeExtensionCouple("video/3gpp", ".3gp");
                        AddMimeExtensionCouple("video/3gpp2", ".3g2");
                        AddMimeExtensionCouple("video/avi", ".avi");
                        AddMimeExtensionCouple("video/mp4", ".mp4");
                        AddMimeExtensionCouple("video/mpeg", ".mpeg");
                        AddMimeExtensionCouple("video/mpg", ".mpeg");
                        AddMimeExtensionCouple("video/msvideo", ".avi");
                        AddMimeExtensionCouple("video/quicktime", ".mov");
                        AddMimeExtensionCouple("video/vnd.dlna.mpeg-tts", ".tts");
                        AddMimeExtensionCouple("video/wtv", ".wtv");
                        AddMimeExtensionCouple("video/x-mpeg", ".mpeg");
                        AddMimeExtensionCouple("video/x-mpeg2a", ".mpeg");
                        AddMimeExtensionCouple("video/x-ms-asf", ".asx");
                        AddMimeExtensionCouple("video/x-ms-asf-plugin", ".asx");
                        AddMimeExtensionCouple("video/x-ms-dvr", ".dvr-ms");
                        AddMimeExtensionCouple("video/x-ms-wm", ".wm");
                        AddMimeExtensionCouple("video/x-ms-wmv", ".wmv");
                        AddMimeExtensionCouple("video/x-ms-wmx", ".wmx");
                        AddMimeExtensionCouple("video/x-ms-wvx", ".wvx");
                        AddMimeExtensionCouple("video/x-msvideo", ".avi");
                        AddMimeExtensionCouple("video/webm", ".webm");
                        AddMimeExtensionCouple("vnd.ms-pki.certstore", ".sst");
                        AddMimeExtensionCouple("vnd.ms-pki.pko", ".pko");
                        AddMimeExtensionCouple("vnd.ms-pki.seccat", ".cat");
                        AddMimeExtensionCouple("vnd.ms-pki.stl", ".stl");
                        AddMimeExtensionCouple("x-pkcs12", ".p12");
                        AddMimeExtensionCouple("x-pkcs7-certificates", ".p7b");
                        AddMimeExtensionCouple("x-pkcs7-certreqresp", ".p7r");
                        AddMimeExtensionCouple("x-x509-ca-cert", ".cer");

                    }
                }
            }
        }

        private static void AddMimeExtensionCouple(string mime, string extension)
        {
            mimeToExtension[mime] = extension;
            var old = extensionToMime.TryGetValue(extension);
            if (old == null || old.Length >= mime.Length) extensionToMime[extension] = mime;
        }

#if !SALTARELLE
        public static void Report(this IProgress<DataTransferProgress> progress, string status)
        {
            if (progress == null) return;
            progress.Report(new DataTransferProgress(status));
        }
#endif


    }
}
