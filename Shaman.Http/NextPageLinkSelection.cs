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
        // .link-next§§preserve§§a={z}

        public static bool UpdateNextLink(ref LazyUri modifiableUrl, HtmlNode node, string rule, bool isUnprefixedExtraParameters = false, bool alwaysPreserveRemainingParameters = false)
        {
            if (string.IsNullOrEmpty(rule))
            {
                //var ap = AutoPagerize.GetRules(modifiableUrl.PathAndQueryConsistentUrl);

#if STANDALONE
                modifiableUrl = null;
                return false;
#else
                var next = AutoPagerize.GetNextPageUrl(node);

                if (next == null || next.GetLeftPart_UriPartial_Query() == node.OwnerDocument.PageUrl.GetLeftPart_UriPartial_Query())
                {
                    modifiableUrl = null;
                    return false;
                }

                var defaults = modifiableUrl.QueryParameters.Concat(modifiableUrl.FragmentParameters).ToList();
                modifiableUrl = new LazyUri(next);
                foreach (var kv in defaults)
                {
                    if (kv.Key.StartsWith("$json-query-") && modifiableUrl.GetQueryParameter(kv.Key.CaptureBetween("-query-", "-")) != null) continue;
                    if (modifiableUrl.GetQueryParameter(kv.Key) == null && modifiableUrl.GetFragmentParameter(kv.Key) == null)
                    {
                        if (kv.Key.StartsWith("$")) modifiableUrl.AppendFragmentParameter(kv.Key, kv.Value);
                        else modifiableUrl.AppendQueryParameter(kv.Key, kv.Value);
                    }
                }
                

                return true;
#endif
            }

            var anyVarying = false;
            bool preserve = alwaysPreserveRemainingParameters;
            if (!isUnprefixedExtraParameters)
            {
                string additionalChanges = null;
                if (!rule.StartsWith("§"))
                {
                    if (rule.Contains("§§preserve"))
                    {
                        preserve = true;
                        rule = rule.Replace("§§preserve", string.Empty);
                    }
                    if (rule.Contains("§§"))
                    {
                        additionalChanges = rule.CaptureAfter("§§");
                        rule = rule.CaptureBefore("§§");
                    }
                    var nextlink = node.FindSingle(rule);
                    if (nextlink == null) { modifiableUrl = null; return false; }

                    var url = nextlink.TryGetLinkUrl();
                    if (url == null)
                    {
                        url = nextlink?.TryGetValue()?.AsUri();
                    }
                    if (!HttpUtils.IsHttp(url)) { modifiableUrl = null; return false; }
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
                    anyVarying = true;
                    if(additionalChanges == null)
                        return anyVarying;
                }

                if (additionalChanges != null) rule = additionalChanges;
                else rule = rule.Substring(1);
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
                    val = val.Substring(1, val.Length - 2);
                    var optional = false;
                    var leaveUnchanged = false;
                    if (val.StartsWith("optional:")) { optional = true; val = val.CaptureAfter(":"); }
                    if (val.StartsWith("unchanged:")) { leaveUnchanged = true; val = val.CaptureAfter(":"); }
                    var v = node.TryGetValue(val);
                    anyVarying = true;
                    if (v == null)
                    {
                        if (leaveUnchanged) continue;
                        if (optional)
                        {
                            if (key.StartsWith("$")) modifiableUrl.RemoveFragmentParameter(key);
                            else modifiableUrl.RemoveQueryParameter(key);
                            continue;
                        }
                        modifiableUrl = null;
                        return anyVarying;
                    }
                    val = v;
                }

                
                if (key.StartsWith("$")) modifiableUrl.AppendFragmentParameter(key, val);
                else modifiableUrl.AppendQueryParameter(key, val);
            }

            return anyVarying;
        }
    }
}
