using System;

using Shaman.Runtime;
using Shaman.Dom;
#if SALTARELLE
using System.Text.Saltarelle;
#else
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Net;
#endif
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;

namespace Shaman
{
#if STANDALONE
    public static partial class HttpUtils
#else
    public static partial class Utils
#endif
    {
        public readonly static string UriSchemeHttp = "http";
        public readonly static string UriSchemeHttps = "https";
        public readonly static string UriSchemeFile = "file";
        public readonly static string UriSchemeMailto = "mailto";
        public readonly static string UriSchemeFtp = "ftp";

#if !SALTARELLE
        [Configuration]
        internal static readonly string[] Configuration_SessionCookieNames = new[] { "jsessionid", "phpsessid", "aspsessionid", "sid", "zenid", "asp.net_sessionid" };
#endif

        public static Uri GetAbsoluteUri(Uri baseUrl, string relative)
        {
            return GetAbsoluteUriAsString(baseUrl, relative).AsUri();
        }

        public static string GetAbsoluteUriAsString(Uri baseUrl, string relative)
        {
            try
            {
                if (relative == null) return null;
                if (relative.StartsWith("//"))
                {
                    if (baseUrl == null || baseUrl.Scheme != "http")
                        return "https:" + relative;
                    else
                        return "http:" + relative;
                }

                //  if (relative.StartsWith("mailto")) System.Diagnostics.Debugger.Break();
                if (relative.StartsWith("http:") || relative.StartsWith("https:"))
                {
                    if (relative.IndexOf('/', 8) == -1) return relative + "/";
                    return relative;
                }
                var url = GetAbsoluteUrlInternal(baseUrl, relative);
                if (url == null) return null;
                return url.AbsoluteUri;
            }
            catch (FormatException)
            {
#if !SMALL_LIB_AWDEE
                LogNonCriticalException(ex, baseUrl, relative);
#endif
                return relative;
            }
        }


#if !SALTARELLE
        internal static LazyUri GetVaryUrl(LazyUri url,
#if NET35
        IDictionary<string, string> metaParameters,
#else
        IReadOnlyDictionary<string, string> metaParameters,
#endif 
        Credentials credentials)
        {
            var format = metaParameters.TryGetValue("$vary");
            if (format == null) return url;
            url = url.Clone();
            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            var first = true;
            foreach (var item in format.SplitFast(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!first) sb.Append(',');
                first = false;
                string str;
                if (item.StartsWith("cookie-"))
                {
                    str = credentials.GetCookie(item.Substring(7));
                }
                else if (item == "session")
                {
                    str = credentials.GetSessionCookie();
                }
                else if (item == "day")
                {
                    str = DateTime.UtcNow.ToString("yyyyMMdd");
                }
                else if (item == "month")
                {
                    str = DateTime.UtcNow.ToString("yyyyMM");
                }
                else if (item == "year")
                {
                    str = DateTime.UtcNow.ToString("yyyy");
                }
                else if (item == "hour")
                {
                    str = DateTime.UtcNow.ToString("yyyyMMddHH");
                }
                else if (item == "minute")
                {
                    str = DateTime.UtcNow.ToString("yyyyMMddHHmm");
                }
                else if (item == "second")
                {
                    str = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                }
                else
                {
                    throw new ArgumentException("Unrecognized $vary parameter value: " + item);
                }
                sb.Append(str);

            }
            var varyDaya = ReseekableStringBuilder.GetValueAndRelease(sb);
            url.RemoveFragmentParameter("$vary");
            url.AppendFragmentParameter("$vary", varyDaya);
            return url;
        }
#endif

        internal static Uri GetAbsoluteUrlInternal(Uri baseUrl, string relative)
        {

            if (relative.StartsWith("http:") || relative.StartsWith("https:"))
                return new Uri(relative);

            if (relative.StartsWith("www."))
                return new Uri("http://" + relative);

            if (relative.StartsWith("//"))
                return new Uri((baseUrl != null ? baseUrl.Scheme : "https") + ":" + relative);

            var firstColon = relative.IndexOf(':', 0, Math.Min(relative.Length, 15));
            var firstSlash = relative.IndexOf('/');
            var firstQuestionMark = relative.IndexOf('?');
            if (firstColon != -1 && (firstSlash == -1 || firstColon < firstSlash) && (firstQuestionMark == -1 || firstColon < firstQuestionMark))
                return new Uri(relative);

            if (baseUrl == null) throw new ArgumentException("Cannot create an absolute Uri without a base Uri.");

            return new Uri(baseUrl, relative);

        }

#if !SALTARELLE
        public static JsonTextReader CreateJsonReader(string json)
        {
            return CreateJsonReader(new StringReader(json));
        }
        public static JsonTextReader CreateJsonReader(TextReader textReader)
        {
            var reader = new JsonTextReader(textReader);
            reader.DateParseHandling = DateParseHandling.None;
            reader.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
            return reader;
        }

        public static string ParametersToQueryString(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return ParametersToString(parameters, '?');
        }

        public static string ParametersToFragmentString(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return ParametersToString(parameters, '#');
        }

        public static string ParametersToString(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            return ParametersToString(parameters, char.MinValue);
        }



#if STANDALONE
        internal static Task<PageExecutionResults> GetJavascriptProcessedPageAsync(LazyUri originalUrl, LazyUri url,
#if NET35
        IDictionary<string, string> metaParameters
#else
        IReadOnlyDictionary<string, string> metaParameters
#endif
         )
        {
            throw new NotSupportedException("$js preprocessing is not supported in the standalone Shaman.Http library.");
        }
#endif

        [RestrictedAccess]
        [AllowNumericLiterals]
        public static bool LooksLikeHtml(LazyTextReader lazy)
        {
            var initialGarbage = 0;
            int i = 0;
            for (; lazy.ContainsIndex(i); i++)
            {
                var ch = lazy[i];
                if (ch == (char)0xfeff) continue;
                if (char.IsWhiteSpace(ch)) continue;
                if (ch == '<') return true;
                if (ch == '{' || ch == '[' || ch == '"' || ch == '\'' || ch == '(') break;
                initialGarbage++;
                if (initialGarbage == 5) break;
            }
            if (!lazy.ContainsIndex(i) && initialGarbage == 0) return true; // Empty document
            return false;
        }

        internal static string GetSuggestedFileName(Uri Url, string contentDispositionFileName, string contentTypeExtension)
        {
            const string FileNameRegex = @"/?([^/]+\.[^/\.]{1,8})/?$";

            string name = null;

            if (contentDispositionFileName != null)
            {
                name = contentDispositionFileName;
            }

            if (name == null)
            {
                name = Url.LocalPath.TryCapture(FileNameRegex);
                if (name != null)
                {
                    try
                    {
                        name = UnescapeDataString(name);
                        var urlExtension = Path.GetExtension(name);
                        if (urlExtension == null || urlExtension.In(Configuration_PageExtensions))
                            if (Configuration_DownloadWrapperNames.Any(x => name.Contains(x)))
                                name = null;
                    }
                    catch (ArgumentException)
                    {
                        name = null;
                    }
                }
            }

            if (name == null)
                name = Url.GetQueryParameters().Select(x => x.Value.TryCapture(FileNameRegex)).FirstOrDefault(x => x != null);

            if (name == null)
                return null;


            var extension = contentTypeExtension;

            if (extension != null)
            {
                if (name != null) name = Path.GetFileNameWithoutExtension(name) + extension;
                else name = name + extension;
            }

            return name;
        }

        [Configuration]
        private static string[] Configuration_PageExtensions = new[] { ".php", ".asp", ".aspx", ".mspx", ".htm", ".html", ".jsp", ".do" };


        [Configuration]
        private static string[] Configuration_DownloadWrapperNames = new[] { "download", "action", "torrent", "get_file", "getfile", "file.php" };

        public const WebExceptionStatus WebAuthenticationRequested = (WebExceptionStatus)0x7FFF0000;
        public const WebExceptionStatus RedirectLoopDetected = (WebExceptionStatus)0x7FFF0001;
        public const WebExceptionStatus UnexpectedRedirect = (WebExceptionStatus)0x7FFF0002;
        public const WebExceptionStatus MaximumNumberOfRedirectsExceeded = (WebExceptionStatus)0x7FFF0003;
        public const WebExceptionStatus RequestProhibitedByProxy = (WebExceptionStatus)20;
        public const WebExceptionStatus UnexpectedResponseType = (WebExceptionStatus)0x7FFF0004;
        public const WebExceptionStatus UnknownError = (WebExceptionStatus)0x7FFF0005;

        public static void AppendQueryParameters(IEnumerable<KeyValuePair<string, string>> parameters, StringBuilder sb)
        {
            if (sb.IndexOf('#') != -1)
            {
                Sanity.ShouldntHaveHappened();
            }
            AppendParameters(parameters, sb, '?');
        }

        public static void AppendFragmentParameter(string name, string value, ref Uri url)
        {
            var sb = new StringBuilder(url.AbsoluteUri);
            AppendFragmentParameter(name, value, sb);
            url = sb.ToString().AsUri();
        }

        public static void AppendFragmentParameter(string name, string value, StringBuilder sb)
        {
            AppendFragmentParameters(new[] { new KeyValuePair<string, string>(name, value) }, sb);
        }
        public static void AppendQueryParameter(string name, string value, StringBuilder sb)
        {
            AppendQueryParameters(new[] { new KeyValuePair<string, string>(name, value) }, sb);
        }

        public static void AppendFragmentParameters(IEnumerable<KeyValuePair<string, string>> parameters, StringBuilder sb)
        {
            AppendParameters(parameters, sb, '#');
        }

        public static void AppendParameters(IEnumerable<KeyValuePair<string, string>> parameters, StringBuilder sb)
        {
            AppendParameters(parameters, sb, char.MinValue);
        }

        private static string ParametersToString(IEnumerable<KeyValuePair<string, string>> parameters, char initialChar)
        {
            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            AppendParameters(parameters, sb, initialChar);
            return ReseekableStringBuilder.GetValueAndRelease(sb);
        }

        private static void AppendParameters(IEnumerable<KeyValuePair<string, string>> parameters, StringBuilder sb, char initialChar)
        {
            var first = initialChar == '\0' ? true : sb.IndexOf(initialChar) == -1;
            foreach (var item in parameters)
            {
                if (first)
                {
                    if (initialChar != '\0') sb.Append(initialChar);
                    first = false;
                }
                else
                {
                    sb.Append('&');
                }
                var key = item.Key;
                if (key.Length != 0 && key[0] == '$')
                {
                    sb.Append('$');
                    sb.AppendUriEncoded(key.Substring(1));
                }
                else
                {
                    sb.AppendUriEncoded(key);
                }
                sb.Append('=');
                sb.AppendUriEncoded(item.Value);
            }
        }
#endif
        public static IEnumerable<KeyValuePair<string, string>> GetParameters(string query)
        {
            var i = 0;

            while (query.Length > i && (query[i] == '?' || query[i] == '#'))
                i++;

            var endd = query.IndexOf('#', i);
            if (endd == -1) endd = query.Length;

            while (i < endd)
            {

                var end = query.IndexOf('&', i, endd - i);
                if (end == -1) end = endd;

                var eq = query.IndexOf('=', i, end - i);
                string name;
                string value;

                // ?&first=
                if (i + 1 != end)
                {

                    if (eq != -1)
                    {
                        name = query.SubstringCached(i, eq - i);
                        value = UnescapeDataString(query.SubstringCached(eq + 1, end - eq - 1));
                    }
                    else
                    {
                        name = query.SubstringCached(i, end - i);
                        value = string.Empty;
                    }
                    name = UnescapeDataString(name);
                    yield return new KeyValuePair<string, string>(name, value);
                }
                i = end + 1;

            }
        }
#if !SALTARELLE
        internal static void AppendParameters(IEnumerable<KeyValuePair<string, string>> parameters, NakedStringBuilder sb, char initialChar)
        {
            var first = initialChar == '\0' ? true : sb.IndexOf(initialChar) == -1;
            foreach (var item in parameters)
            {
                if (first)
                {
                    if (initialChar != '\0') sb.Append(initialChar);
                    first = false;
                }
                else
                {
                    sb.Append('&');
                }
                var key = item.Key;
                if (key.Length != 0 && key[0] == '$')
                {
                    sb.Append('$');
                    sb.AppendUriEncoded(key.Substring(1));
                }
                else
                {
                    sb.AppendUriEncoded(key);
                }
                sb.Append('=');
                sb.AppendUriEncoded(item.Value);
            }
        }












        public static Uri FormatEscaped(string url, params object[] parameters)
        {
            return FormatEscapedData(url, parameters).AsUri();
        }

        public static string FormatEscapedData(string data, params object[] parameters)
        {
            var escaped = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                escaped[i] = EscapeDataString(Convert.ToString(parameters[i]));
            }
            return string.Format(data, escaped);
        }




#endif

        public static string EscapeDataString(string stringToEscape)
        {
            return Uri.EscapeDataString(stringToEscape).Replace("%20", "+");
        }

        public static string UnescapeDataString(string stringToUnescape)
        {
            return Uri.UnescapeDataString(stringToUnescape.Replace('+', ' '));
        }



        internal static void MakeAbsoluteAttribute(HtmlNode node, string attribute, Uri pageUrl)
        {
            var value = node.GetAttributeValue(attribute);
            if (value == null) return;

            node.SetAttributeValue(attribute, GetAbsoluteUriAsString(pageUrl, value));


        }

#if !SALTARELLE

        public static Task<HtmlNode> ClickFormButtonAsync(this HtmlNode button,
#if NET35
            IList<KeyValuePair<string, string>> parameters
#else
            IReadOnlyList<KeyValuePair<string, string>> parameters
#endif
            )
        {
            return ClickFormButtonAsync(button, null, parameters, null);
        }

        public static async Task<HtmlNode> ClickFormButtonAsync(this HtmlNode button, WebRequestOptions options = null,
#if NET35
        IList<KeyValuePair<string, string>> parameters = null,
#else
        IReadOnlyList<KeyValuePair<string, string>> parameters = null,
#endif
        Shaman.Runtime.CookieContainer cookies = null
)
        {
            if (options == null) options = new WebRequestOptions();
            var t = SetUpOptionsFromFormButton(button, options, parameters);
            return t.Processor(await t.Url.GetHtmlNodeAsync(options, cookies));
        }



        public class SetUpOptionsFromFormInfo
        {
            public LazyUri Url;
            public Func<HtmlNode, HtmlNode> Processor;
        }

        public static SetUpOptionsFromFormInfo SetUpOptionsFromFormButton(HtmlNode button, WebRequestOptions options,
#if NET35
        IList<KeyValuePair<string, string>> parameters = null
#else
        IReadOnlyList<KeyValuePair<string, string>> parameters = null
#endif
        )
        {
            JObject a4joptions = null;
            HtmlNode form = null;
            var onclick = button.GetAttributeValue("onclick");
            if (onclick != null && onclick.Contains(a4jPrefix))
            {
                var formname = onclick.Capture(@"A4J\.AJAX\.Submit\('(.*?)'");
                form = button.OwnerDocument.DocumentNode.DescendantsAndSelf().First(x => x.Id == formname);
                a4joptions = (JObject)ReadJsonToken(onclick, onclick.IndexOf(",{", onclick.IndexOf(a4jPrefix)) + 1);
            }
            else
            {
                form = button.AncestorsAndSelf().FirstOrDefault(x => x.TagName == "form");
                if (form == null) throw new ArgumentException("The specified button is not contained within a form.");
            }

#if SALTARELLE && false
            var method = form.GetAttributeValue("method");
            var post = method != null && method.ToUpper() == "POST";
#else
            var post = "POST".Equals(form.GetAttributeValue("method"), StringComparison.OrdinalIgnoreCase);
#endif

            List<string> parametersList = new List<string>();

            if (form.TagName != "form")
            {
                form = form.Descendants().First(x => x.TagName == "form");
            }




            var url = new LazyUri(GetAbsoluteUriAsString(form.OwnerDocument.BaseUrl, (a4joptions != null ? a4joptions.Value<string>("actionUrl") : null) ?? form.GetAttributeValue("action")));


            if (options != null)
            {
                if (options.AdditionalQueryParameters != null) parametersList.AddRange(options.AdditionalQueryParameters.Select(x => x.Key));
                if (options.PostData != null) parametersList.AddRange(options.PostData.Select(x => x.Key));
            }




            Action<string, string> add;
            if (post)
                add = (k, v) =>
                {
                    if (!parametersList.Contains(k))
                    {
                        parametersList.Add(k);
                        url.AppendFragmentParameter("$post-" + k, v);
                    }
                };
            else
                add = (k, v) =>
                {
                    if (!parametersList.Contains(k))
                    {
                        parametersList.Add(k);
                        url.AppendQueryParameter(k, v);
                    }
                };

            if (a4joptions != null)
            {
                var containerIdOrDefault = a4joptions.Value<string>("containerId") ?? "_viewRoot";
                add("AJAXREQUEST", containerIdOrDefault);
                var name = button.GetAttributeValue("name") ?? button.Id;
                if (name != null) add(name, name);
            }
            else
            {
                PopulateSubmitButtonParameter(button, add);
            }


            if (parameters != null)
            {

                foreach (var item in parameters)
                {
                    add(item.Key, item.Value);
                }

                parametersList.AddRange(parameters.Select(x => x.Key));

            }

            foreach (var item in form.DescendantsAndSelf())
            {


                PopulateFormParameters(item, x =>
                {

                    if (a4joptions != null)
                    {
                        var f = GetA4jField(button, x.GetAttributeValue("name") ?? x.Id, a4joptions);
                        if (f == null) return null;
                        if (f == false) return string.Empty;
                    }
                    var type = x.GetAttributeValue("type");
                    if (x.TagName == "textarea")
                    {
                        return x.InnerText;
                    }
                    else if (type == "radio" || type == "checkbox")
                    {
                        return x.GetAttributeValue("checked") != null ? x.GetAttributeValue("value") ?? "on" : null;
                    }
                    else if (x.TagName == "option")
                    {
                        return x.GetAttributeValue("checked") != null ? x.GetAttributeValue("value") ?? x.GetText() : null;
                    }
                    else
                    {
                        var k = x.GetAttributeValue("value");
                        if (k == null && x.TagName == "input") return string.Empty;
                        return k;
                    }
                }, add);
            }

            string originalDocumentHtml = null;
            if (a4joptions != null)
            {
                var sw = new StringWriter();
                button.OwnerDocument.WriteTo(sw, false);
                originalDocumentHtml = sw.ToString();
                var p = (JObject)a4joptions["parameters"];
                if (p != null)
                {
                    foreach (var prop in p.Properties())
                    {
                        add(prop.Name, Convert.ToString(prop.Value, CultureInfo.InvariantCulture));
                    }
                }
            }



            return new SetUpOptionsFromFormInfo() { Url = url, Processor = a4joptions != null ? MakeA4jProcessor(originalDocumentHtml, a4joptions.Value<string>("org.ajax4jsf.portlet.NAMESPACE"), button.OwnerDocument.DocumentNode.Attributes.ToDictionary(x => x.OriginalName, x => x.Value)) : new Func<HtmlNode, HtmlNode>(x => x) };

        }

        private static Func<HtmlNode, HtmlNode> MakeA4jProcessor(string originalDocumentHtml, string ns, Dictionary<string, string> documentAttributes)
        {
            return x =>
            {


                var ajaxResponse = x.TryGetValue(":property('Ajax-Response')") ?? x.GetAttributeValue("header-ajax-response");
                var expiredMsg = x.TryGetValue(":property('Ajax-Expired')") ?? x.GetAttributeValue("header-ajax-expired");
                if (expiredMsg != null) throw new Exception("The stateful page is expired: " + expiredMsg);
                if (ajaxResponse == "true")
                {
                    var d = new HtmlDocument();

                    d.LoadHtml(originalDocumentHtml);
                    var docnode = d.DocumentNode;
                    foreach (var attr in documentAttributes)
                    {
                        docnode.SetAttributeValue(attr.Key, attr.Value);
                    }
                    var idsFromResponse = x.TryGetValue(":property('Ajax-Update-Ids')") ?? x.GetAttributeValue("header-Ajax-Update-Ids");
                    if (!string.IsNullOrEmpty(idsFromResponse))
                    {
                        var ids = idsFromResponse.SplitFast(',');
                        foreach (var id in ids)
                        {
                            var newnode = x.DescendantsAndSelf().First(y => y.Id == id);
                            var old = docnode.DescendantsAndSelf().FirstOrDefault(y => y.Id == id);
                            if (old != null)
                            {
                                var parent = old.ParentNode;
                                var idx = parent.ChildNodes.IndexOf(old);
                                parent.RemoveChild(old);
                                parent.ChildNodes.Insert(idx, newnode);
                                d.SetOwnerDocumentRecursive(newnode);
                            }
                        }
                    }





                    var idsSpan = x.DescendantsAndSelf().FirstOrDefault(y => y.Id == "ajax-view-state");
                    // LOG.debug("Hidden JSF state fields: " + idsSpan);
                    if (idsSpan != null)
                    {

                        // LOG.debug("Namespace for hidden view-state input fields is " + namespace);
                        var anchor = ns != null ? docnode.DescendantsAndSelf().First(y => y.Id == ns) : docnode;
                        var inputs = anchor.DescendantsAndSelf("input").ToList();

                        var newinputs = idsSpan.DescendantsAndSelf("input").ToList();
                        //A4J.AJAX.replaceViewState(inputs, newinputs);
                        foreach (var newinput in newinputs)
                        {
                            foreach (var oldinput in inputs)
                            {
                                if (newinput.GetAttributeValue("name") == oldinput.GetAttributeValue("name"))
                                {
                                    oldinput.SetAttributeValue("value", newinput.GetAttributeValue("value"));
                                }
                            }
                        }


                    }

                    return docnode;
                }
                return x;
            };
        }


#if STANDALONE
        static HttpUtils()
        {
            FizzlerCustomSelectors.RegisterAll();
        }
#endif


        const string a4jPrefix = "A4J.AJAX.Submit(";

        [RestrictedAccess]
        public static void PopulateSubmitButtonParameter(HtmlNode button, Action<string, string> add)
        {
            var onclick = button.GetAttributeValue("onclick");

            var name = button.GetAttributeValue("name");

            var href = button.GetAttributeValue("href");
            const string javascriptDoPostBack = "javascript:__doPostBack(";
            if (href != null && href.StartsWith(javascriptDoPostBack))
            {
                var modified = "[" + href.Substring(javascriptDoPostBack.Length, href.Length - javascriptDoPostBack.Length - 1) + "]";
                var arr = (JArray)ReadJsonToken(modified);
                

                add("__EVENTTARGET", arr[0].Value<string>());
                add("__EVENTARGUMENT", arr[1].Value<string>());
                
                name = null;
            }
            else if (onclick != null)
            {

                if (onclick.StartsWith("if(typeof jsfcljs == 'function')"))
                {
                    var idx = onclick.IndexOf(',');
                    if (idx != -1)
                    {

                        using (var reader = new FizzlerCustomSelectors.PartialStringReader(onclick, idx + 1))
                        using (var jr = CreateJsonReader(reader))
                        {
                            var str = jr.ReadAsString();
                            if (str == null) throw new ExtractionException(message: "Unexpected format of JSF CommandLink onclick handler.");
                            var arr = str.SplitFast(',');
                            for (int i = 0; i < arr.Length / 2; i++)
                            {
                                add(arr[i * 2], arr[i * 2] + 1);
                            }
                        }
                        name = null;
                    }
                }
            }

            if (name != null)
            {
                add(name, button.GetAttributeValue("value") ?? string.Empty);
            }

        }

        private static bool? GetA4jField(HtmlNode element, string name, JObject a4joptions)
        {
            var hiddenOnly = a4joptions.Value<bool?>("single").GetValueOrDefault(false);
            if (hiddenOnly && element.GetAttributeValue("type") != "hidden") return null;
            if (name.EndsWith("_link_hidden_") || name.EndsWith("_idcl")) return null;
            return true;
        }

        public static bool UrisEqual(LazyUri a, LazyUri b)
        {
            if (a == null || b == null) return (a == null) == (b == null);
            return a.AbsoluteUri == b.AbsoluteUri;
        }
#endif

        public static bool UrisEqual(Uri a, Uri b)
        {
            if (object.ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.AbsoluteUri == b.AbsoluteUri;
        }

#if !SALTARELLE
        [RestrictedAccess]
#endif
        public static void PopulateFormParameters(HtmlNode field, Func<HtmlNode, string> getValue, Action<string, string> add)
        {

            var fieldName = field.GetAttributeValue("name");
            if (fieldName != null)
            {
                var type = field.GetAttributeValue("type");
                if (type != null) type = type.ToLowerFast();
                string value = null;

                if (type == "reset") return;
                if (type == "image" || type == "button" || type == "submit" || field.TagName == "button") return;


                if (field.TagName == "select")
                {
                    var c = field.Descendants("option");
                    HtmlNode frst = null;
                    var any = false;
                    foreach (var option in c)
                    {
                        if (frst == null) frst = option;
                        var v = getValue(option);
                        if (v != null)
                        {
                            add(fieldName, v);
                            any = true;
                        }
                    }
                    if (!any && frst != null && field.GetAttributeValue("multiple") == null)
                    {
                        add(fieldName, getValue(frst) ?? string.Empty);
                    }
                    fieldName = null;
                }
                else
                {
                    value = getValue(field);
                }


                if (fieldName != null && value != null)
                {
                    add(fieldName, value);
                }





            }

        }



        internal static bool IsGenericContentType(string mime)
        {
            return string.IsNullOrEmpty(mime) ||
                mime == "application/octet-stream" ||
                mime == "application/download" ||
                mime == "application/force-download";
        }

        public static string GetFileExtensionFromMime(string mime)
        {
            if (IsGenericContentType(mime)) return null;


#if !DESKTOP || CORECLR || true
#if STANDALONE
            HttpExtensionMethods.InitializeMimesDictionary();
            return HttpExtensionMethods.mimeToExtension.TryGetValue(mime);
#else
            ExtensionMethods.InitializeMimesDictionary();
            return ExtensionMethods.mimeToExtension.TryGetValue(mime);
#endif
#else
            lock (mimeCache)
            {
                string extension;
                if (!mimeCache.TryGetValue(mime, out extension))
                {
                    extension = (string)Registry.GetValue(@"HKEY_CLASSES_ROOT\MIME\Database\Content Type\" + mime, "Extension", null);
                    if (string.IsNullOrEmpty(extension)) extension = null;
                    if (extension == null)
                    {
                        Debug.WriteLine("No extension found for mime type " + mime);
                    }
                    mimeCache.Add(mime, extension);

                }
                return extension;
            }
#endif

        }



#if NET35
        public static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
            {
                return true;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static char[] HeaderValueTrimChars = new[]{'"', ' '};
        internal static string GetCharSetFromContentType(string contentTypeHeader)
        {
            // todo replace utf8 -> utf-8
            if(string.IsNullOrEmpty(contentTypeHeader)) return null;
            var p = contentTypeHeader.IndexOf("charset=");
            if(p == -1) return null;
            var end = contentTypeHeader.IndexOf(';', p);
            if(end==-1)end = contentTypeHeader.Length;
            var a = p + "charset=".Length;
            var charset = contentTypeHeader.Substring(a, end - a).Trim(HeaderValueTrimChars);
            if(charset.Equals("utf8", StringComparison.OrdinalIgnoreCase)) return "utf-8";
            return charset;
            
        }
        
        internal static string GetMimeFromContentType(string contentTypeHeader)
        {
            if(string.IsNullOrEmpty(contentTypeHeader)) return null;
            var p = contentTypeHeader.IndexOf(';');
            if(p==-1) return contentTypeHeader;
            return contentTypeHeader.Substring(0, p);
            
        }
#endif

        public static HtmlNode GetPreviousElementSibling(this HtmlNode node)
        {
            node = node.PreviousSibling;
            while (node != null && node.NodeType != HtmlNodeType.Element)
            {
                node = node.PreviousSibling;
            }
            return node;
        }
        public static HtmlNode GetNextElementSibling(this HtmlNode node)
        {
            node = node.NextSibling;
            while (node != null && node.NodeType != HtmlNodeType.Element)
            {
                node = node.NextSibling;
            }
            return node;
        }

        public static HtmlNode GetFirstElementChild(this HtmlNode node)
        {
            node = node.FirstChild;
            while (node != null && node.NodeType != HtmlNodeType.Element)
            {
                node = node.NextSibling;
            }
            return node;
        }



        public static JToken ReadJsonToken(string source)
        {
            return ReadJsonToken(source, 0);
        }


        private static bool jsonXescapeRecentlyHappened;

        public static JToken ReadJsonToken(string source, int startIndex)
        {

            if (!jsonXescapeRecentlyHappened || source.IndexOf(@"\x", startIndex) == -1)
            {
                using (var jsonReader = CreateJsonReader(new FizzlerCustomSelectors.PartialStringReader(source, startIndex)))
                {
                    try
                    {
                        return JToken.Load(jsonReader);
                    }
                    catch (Newtonsoft.Json.JsonReaderException ex) when(ex.Message.StartsWith(@"Bad JSON escape sequence: \x"))
                    {
                    }
                }
            }

            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();

            for (int i = startIndex; i < source.Length; i++)
            {
                var ch = source[i];
                if (ch == 'x' && i != startIndex && source[i - 1] == '\\' && IsPreceededByOddNumberOfBackslashes(source, i, startIndex))
                {
                    sb.Append('u');
                    sb.Append('0');
                    sb.Append('0');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            var fixedjson = ReseekableStringBuilder.GetValueAndRelease(sb);
            using (var jsonReader = CreateJsonReader(fixedjson))
            {
                return JToken.Load(jsonReader);
            }
        }

        private static bool IsPreceededByOddNumberOfBackslashes(string source, int pos, int startIndex)
        {
            int i = pos - 1;
            for (; i >= startIndex; i--)
            {
                if (source[i] != '\\') break;
            }
            return (pos - i) % 2 == 0;

        }
    }


}