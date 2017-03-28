using Shaman.Dom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if SHAMAN
using HttpUtils = Shaman.Utils;
#endif

namespace Shaman.Runtime
{
    static class NextPageLinkSelection
    {

        // Supported formats:
        // a=1&b=c    (isUnprefixedExtraParameters)
        // §a=1&b=c
        // .link-next
        // .link-next§§preserve
        // .link-next (alwaysPreserveRemainingParameters)


        public static bool UpdateNextLink(ref LazyUri modifiableUrl, HtmlNode node, string rule, bool isUnprefixedExtraParameters = false, bool alwaysPreserveRemainingParameters = false)
        {
            var anyVarying = false;
            bool preserve = alwaysPreserveRemainingParameters;
            if (!isUnprefixedExtraParameters)
            {
                if (!rule.StartsWith("§"))
                {
                    if (rule.EndsWith("§§preserve"))
                    {
                        preserve = true;
                        rule = rule.Substring(0, rule.Length - "§§preserve".Length);
                    }
                    var nextlink = node.FindSingle(rule);
                    if (nextlink == null) { modifiableUrl = null; return anyVarying; }

                    var url = nextlink.TryGetLinkUrl();
                    if (url == null)
                    {
                        url = nextlink?.TryGetValue()?.AsUri();
                    }
                    if (!HttpUtils.IsHttp(url)) { modifiableUrl = null; return anyVarying; }
                    if (!string.IsNullOrEmpty(url.Fragment))
                        url = url.GetLeftPart_UriPartial_Query().AsUri();

                    var defaults = preserve ? modifiableUrl.QueryParameters.Concat(modifiableUrl.FragmentParameters).ToList() : null;
                    modifiableUrl = new LazyUri(url);
                    if (defaults != null)
                    {
                        foreach (var kv in defaults)
                        {
                            if (kv.Key.StartsWith("$json-query-") && modifiableUrl.GetQueryParameter(kv.Key.CaptureBetween("-query-", "-")) != null) continue;
                            if (modifiableUrl.GetQueryParameter(kv.Key) == null && modifiableUrl.GetFragmentParameter(kv.Key) == null)
                            {
                                if (kv.Key.StartsWith("$")) modifiableUrl.AppendFragmentParameter(kv.Key, kv.Value);
                                else modifiableUrl.AppendQueryParameter(kv.Key, kv.Value);
                            }
                        }
                    }
                    return anyVarying;
                }

                rule = rule.Substring(1);
            }





            var z = HttpUtils.GetParameters(rule);
            foreach (var kv in z)
            {
                var val = kv.Value;
                var key = kv.Key;
                if (key.StartsWith("£")) key = "$" + key.Substring(1);

                if (val == "{delete}")
                {
                    if (key.StartsWith("$")) modifiableUrl.RemoveFragmentParameter(key);
                    else modifiableUrl.RemoveQueryParameter(key);
                    continue;
                }
                if (val.StartsWith("{") && val.EndsWith("}"))
                {
                    var v = node.TryGetValue(val.Substring(1, val.Length - 2));
                    anyVarying = true;
                    if (v == null) { modifiableUrl = null; return anyVarying; }
                    val = v;
                }

                
                if (key.StartsWith("$")) modifiableUrl.AppendFragmentParameter(key, val);
                else modifiableUrl.AppendQueryParameter(key, val);
            }

            return anyVarying;
        }
    }
}
