using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Net;
#if NETFX_CORE
using Windows.Storage;
#endif
#if !STANDALONE
using HttpExtensionMethods = Shaman.ExtensionMethods;
#endif

using System.Collections;
using Shaman.Runtime;
using Shaman.Dom;

#if SMALL_LIB_AWDEE
namespace Shaman
#else
namespace Xamasoft
#endif
{
    public class WebRequestOptions
    {



        private readonly bool IsDefault;

        public string UserAgent { get; set; }
        public int Timeout { get; set; }
        public int? TimeoutStartSecondRetrial { get; set; }
        public int? TimeoutSecondRetrialAfterError { get; set; }

        public TimeSpan WaitBefore { get; set; }

        public Action<HtmlNode, Uri, Exception> HtmlRetrieved;

        private bool _allowRedirects = true;
        public bool AllowRedirects
        {
            get
            {
                return _allowRedirects;
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _allowRedirects = value;
            }
        }



        private string _cookies;
        public string Cookies
        {
            get
            {
                return _cookies;
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _cookies = value;
            }
        }

#if !SALTARELLE
        private Encoding _responseEncoding;
        public Encoding ResponseEncoding
        {
            get
            {
                return _responseEncoding;
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _responseEncoding = value;
            }
        }

#endif


        public PageExecutionResults PageExecutionResults { [RestrictedAccess] get; [RestrictedAccess] set; }

        private Uri _referrer;
        public Uri Referrer
        {
            get
            {
                return _referrer;
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _referrer = value;
            }
        }



        private IList<KeyValuePair<string, object>> _postData;
        public IList<KeyValuePair<string, object>> PostData
        {
            get
            {
                return _postData;
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _postData = value;
            }
        }


        private string _postString;
        public string PostString
        {
            get
            {
                return _postString;
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _postString = value;
            }
        }


        private WebRequestOptions(bool isDefault)
        {
            this.IsDefault = isDefault;
            if (isDefault)
            {
                this.Timeout = 20000;
                this.TimeoutSecondRetrialAfterError = 8000;
                this.TimeoutStartSecondRetrial = 5000;
                //this.UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64; rv:47.0) Gecko/20100101 Firefox/47.0";
                //this.UserAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64; Trident/7.0; rv:11.0) like Gecko";
                this.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:57.0) Gecko/20100101 Firefox/57.0";
            }
            else
            {
                this.Timeout = defaultOptions.Timeout;
                this.TimeoutSecondRetrialAfterError = defaultOptions.TimeoutSecondRetrialAfterError;
                this.TimeoutStartSecondRetrial = defaultOptions.TimeoutStartSecondRetrial;
                this.UserAgent = defaultOptions.UserAgent;
            }
        }

        public WebRequestOptions()
            : this(false)
        {

        }


        public bool AllowCachingEvenWithCustomRequestOptions;


#if !NET35
        public System.Net.Http.HttpClient CustomHttpClient;
#endif

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static WebRequestOptions defaultOptions = new WebRequestOptions(true);
        public static WebRequestOptions DefaultOptions
        {
            get
            {
                return defaultOptions;
            }
        }


        public WebRequestOptions AddPostField(string name, object value)
        {
            return AddPostField(name, value, -1);
        }

        internal WebRequestOptions AddPostField(string name, object value, int insertionIndex)
        {
            if (_postData == null)
            {
                if (IsDefault) throw new InvalidOperationException();
                _postData = new List<KeyValuePair<string, object>>();
            }
            var tuple = new KeyValuePair<string, object>(name, value);
            if (insertionIndex != -1) _postData.Insert(insertionIndex, tuple);
            else _postData.Add(tuple);
            return this;
        }


        internal List<PriorityCookie> CookiesList = new List<PriorityCookie>();
        internal List<KeyValuePair<string, string>> ExtraHeaders;

        internal string _method;
        public string Method
        {
            get
            {
                return _method ?? ((_postData != null || _postString != null) ? "POST" : "GET");
            }
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                _method = value;
            }
        }

        public WebRequestOptions AddHeader(string name, string value)
        {
            if (ExtraHeaders == null)
            {
                if (IsDefault) throw new InvalidOperationException();
                ExtraHeaders = new List<KeyValuePair<string, string>>();
            }
            ExtraHeaders.Add(new KeyValuePair<string, string>(name, value));
            return this;
        }

        public WebRequestOptions AddCookie(string name, string value, int priority)
        {
            if (CookiesList == null)
            {
                if (IsDefault) throw new InvalidOperationException();
                CookiesList = new List<PriorityCookie>();
            }
            var idx = CookiesList.FindIndex(x => x.Name == name);
            if (idx != -1) 
            {
                if (CookiesList[idx].Priority > priority) return this;
                else CookiesList.RemoveAt(idx);
            }
            CookiesList.Add(new PriorityCookie() {  Name = name, Value = value, Priority = priority });
            return this;
        }
        public WebRequestOptions AddQueryParameter(string name, object value)
        {
            if (AdditionalQueryParameters == null)
            {
                if (IsDefault) throw new InvalidOperationException();
                AdditionalQueryParameters = new List<KeyValuePair<string, object>>();
            }
            AdditionalQueryParameters.Add(new KeyValuePair<string, object>(name, value));
            return this;
        }

        public WebRequestOptions RemoveCookie(string name)
        {
            if (CookiesList != null)
            {
                var idx = CookiesList.FindIndex(x => x.Name == name);
                if (idx != -1) CookiesList.RemoveAt(idx);
            }
            return this;
        }

        public WebRequestOptions AddPostField(string name, string value)
        {
            return AddPostField(name, (object)value);
        }

        public WebRequestOptions AddPostField(string name, int value)
        {
            return AddPostField(name, (object)HttpExtensionMethods.ToString(value));
        }

#if !SALTARELLE
#if NETFX_CORE
        public WebRequestOptions AddPostField(string name, StorageFile value)
        {
            return AddPostField(name, (object)value);
        }
#else
        public WebRequestOptions AddPostField(string name, FileInfo value)
        {
            return AddPostField(name, (object)value);
        }
#endif

        public WebRequestOptions AddPostField(string name, Stream value)
        {
            return AddPostField(name, (object)value);
        }

        public WebRequestOptions AddPostField(string name, Action<Stream> value)
        {
            return AddPostField(name, (object)value);
        }


        private bool _postParametersSet;
        public object PostParameters
        {
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                if (_postParametersSet) throw new InvalidOperationException();
                if (value == null) return;
                _postParametersSet = true;
                AddParametersFromObject(value, (key, val) => AddPostField(key, val));
            }
        }
#endif


        public string AlternateCacheFolder
        {
            [RestrictedAccess]
            get;
            [RestrictedAccess]
            set;
        }

        private bool _cookiesSet;
        public object CookiesObject
        {
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                if (_cookiesSet) throw new InvalidOperationException();
                if (value == null) return;
                _cookiesSet = true;
                AddParametersFromObject(value, (key, val) => AddCookie(key, Convert.ToString(val, CultureInfo.InvariantCulture), 10));
            }
        }

        internal List<KeyValuePair<string, object>> AdditionalQueryParameters;

        public object QueryParameters
        {
            set
            {
                if (IsDefault) throw new InvalidOperationException();
                if (AdditionalQueryParameters != null) throw new InvalidOperationException();
                if (value == null) return;

                AdditionalQueryParameters = new List<KeyValuePair<string, object>>();

                AddParametersFromObject(value, (key, val) => AdditionalQueryParameters.Add(new KeyValuePair<string, object>(key, val)));
            }
        }

        private static void AddParametersFromObject(object obj, Action<string, object> adder)
        {
            var type = obj.GetType();
            var dict = obj as IEnumerable<KeyValuePair<string, object>>;
            if (dict != null)
            {
                foreach (var item in dict)
                {
                    MaybeAddMultipleItems(adder, item.Key, item.Value);
                }
                return;
            }

            var dict2 = obj as IEnumerable<KeyValuePair<string, string>>;
            if (dict2 != null)
            {
                foreach (var item in dict2)
                {
                    MaybeAddMultipleItems(adder, item.Key, item.Value);
                }
                return;
            }

            var properties =
#if NETFX_CORE
 type.GetTypeInfo().DeclaredProperties;
#else
 type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
#endif
            foreach (var item in properties)
            {
                MaybeAddMultipleItems(adder, UnescapeFieldName(item.Name), item.GetValue(obj, EmptyArray));
            }

        }

        private static void MaybeAddMultipleItems(Action<string, object> adder, string name, object value)
        {
            if (!(value is string))
            {
                var ien = value as IEnumerable;
                if (ien != null)
                {
                    foreach (var item in ien)
                    {
                        adder(name, item);
                    }
                    return;
                }
            }
            adder(name, value);
        }

        private static string UnescapeFieldName(string name)
        {
            if (name[0] != '_') return name;
            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            for (int i = 1; i < name.Length; i++)
            {
                char ch = name[i];
                if (ch == '_')
                {
                    var next = name[i + 1];
                    if (next == '_') { ch = '_'; i--; }
                    else if (next == 'L') ch = '[';
                    else if (next == 'J') ch = ']';
                    else if (next == 'X') ch = '-';
                    else throw new FormatException();
                    i += 2;
                }
                sb.Append(ch);
            }
            return ReseekableStringBuilder.GetValueAndRelease(sb);
        }



        private static readonly object[] EmptyArray = new object[0];




        internal string GetCookieHeader()
        {

            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            var first = true;
            if (!string.IsNullOrEmpty(Cookies))
            {
                sb.Append(Cookies);
                first = false;
            }

            if (CookiesList != null)
            {
                foreach (var item in CookiesList)
                {
                    if (!first) sb.Append("; ");
                    sb.Append(item.Name);
                    sb.Append('=');
                    sb.Append(HttpExtensionMethods.ToString(item.Value));
                    first = false;
                }
            }

            return ReseekableStringBuilder.GetValueAndRelease(sb);
        }


        public Action<Stream> WriteRequest;


    }

    namespace Runtime
    {
        public struct PriorityCookie
        {
            internal string Name;
            internal string Value;
            internal int Priority;

            public const int PRIORITY_SiteInfoInitial = 3;
            public const int PRIORITY_IsolatedCookieContainerInitial = 5;
            public const int PRIORITY_FromCache = 10;
            public const int PRIORITY_Login = 15;
            public const int PRIORITY_MetaParameter = 18;
            public const int PRIORITY_SetCookie = 20;
        }
    }

}
