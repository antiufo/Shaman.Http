using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if !STANDALONE
using HttpUtils = Shaman.Utils;
#endif
namespace Shaman.Runtime
{
    public class IsolatedCookieContainer : CookieContainer
    {
        internal string CacheVaryKey;
        internal Dictionary<string, string> _cookies = new Dictionary<string, string>();
        internal Dictionary<string, string> LastPersistedCookies;


#if NET35
        public IDictionary<string, string> Cookies => _cookies;
#else
        public IReadOnlyDictionary<string, string> Cookies => _cookies;
#endif


        internal void MaybeSave()
        {
            if (CacheVaryKey == null) return;
#if DESKTOP
            if (LastPersistedCookies != null && _cookies.Count == LastPersistedCookies.Count && _cookies.All(x => LastPersistedCookies.TryGetValue(x.Key) == x.Value)) return;

            var p = GetCachePath();

            Caching.SaveCache(p, new WebCache()
            {
                Cookies = _cookies,
            });
            LastPersistedCookies = _cookies.ToDictionary(x => x.Key, x => x.Value);
#endif
        }

        internal string GetCachePath()
        {
            if (CacheVaryKey == null) return null;
            return Caching.GetWebCachePath(new LazyUri("http://shaman-cookies/?id=" + HttpUtils.EscapeDataString(CacheVaryKey)), false, true);
        }

        [StaticFieldCategory(StaticFieldCategory.TODO)]
        private static Dictionary<string, int> isolatedCookieContainerIds = new Dictionary<string, int>();

        internal static IsolatedCookieContainer Create(string id)
        {
            var d = isolatedCookieContainerIds.TryGetNullableValue(id).GetValueOrDefault();
            d++;
            isolatedCookieContainerIds[id] = d;
            if (d != 1)
            {
                id += "?" + d;
            }

            var isolatedCookies = new IsolatedCookieContainer();
            isolatedCookies.CacheVaryKey = id;
            var p = isolatedCookies.GetCachePath();
            if (p != null)
            {
                var f = Caching.TryReadCacheFile(p);
                if (f != null)
                {
                    isolatedCookies._cookies = f.Cookies;
                    isolatedCookies.LastPersistedCookies = f.Cookies.ToDictionary(x => x.Key, x => x.Value);
                }
            }
            return isolatedCookies;
        }
    }
}
