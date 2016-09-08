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

        public string Fragment { get { return Url.Fragment; } }

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
                    parsedUnparsedOutOfSync = this.parsedUnparsedOutOfSync,
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
        public LazyUri(string url)
        {
            this.url = url.AsUri();
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
                if (parsedUnparsedOutOfSync || (queryParameters != null && queryParameters.Count != nextQueryParameterToAdd)) return Url;
                return url;
            }
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
                        parsedUnparsedOutOfSync = false;
                    }
                    else if (unparsedUrl != null)
                    {
                        url = unparsedUrl.AsUri();
                        parsedUnparsedOutOfSync = false;
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
                        parsedUnparsedOutOfSync = true;
                        unparsedUrl = s;
                        return s;
                    }
                    return url.AbsoluteUri;
                }

            }
        }
        private bool parsedUnparsedOutOfSync;
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
                sb = new NakedStringBuilder(initial, CalculateApproxLength(initial));

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
                    sb = new NakedStringBuilder(initial, CalculateApproxLength(initial));
                }
                HttpUtils.AppendParameters(fragmentParameters.Skip(nextFragmentParameterToAdd), sb, '#');
                nextFragmentParameterToAdd = fragmentParameters.Count;
            }
            else if (fragmentsToReapply != null && sb != null)
            {
                sb.Append(fragmentsToReapply);
            }
            return sb != null ? sb.ToString() : null;
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
            queryParameters.Add(new KeyValuePair<string, string>(name, value));
        }

        private void LoadInitialQueryParameters()
        {

            if (queryParameters == null)
            {
                queryParameters = url.GetQueryParameters().ToList();
                nextQueryParameterToAdd = queryParameters.Count;
            }


        }

        public void AppendFragmentParameter(string name, string value)
        {
            LoadInitialFragmentParameters();
            fragmentParameters.Add(new KeyValuePair<string, string>(name, value));
        }

        private void LoadInitialFragmentParameters()
        {
            if (fragmentParameters == null)
            {
                fragmentParameters = url.GetFragmentParameters().ToList();
                nextFragmentParameterToAdd = fragmentParameters.Count;
            }
        }

        public void RemoveFragmentParameter(string name)
        {
            LoadInitialFragmentParameters();
            var idx = fragmentParameters.FindIndex(x => x.Key == name);
            if (idx != -1)
            {
                fragmentParameters.RemoveAt(idx);
                url = url.GetLeftPart_UriPartial_Query().AsUri();
                nextFragmentParameterToAdd = 0;
            }
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
                return PathAndQueryConsistentUrl.Query;
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

        public IEnumerable<KeyValuePair<string, string>> QueryParameters
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
        public IEnumerable<KeyValuePair<string, string>> FragmentParameters
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
    }
}
