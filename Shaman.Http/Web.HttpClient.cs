using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
#if NATIVE_HTTP
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
#else
using System.Net.Reimpl;
using System.Net.Reimpl.Http;
using System.Net.Reimpl.Http.Headers;
#endif
using System.Threading.Tasks;
using Shaman.Dom;
using System.Threading;
using System.Reflection;
#if !STANDALONE
using HttpUtils = Shaman.Utils;
#endif
#if NETFX_CORE
using Windows.Storage;
#endif
using StreamWriteAction = System.Action<System.IO.Stream>;
using Shaman.Runtime;
using WebExceptionStatus = System.Net.WebExceptionStatus;
#if NET35
using HttpResponseMessage = System.Net.HttpWebResponse;
using HttpRequestMessage = System.Net.HttpWebRequest;
#else
using TaskEx = System.Threading.Tasks.Task;
#endif

#if SMALL_LIB_AWDEE
namespace Shaman
#else
namespace Xamasoft
#endif
{

    public static partial class
#if STANDALONE
 HttpExtensionMethods
#else
ExtensionMethods
#endif
    {

        internal class HttpResponseInfo
        {
            public HttpResponseMessage Response;
            public LazyUri RespondingUrl;
            public Exception Exception;
        }



        [AllowNumericLiterals]
        internal static async Task<HttpResponseInfo> SendAsync(this LazyUri url, WebRequestOptions options, HttpRequestMessageBox messageBox, bool alwaysCatchAndForbidRedirects = false, bool keepResponseAliveOnError = false)
        {
            await Utils.CheckLocalFileAccessAsync(url);
            Utils.RaiseWebRequestEvent(url, true);
            HttpResponseMessage result = null;
            LazyUri previousResponse2 = null;
            try
            {

                if (options == WebRequestOptions.DefaultOptions) throw new ArgumentException();
                if (options.WaitBefore.Ticks != 0)
                    await TaskEx.Delay(options.WaitBefore);
                LazyUri previousResponse1 = null;
                previousResponse2 = url.Clone();
                previousResponse2 = MaybeAddAdditionalQueryParameters(previousResponse2, options);
                var redirectIndex = 0;
                while (true)
                {
#if WEBCLIENT
                    HttpContent requestContent;
#endif
                    var message = CreateRequestInternal(previousResponse2, options, true, redirectIndex
#if WEBCLIENT
                    , out requestContent
#endif              
                    );
                    if (messageBox != null)
                    {
                        messageBox.Dispose();
                        messageBox.Message = message;
                    }

#if WEBCLIENT
                    if(requestContent != null)
                    {
                        if(requestContent.ContentType != null) message.ContentType = requestContent.ContentType;
                        if(requestContent.ContentDisposition != null) message.Headers["Content-Disposition"] = requestContent.ContentDisposition;
                        using(var req = await message.GetRequestStreamAsync())
                        {
                            await requestContent.CopyToAsync(req);
                        }
                    }
                    result = (HttpWebResponse)await message.GetResponseAsync();
#else
                    var client = CreateHttpClient();
                    result = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
#endif





#if !WEBCLIENT
                    if (result.Content != null && result.Content.Headers.ContentType != null && result.Content.Headers.ContentType.CharSet == "utf8")
                        result.Content.Headers.ContentType.CharSet = "utf-8";
#endif

                    if ((int)result.StatusCode >= 400)
                    {
                        if(!keepResponseAliveOnError) result.Dispose();
                        // Hackish, change purpose of enumeration type
                        throw new WebException("The web server returned: " + result.StatusCode.ToString(), (WebExceptionStatus)result.StatusCode);
                    }
#if WEBCLIENT
                    var zz = result.Headers["Location"];
                    var redirectUrlNative = zz != null ? HttpUtils.GetAbsoluteUri(url.PathConsistentUrl, zz) : null;
#else
                    var redirectUrlNative = result.Headers.Location;
#endif

                    if (redirectUrlNative == null)
                    {
                        return new HttpResponseInfo() { RespondingUrl = previousResponse2, Response = result };

                    }
                    else
                    {

                        if (alwaysCatchAndForbidRedirects) return new HttpResponseInfo() { Response = result, RespondingUrl = previousResponse2, Exception = new WebException("Unexpected redirect", HttpUtils.UnexpectedRedirect) };

                        result.Dispose();
                        var redirectUrl = new LazyUri(redirectUrlNative);
                        if (!redirectUrl.IsAbsoluteUri) redirectUrl = new LazyUri(new Uri(previousResponse2.PathConsistentUrl, redirectUrlNative));
                        if (options != null && !options.AllowRedirects) throw new WebException("Unexpected redirect was received.", HttpUtils.UnexpectedRedirect);
                        if (redirectIndex == Configuration_MaximumNumberOfRedirects) throw new WebException("The maximum number of redirects has been reached.", HttpUtils.MaximumNumberOfRedirectsExceeded);

                        if (!(redirectIndex == 0 && options != null && (options.PostData != null || options.PostString != null)))
                            if ((previousResponse1 != null && HttpUtils.UrisEqual(redirectUrl.PathAndQueryConsistentUrl, previousResponse1.PathAndQueryConsistentUrl)) || HttpUtils.UrisEqual(redirectUrl, previousResponse2)) throw new WebException("The server isn't redirecting the requested resource properly.", HttpUtils.RedirectLoopDetected);
                        previousResponse1 = previousResponse2;
                        previousResponse2 = redirectUrl;

                        redirectIndex++;


                    }
                }
            }
            catch (Exception ex)
            {
                var orig = ex;
#if !WEBCLIENT
                var hre = ex as HttpRequestException;
                if (hre != null && hre.InnerException != null) ex = hre.InnerException;
#endif
                if (alwaysCatchAndForbidRedirects) return new HttpResponseInfo() { Exception = ex, Response = result, RespondingUrl = previousResponse2 };
                else if (ex == orig) throw;
                else throw ex;
            }
        }


        private static HttpRequestMessage CreateRequestInternal(LazyUri url, WebRequestOptions options, bool forceSameUrl, int redirectionIndex
#if WEBCLIENT
        , out HttpContent requestContent
#endif
        )
        {
            if (!forceSameUrl) url = MaybeAddAdditionalQueryParameters(url, options);
#if WEBCLIENT
            var message = (HttpWebRequest)WebRequest.Create(url.PathAndQueryConsistentUrl);
            message.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            message.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.Deflate;
            requestContent = null;
#else
            var message = new HttpRequestMessage();
            message.RequestUri = url.PathAndQueryConsistentUrl;
#endif
            if (options != null)
            {
                if (redirectionIndex != 0) message.Method = HttpMethod.Get;
                else if (options._method != null)
#if WEBCLIENT 
                    message.Method = options._method;
#else
                    message.Method = new HttpMethod(options._method);
#endif
                else message.Method = (options.PostData != null || options.PostString != null || options.WriteRequest != null) ? HttpMethod.Post : HttpMethod.Get;
            }

#if WEBCLIENT
            message.ServicePoint.Expect100Continue = false;
#else
            HttpContent requestContent = null;
            message.Headers.ExpectContinue = false;     
#endif

            if (options != null)
            {
                if (options.WriteRequest != null)
                {
                    requestContent = new StreamWriteContent(options.WriteRequest);
                }
                else if (options.PostData != null && redirectionIndex == 0)
                {
                    if (options.PostData.Any(x =>
#if NETFX_CORE
 x.Value is StorageFile ||
#else
 x.Value is FileInfo ||
#endif
 x.Value is Stream ||
                        x.Value is StreamWriteAction))
                    {
                        var content = new MultipartFormDataContent();
                        foreach (var item in Flatten(options.PostData))
                        {
                            var value = item.Value;
                            if (value is string || value == null || value.GetType().GetTypeInfo().IsPrimitive) content.Add(new StringContent(ToString(value)), item.Key);
                            else if (value is Stream)
                            {
                                var cnt = new StreamContent((Stream)value);
                                SetMultipartContentType(cnt);
                                content.Add(cnt, item.Key, "file.dat");
                            }
#if NETFX_CORE
                            else if (value is StorageFile)
                            {
                                throw new NotImplementedException();
                                var file = ((StorageFile)value);
                                var t = file.OpenSequentialReadAsync().AsTask();
                                t.Wait();
                                content.Add(new StreamContent(t.Result.AsStreamForRead()), item.Key, file.Name);
                            }
#else
                            else if (value is FileInfo)
                            {
                                var file = ((FileInfo)value);
                                var cnt = new StreamContent(file.OpenRead());
                                SetMultipartContentType(cnt);
                                content.Add(cnt, item.Key, file.Name);
                            }
#endif
                            else if (value is StreamWriteAction)
                            {
                                var cnt = new StreamWriteContent((StreamWriteAction)value);
                                SetMultipartContentType(cnt);
                                content.Add(cnt, item.Key, "file.dat");
                            }
                            else throw new NotSupportedException();
                        }
                        requestContent = content;
                    }
                    else
                    {
                        requestContent = new OptimizedFormUrlEncodedContent(Flatten(options.PostData).Select(x =>
                        {
                            var value = x.Value;
                            string str = null;
                            str = ToString(value);
                            return new KeyValuePair<string, string>(x.Key, str);
                        }));
                    }
                }
                else if (options.PostString != null && redirectionIndex == 0)
                {
                    requestContent = new StringContent(options.PostString, null, "application/x-www-form-urlencoded");
                }
                else if (message.Method == HttpMethod.Post || message.Method == HttpMethod.Put)
                {
                    requestContent = new StringContent(string.Empty, null, "application/x-www-form-urlencoded");
                }


                string userAgent = null;

                if (options.UserAgent != null)
                {
                    userAgent = options.UserAgent;
#if WEBCLIENT
                    message.UserAgent = userAgent;        
#endif
                }

                if (options.Referrer != null)
                {
#if WEBCLIENT
                    message.Referer = options.Referrer.AbsoluteUri;  
#else
                    message.Headers.Referrer = options.Referrer;
#endif
                }


                if (options.Cookies != null || options.CookiesList != null)
                {
                    var c = options.GetCookieHeader();
                    if (!string.IsNullOrEmpty(c)){
#if WEBCLIENT
                        message.Headers.Add("Cookie", c);
#else
                        message.Headers.TryAddWithoutValidation("Cookie", c);
#endif
                    }
                }

                if (options.ExtraHeaders != null)
                {
                    foreach (var item in options.ExtraHeaders)
                    {
                        var name = item.Key;
#if !WEBCLIENT
                        var namelower = item.Key.ToLowerFast();
                        if (namelower.StartsWith("content-") || namelower == "allow" || namelower == "expires" || namelower == "last-modified")
                        {
                            if (message.Content != null && message.Content.Headers != null)
                            {
                                if (namelower == "content-type") message.Content.Headers.ContentType = null;
                                message.Content.Headers.TryAddWithoutValidation(item.Key, item.Value);
                            }
                        }
                        else if (namelower == "user-agent")
                        {
                            userAgent = item.Value;
                        }
                        else
#endif
                        {
#if WEBCLIENT
                            message.Headers[item.Key] = item.Value;
#else
                            message.Headers.TryAddWithoutValidation(item.Key, item.Value);
#endif
                            
                        }
                    }
                }

#if !WEBCLIENT
                if (userAgent != null)
                {
                    var ua = message.Headers.UserAgent;
                    ua.Clear();
                    try
                    {
                        ua.ParseAdd(userAgent);
                    }
                    catch (FormatException)
                    {
                        if (Compatibility.IsObsoleteMono)
                        {
                            // ignore, it's a bug of mono 3.2.3
                        }
                        else
                        {
                            throw new FormatException("Malformed User-Agent string: " + options.UserAgent);
                        }
                    }

                }
#endif
            }
#if !WEBCLIENT
            message.Content = requestContent;
#endif
            return message;
        }

        private static void SetMultipartContentType(HttpContent cnt)
        {
#if NET35
            cnt.ContentType = "application/octet-stream";
#else
            cnt.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
#endif
        }

        private class StreamWriteContent : HttpContent
        {
            private StreamWriteAction writer;
            public StreamWriteContent(StreamWriteAction writer)
            {
                this.writer = writer;
            }

#if NET35
            internal
#endif
            protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext context)
            {
                return TaskEx.Run(() => writer(stream));
            }

#if NET35
            internal
#endif
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }

#if !SMALL_LIB_AWDEE
#if MINHTTP
        public static Task<HttpResponseMessage> GetAsync2(this Uri url, WebRequestOptions options = null)
#else
        public static Task<HttpResponseMessage> GetAsync(this Uri url, WebRequestOptions options = null)
#endif
        {
            return SendAsync(url, options);
        }
#endif

#if !SMALL_LIB_AWDEE
#if MINHTTP
        public static HttpResponseMessage Get2(this Uri url, WebRequestOptions options = null)
#else
        public static HttpResponseMessage Get(this Uri url, WebRequestOptions options = null)
#endif
        {
            return SendAsync(url, options).Synchronously();
        }
#endif

#if !MINHTTP && !SMALL_LIB_AWDEE
        public static async Task<Stream> GetStreamAsync(this Uri url, WebRequestOptions options = null)
        {
            var response = await SendAsync(url, options);
            return await response.Content.ReadAsStreamAsync();
        }

        public static Stream GetStream(this Uri url, WebRequestOptions options = null)
        {
            return GetStreamAsync(url, options).Synchronously();
        }
#endif
#if !SMALL_LIB_AWDEE
        public static async Task<string> GetStringAsync(this Uri url, WebRequestOptions options = null)
        {

            var response = await SendAsync(url, options);
            if (options != null && options.ResponseEncoding != null)
            {
                response.Content.Headers.ContentType.CharSet = options.ResponseEncoding.WebName;
            }
            if (response.Content.Headers.ContentType != null && response.Content.Headers.ContentType.CharSet == "utf8")
                response.Content.Headers.ContentType.CharSet = "utf-8";
            var result = await response.Content.ReadAsStringAsync();
            CheckResponseText(result, response.Content.Headers.ContentType.MediaType);

            return result;
        }
#endif



#if !SMALL_LIB_AWDEE
        public static string GetString(this Uri url, WebRequestOptions options = null)
        {
            return GetStringAsync(url, options).Synchronously();
        }

        public async static Task<HtmlNode> GetHtmlNodeAsync(this Uri url, WebRequestOptions options = null)
        {
            if (options == null) options = new WebRequestOptions();
            var html = await GetStringAsync(url, options);
            return await Task.Run(() =>
            {
                var node = html.AsHtmlNode();
                node.OwnerDocument.SetPageUrl(options.ResponseUri ?? url);
                return node;
            });
        }


        public async static Task<T> GetJsonAsync<T>(this Uri url, WebRequestOptions options = null)
        {
            return DeserializeJson<T>(await GetStringAsync(url, options));
        }
        public static HtmlNode GetHtmlNode(this Uri url, WebRequestOptions options = null)
        {
            if (options == null) options = new WebRequestOptions();
            var html = GetString(url, options);
            var node = html.AsHtmlNode();
            node.OwnerDocument.SetPageUrl(options.ResponseUri ?? url);
            return node;
        }
#endif

#if !WEBCLIENT
        private class CustomMessageHandler : HttpClientHandler
        {
            public CustomMessageHandler()
            {
                this.AutomaticDecompression =
#if NATIVE_HTTP
                    System.Net.DecompressionMethods.Deflate |
                    System.Net.DecompressionMethods.GZip;
#else
                    System.Net.Reimpl.DecompressionMethods.Deflate |
                    System.Net.Reimpl.DecompressionMethods.GZip;
#endif

                base.AllowAutoRedirect = false;
                base.UseCookies = false;
            }

            protected async override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                try
                {
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (OperationCanceledException ex)
                {
                    throw new TimeoutException("The server did not respond within the allotted time.", ex);
                }
            }

        }

        internal static HttpClient CreateHttpClient()
        {
            var handler = new CustomMessageHandler();
            var httpClient = new HttpClient(handler);

            var acceptLanguage = httpClient.DefaultRequestHeaders.AcceptLanguage;
            var accept = httpClient.DefaultRequestHeaders.Accept;
            var userAgent = httpClient.DefaultRequestHeaders.UserAgent;



            try
            {
                acceptLanguage.Clear();
                acceptLanguage.ParseAdd(DefaultAcceptLanguage);

                accept.Clear();
                accept.ParseAdd(DefaultAccept);

                userAgent.Clear();
                userAgent.ParseAdd(WebRequestOptions.DefaultOptions.UserAgent);
            }
            catch when(Compatibility.IsObsoleteMono)
            {
            }
            httpClient.DefaultRequestHeaders.Add("DNT", "1");
            httpClient.Timeout = TimeSpan.FromMilliseconds(WebRequestOptions.DefaultOptions.Timeout);


            return httpClient;
        }
#endif

#if !SMALL_LIB_AWDEE

        private static T Synchronously<T>(this Task<T> task)
        {
            task.Wait();
            return task.Result;
        }
#endif


    }


}
