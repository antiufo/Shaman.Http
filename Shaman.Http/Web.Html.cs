using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shaman.Dom;
using Fizzler.Systems.HtmlAgilityPack;
using System.IO;
#if !NO_ASYNC
using System.Threading.Tasks;
#endif
#if NETFX_CORE
using System.Net.Http;
#endif
#if !SALTARELLE
#if NATIVE_HTTP
using System.Net;
#else
using System.Net.Reimpl;
#endif
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Collections;
using Newtonsoft.Json.Linq;
using WebExceptionStatus = System.Net.WebExceptionStatus;
#endif

#if SALTARELLE
using StringBuilder = System.Text.Saltarelle.StringBuilder;
#endif

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


        public static string GetPlainText(this HtmlDocument plainTextDocument)
        {
            var c = (HtmlTextNode)plainTextDocument.DocumentNode.ChildNodes.SingleOrDefault();
            return c != null ? c.Text : null;
        }
        public static HtmlNode AppendTextNode(this HtmlNode parent, string text)
        {
            var el = parent.OwnerDocument.CreateTextNode(text);
            parent.AppendChild(el);
            return el;
        }

        private static string GetValue(this HtmlNode node, string selector, string attribute, string pattern, bool optional)
        {
            if (node == null) throw new ArgumentNullException();

            var child = selector != null ? node.FindSingle(selector) : node;
            if (child != null)
            {
                var attributeValue = attribute != null ? child.GetAttributeValue(attribute) : child.GetText();
                if (!string.IsNullOrEmpty(attributeValue))
                {
                    var result = pattern != null ? attributeValue.TryCapture(pattern) : attributeValue;
                    if (!string.IsNullOrEmpty(result))
                    {
                        return result;
                    }
                }
            }



            if (optional) return null;


            throw new UnparsableDataException() { SourceDataNode = node, NodeQuery = selector, Attribute = attribute, Regex = pattern };

        }




        public static string GetValue(this HtmlNode node, string selector = null, string attribute = null, string pattern = null)
        {
            return GetValue(node, selector, attribute, pattern, false);
        }

        public static string TryGetValue(this HtmlNode node, string selector = null, string attribute = null, string pattern = null)
        {
            return GetValue(node, selector, attribute, pattern, true);
        }



        public static IEnumerable<HtmlNode> GetSiblingElements(this HtmlNode node)
        {
            if (node.ParentNode == null) return new[] { node };

            IEnumerable<HtmlNode> siblings = node.ParentNode.ChildNodes;

            siblings = siblings.Where(x => x.NodeType == HtmlNodeType.Element);

            return siblings;
        }

#if !SMALL_LIB_AWDEE

        public static Action<Exception, object[]> NonCriticalExceptionLogger { get; set; }


        private static void LogNonCriticalException(Exception exception, params object[] parameters)
        {
            var logger = NonCriticalExceptionLogger;
            if (logger != null)
                logger(exception, parameters);
        }
#endif


#if !SALTARELLE
#if !SMALL_LIB_AWDEE
        public static readonly Encoding DefaultEncoding = Encoding.UTF8;


        public static Encoding GuessEncoding(this WebResponse response)
        {
            return GuessEncoding(response.ContentType);
        }
#endif

#if NETFX_CORE
        public static Encoding GuessEncoding(this HttpResponseMessage response)
        {
            // HACK always return UTF8
            return Encoding.UTF8;
            // dafaq, where is the Content-Type header?
            // return GuessEncoding(response.Headers.content);
        }
#endif

#if !SMALL_LIB_AWDEE
        private static Encoding GuessEncoding(string contentType)
        {
            try
            {
                if ((contentType == null))
                {
                    return null;
                }
                string[] strArray =
#if NETFX_CORE
 contentType.ToLowerInvariant()
#else
 contentType.ToLower(System.Globalization.CultureInfo.InvariantCulture)
#endif


.Split(new char[] {
					';',
					'=',
					' '
				});
                bool flag = false;
                string str2 = null;
                foreach (string str2_loopVariable in strArray)
                {
                    str2 = str2_loopVariable;
                    if ((str2 == "charset"))
                    {
                        flag = true;
                    }
                    else if (flag)
                    {
                        if (str2 == "utf8") str2 = "utf-8";
                        return Encoding.GetEncoding(str2);
                    }
                }
            }
            catch (Exception ex)
            {
                LogNonCriticalException(ex, contentType);
            }
            return null;
        }
#endif
#endif

#if !SALTARELLE

        private static string DefaultAccept = "text/html,application/xhtml+xml,*/*";
        private static string DefaultAcceptLanguage = "en-US";


#endif


        internal static string ToString(object value)
        {
#if SALTARELLE
            return value != null ? value.ToString() : string.Empty;
#else
            return Convert.ToString(value, CultureInfo.InvariantCulture);
#endif
        }


#if !SALTARELLE
     



        private static LazyUri MaybeAddAdditionalQueryParameters(LazyUri url, WebRequestOptions options)
        {
            bool cloned = false;
            if (options != null && options.AdditionalQueryParameters != null)
            {

                foreach (var item in Flatten(options.AdditionalQueryParameters))
                {
                    if (!cloned)
                    {
                        cloned = true;
                        url = url.Clone();
                    }
                    url.AppendQueryParameter(item.Key, ToString(item.Value));
                }

            }
            return url;
        }

        private static IEnumerable<KeyValuePair<string, object>> Flatten(IList<KeyValuePair<string, object>> list)
        {
            foreach (var item in list)
            {
                var sublist = item.Value as IList;
                if (sublist != null)
                {
                    foreach (var subitem in sublist)
                    {
                        yield return new KeyValuePair<string, object>(item.Key, subitem);
                    }
                }
                else
                {
                    yield return item;
                }
            }
        }


#endif




#if !SALTARELLE
#if !SMALL_LIB_AWDEE
        public static T GetJson<T>(this Uri url, WebRequestOptions options = null)
        {
            return DeserializeJson<T>(url.GetString(options));
        }

        public static T DeserializeJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(CleanupJsonp(json));
        }
#endif


#endif


#if !SALTARELLE
        [Configuration]
        private static int Configuration_MaximumNumberOfRedirects = 7;


        public static JToken TryGetJToken(this JObject dict, string key)
        {

            JToken value;
            if (dict.TryGetValue(key, out value))
            {
                return value;
            }
            else
            {
                return null;
            }
        }
#endif

        public static string WriteTo(this HtmlDocument document)
        {
            var sw = new StringWriter();
            document.WriteTo(sw, false);
            return sw.ToString();
        }

        public static int TryGetNumericAttributeValue(this HtmlNode node, string name, int @default)
        {
            var val = node.GetAttributeValue(name);
            int num;
            if (val == null || !int.TryParse(val, out num)) return @default;
            return num;
        }

    }

    public delegate void CheckResponseEventHandler(string requestedHost, string responseHost);

}
