using Newtonsoft.Json;
using Shaman.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if !STANDALONE
using HttpUtils = Shaman.Utils;
#endif
using NakedStringBuilder = System.Text.StringBuilder;
namespace Shaman
{
    [JsonConverter(typeof(LazyUriConverter))]
    public class LazyUri
    {
        private Uri url;
        private List<KeyValuePair<string, string>> queryParameters;
        private List<KeyValuePair<string, string>> fragmentParameters;
        private int nextQueryParameterToAdd;
        private int nextFragmentParameterToAdd;
        private string unparsedUrl;

        public string Fragment
        {
            get
            {

                var absurl = TryGetAbsoluteUriIfCached();
                if (absurl != null && !parsedUnparsedOutOfSync) return url.Fragment;

                absurl = AbsoluteUri;
                var hash = absurl.IndexOf('#');
                if (hash == -1) return string.Empty;

                return absurl.Substring(hash);
            }
        }

        public string PathAndQuery { get { return PathAndQueryConsistentUrl.PathAndQuery; } }
        public string Path { get { return PathAndQueryConsistentUrl.AbsolutePath; } }



        private LazyUri() { }

        public LazyUri Clone()
        {
            lock (this)
            {
                return new LazyUri()
                {
                    fragmentParameters = Clone(this.fragmentParameters),
                    nextFragmentParameterToAdd = this.nextFragmentParameterToAdd,
                    nextQueryParameterToAdd = this.nextQueryParameterToAdd,
                    queryParameters = Clone(this.queryParameters),
                    unparsedUrl = this.unparsedUrl,
                    url = this.url,
                };
            }
        }

        private List<KeyValuePair<string, string>> Clone(List<KeyValuePair<string, string>> list)
        {
            if (list == null) return null;
            return list.ToList();
        }

        public LazyUri(Uri url)
        {
            this.url = url;
        }

        [Configuration]
        private static int Configuration_FastParsingThreshold = 600;
        [Configuration]
        private static int Configuration_FastParsingMinSavings = 300;

        private static char[] Separators = new[] { '#', '?' };
        public LazyUri(string url)
        {
            if (url.Length > Configuration_FastParsingThreshold)
            {
                if (url.Contains('\n'))
                {
                    url = url.Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace(" ", "");
                }

                var idx = url.IndexOfAny(Separators);
                if (idx != -1 && url.Length - idx > Configuration_FastParsingMinSavings)
                {
                    this.url = new Uri(url.Substring(0, idx));
                    this.unparsedUrl = url;
                    return;
                }
            }
            
            this.url = url.AsUri();
            

        }

        public void AppendCookies(IEnumerable<KeyValuePair<string, string>> cookies)
        {
            foreach (var item in cookies)
            {
                AppendFragmentParameter("$cookie-" + item.Key, item.Value);
            }
        }

        public void AppendCookies(IsolatedCookieContainer cookies)
        {
            AppendCookies(cookies.Cookies);
        }

        public Uri PathConsistentUrl
        {
            get
            {
                return url;
            }
        }

        public Uri PathAndQueryConsistentUrl
        {
            get
            {
                var u = GetPathAndQueryConsistentUrlIfCached();
                if (u != null) return u;

                var k = AbsoluteUri;
                if (parsedUnparsedOutOfSync)
                {
                    var idx = unparsedUrl.IndexOf('#');
                    if (idx != -1) return unparsedUrl.Substring(0, idx).AsUri();
                }
                return this.Url;
            }
        }



        internal Uri GetPathAndQueryConsistentUrlIfCached()
        {
            if (parsedUnparsedOutOfSync || (queryParameters != null && queryParameters.Count != nextQueryParameterToAdd)) return null;
            return url;
        }
        internal Uri GetPathAndQueryAsUri()
        {
            var absurl = AbsoluteUri;
            var hash = absurl.IndexOf('#');
            if (hash == -1) return Url;
            return absurl.Substring(0, hash).AsUri();
        }

        public Uri Url
        {
            get
            {
                lock (this)
                {
                    var s = GetUrlStringIfNew();
                    if (s != null)
                    {
                        url = s.AsUri();
                        unparsedUrl = null;
                    }
                    else if (unparsedUrl != null)
                    {
                        url = unparsedUrl.AsUri();
                        unparsedUrl = null;
                    }
                    return url;
                }

            }
        }

        public string AbsoluteUri
        {
            get
            {
                lock (this)
                {
                    var s = GetUrlStringIfNew();
                    if (s != null)
                    {
                        unparsedUrl = s;
                        return s;
                    }
                    if (parsedUnparsedOutOfSync) return unparsedUrl;
                    return url.AbsoluteUri;
                }

            }
        }


        private string TryGetAbsoluteUriIfCached()
        {
            if (fragmentParameters != null && nextFragmentParameterToAdd != fragmentParameters.Count) return null;
            if (queryParameters != null && nextQueryParameterToAdd != queryParameters.Count) return null;

            if (unparsedUrl != null) return unparsedUrl;
            if (url != null) return url.AbsoluteUri;
            return null;
        }

        private bool parsedUnparsedOutOfSync => unparsedUrl != null;
        internal string GetUrlStringIfNew()
        {
            NakedStringBuilder sb = null;
            string fragmentsToReapply = null;
            LoadInitialQueryParameters();
            LoadInitialFragmentParameters();
            if (queryParameters != null && nextQueryParameterToAdd != queryParameters.Count)
            {
                if (unparsedUrl != null)
                {
                    url = unparsedUrl.AsUri();
                    unparsedUrl = null;
                }
                fragmentsToReapply = this.url != null ? this.url.Fragment : null;
                var initial = !string.IsNullOrEmpty(fragmentsToReapply) ? url.GetLeftPart_UriPartial_Query() : url.AbsoluteUri;
                sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
                sb.Append(initial);

                HttpUtils.AppendParameters(queryParameters.Skip(nextQueryParameterToAdd), sb, '?');
                nextQueryParameterToAdd = queryParameters.Count;
            }
            if (fragmentParameters != null && nextFragmentParameterToAdd != fragmentParameters.Count)
            {
                if (unparsedUrl != null)
                {
                    url = unparsedUrl.AsUri();
                    unparsedUrl = null;
                }
                if (!string.IsNullOrEmpty(fragmentsToReapply)) sb.Append(fragmentsToReapply);
                else if (sb == null)
                {
                    var initial = url.AbsoluteUri;
                    sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
                    sb.Append(initial);
                }
                HttpUtils.AppendParameters(fragmentParameters.Skip(nextFragmentParameterToAdd), sb, '#');
                nextFragmentParameterToAdd = fragmentParameters.Count;
            }
            else if (fragmentsToReapply != null && sb != null)
            {
                sb.Append(fragmentsToReapply);
            }
            return sb != null ? ReseekableStringBuilder.GetValueAndRelease(sb) : null;
        }

        private int CalculateApproxLength(string initial)
        {
            double length = initial.Length;
            if (queryParameters != null)
            {
                for (int i = nextQueryParameterToAdd; i < queryParameters.Count; i++)
                {
                    var p = queryParameters[i];
                    length += Configuration_ParameterEscapingLengthEstimationRatio * (p.Key.Length + p.Value.Length + Configuration_ParameterEscapingLengthEstimationAddition);
                }
            }
            if (fragmentParameters != null)
            {
                for (int i = nextFragmentParameterToAdd; i < fragmentParameters.Count; i++)
                {
                    var p = fragmentParameters[i];
                    length += Configuration_ParameterEscapingLengthEstimationRatio * (p.Key.Length + p.Value.Length + Configuration_ParameterEscapingLengthEstimationAddition);
                }
            }
            length += 3;
            return (int)length;
        }

        [Configuration]
        internal const double Configuration_ParameterEscapingLengthEstimationRatio = 1.2;
        [Configuration]
        private const double Configuration_ParameterEscapingLengthEstimationAddition = 3;
        public string AbsolutePath => PathConsistentUrl.AbsolutePath;

        public bool IsDefaultPort => PathConsistentUrl.IsDefaultPort;

        public string DnsSafeHost => PathConsistentUrl.DnsSafeHost;

        public void AppendQueryParameter(string name, string value)
        {
            LoadInitialQueryParameters();
            RemoveQueryParameter(name);
            queryParameters.Add(new KeyValuePair<string, string>(name, value));
        }

        private void LoadInitialQueryParameters()
        {

            if (queryParameters == null)
            {

                if (parsedUnparsedOutOfSync)
                {
                    var q = unparsedUrl.IndexOf('?');
                    if (q == -1) queryParameters = new List<KeyValuePair<string, string>>();
                    else queryParameters = HttpUtils.GetParameters(unparsedUrl.Substring(q)).ToList();
                }
                else
                {
                    queryParameters = url.GetQueryParameters().ToList();
                }


                nextQueryParameterToAdd = queryParameters.Count;
            }


        }

        public void AppendFragmentParameter(string name, string value)
        {
            LoadInitialFragmentParameters();
            RemoveFragmentParameter(name);
            fragmentParameters.Add(new KeyValuePair<string, string>(name, value));
        }

        private void LoadInitialFragmentParameters()
        {
            if (fragmentParameters == null)
            {
                if (parsedUnparsedOutOfSync)
                {
                    var hash = unparsedUrl.IndexOf('#');
                    if (hash == -1) fragmentParameters = new List<KeyValuePair<string, string>>();
                    else fragmentParameters = HttpUtils.GetParameters(unparsedUrl.Substring(hash)).ToList();
                }
                else
                {
                    fragmentParameters = url.GetFragmentParameters().ToList();
                }
                nextFragmentParameterToAdd = fragmentParameters.Count;
            }
        }

        public void RemoveQueryParameter(string name)
        {
            LoadInitialQueryParameters();
            LoadInitialFragmentParameters();
            var idx = queryParameters.FindIndex(x => x.Key == name);
            if (idx != -1)
            {
                queryParameters.RemoveAt(idx);
                if (!string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
                {
                    url = url.GetLeftPart(UriPartial.Path).AsUri();
                }
                unparsedUrl = null;
                nextQueryParameterToAdd = 0;
                nextFragmentParameterToAdd = 0;
            }
        }

        public void RemoveFragmentParameter(string name)
        {
            LoadInitialFragmentParameters();
            var idx = fragmentParameters.FindIndex(x => x.Key == name);
            if (idx != -1)
            {
                fragmentParameters.RemoveAt(idx);
                if (!string.IsNullOrEmpty(url.Fragment))
                {
                    url = url.GetLeftPart_UriPartial_Query().AsUri();
                }
                unparsedUrl = null;
                nextFragmentParameterToAdd = 0;
            }
        }


        public string GetLeftPart_Path()
        {
            var u = PathConsistentUrl;
            return u.GetLeftPart(UriPartial.Path);
        }

        public string Authority
        {
            get
            {
                return url.Authority;
            }
        }


        public string Host
        {
            get
            {
                return url.Host;
            }
        }

        public int Port
        {
            get
            {
                return url.Port;
            }
        }

        public string Scheme
        {
            get
            {
                return url.Scheme;
            }
        }

        public string Query
        {
            get
            {

                var absurl = TryGetAbsoluteUriIfCached();
                if (absurl != null && !parsedUnparsedOutOfSync) return url.Query;

                absurl = AbsoluteUri;
                var q = absurl.IndexOf('?');
                if (q == -1) return string.Empty;

                var hash = absurl.IndexOf('#', q);
                if (hash != -1) return absurl.Substring(q, hash - q);
                return absurl.Substring(q);
            }
        }

        public string GetQueryParameter(string name)
        {
            return QueryParameters.FirstOrDefault(x => x.Key == name).Value;
        }
        public string GetFragmentParameter(string name)
        {
            return FragmentParameters.FirstOrDefault(x => x.Key == name).Value;
        }

#if NET35
        public IEnumerable<KeyValuePair<string, string>> QueryParameters
#else
        public IReadOnlyList<KeyValuePair<string, string>> QueryParameters
#endif       
        {
            get
            {
                if (queryParameters == null)
                {
                    lock (this)
                    {
                        LoadInitialQueryParameters();
                    }
                }
                return queryParameters;
            }
        }
#if NET35
        public IEnumerable<KeyValuePair<string, string>> FragmentParameters
#else
        public IReadOnlyList<KeyValuePair<string, string>> FragmentParameters
#endif

        {
            get
            {
                if (fragmentParameters == null)
                {
                    lock (this)
                    {
                        LoadInitialFragmentParameters();
                    }
                }
                return fragmentParameters;
            }
        }



        public bool IsAbsoluteUri { get { return url.IsAbsoluteUri; } }

        public override string ToString()
        {
            return Url.ToString();
        }

        internal static bool UrisEqual(LazyUri a, LazyUri b)
        {
            if (a == null || b == null) return (a == null) == (b == null);
            if (object.ReferenceEquals(a, b)) return true;

            var aa = a.TryGetAbsoluteUriIfCached();
            if (aa != null)
            {
                var bb = b.TryGetAbsoluteUriIfCached();
                if (bb != null) return aa == bb;
            }

            if (a.Scheme != b.Scheme) return false;
            if (a.Authority != b.Authority) return false;
            if (a.AbsolutePath != b.AbsolutePath) return false;

            lock (a)
            {
                a.LoadInitialFragmentParameters();
                a.LoadInitialQueryParameters();
            }

            lock (b)
            {
                b.LoadInitialFragmentParameters();
                b.LoadInitialQueryParameters();
            }

            if (a.fragmentParameters.Count != b.fragmentParameters.Count) return false;
            if (a.queryParameters.Count != b.queryParameters.Count) return false;

            for (int i = 0; i < a.fragmentParameters.Count; i++)
            {
                var af = a.fragmentParameters[i];
                var bf = b.fragmentParameters[i];
                if (af.Key != bf.Key || af.Value != bf.Value) return false;
            }

            for (int i = 0; i < a.queryParameters.Count; i++)
            {
                var aq = a.queryParameters[i];
                var bq = b.queryParameters[i];
                if (aq.Key != bq.Key || aq.Value != bq.Value) return false;
            }

            return true;
        }

      
    }
}
