using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    static class UrlRuleMatcher
    {

        private static bool HasQueryParameters(this Uri url)
        {
            return url.Query.Length > 1;
        }


        [Configuration(CommandLineAlias = "debug-scraper-rules")]
        static bool Configuration_DebugRules;

        public static Func<Uri, bool, bool?> GetMatcher(string[] rules, Uri baseUrl, bool ignoreBaseUrlScheme)
        {
            var lastComponent = baseUrl?.AbsolutePath.SplitFast('/').LastOrDefault();
            var ignoreLastComponent = lastComponent != null && lastComponent.Contains('.');
            var rulesArray = rules != null ? rules.Select(x => x.Trim()).Where(x => x.Length != 0).Select(x =>
            {
                var kind = x[0];
                var include =
                    kind == '+' ? true :
                    kind == '-' ? false :
                    throw new ArgumentException();
                var rest = x.Substring(1);
                bool? prereqOnly = null;
                if (rest.StartsWith("prereq:"))
                {
                    rest = rest.CaptureAfter(":");
                    prereqOnly = true;
                }
                else if (rest.StartsWith("noprereq:"))
                {
                    rest = rest.CaptureAfter(":");
                    prereqOnly = false;
                }
                if (prereqOnly != null && prereqOnly.Value != include)
                    throw new ArgumentException("+prereq: or -noprereq: rules must be used when prerequisite condition is specified.");

                var scheme =
                            rest == "**" ? null :
                            rest.StartsWith("http:") ? "http" :
                            rest.StartsWith("https:") ? "https" :
                            rest.StartsWith("//") ? null :
                            ignoreBaseUrlScheme ? null : baseUrl.Scheme;

                var hasHost = rest.StartsWith("//") || rest.StartsWith("http:") || rest.StartsWith("https:");
                string hostedOn = null;
                string host = null;
                if (!hasHost && rest.Length > 0 && char.IsLetterOrDigit(rest[0]))
                {
                    hostedOn = rest.TryCaptureBefore("/") ?? rest;
                    if (!hostedOn.Contains(".")) throw new ArgumentException();
                    rest = rest.Substring(hostedOn.Length);
                    if (string.IsNullOrEmpty(rest)) rest = "/**";
                    scheme = null;
                }
                else
                {
                    host = rest == "**" ? null : hasHost ?
                        rest.TryCaptureBetween("//", "/") ?? rest.CaptureAfter("//") :
                        baseUrl.Host;
                }
                var query = rest.TryCaptureAfter("?");
                var mustHaveQuery =
                    query == string.Empty ? false :
                    query == null ? (bool?)null :
                    true;
                var canHaveExtraQueryParameters = query == null || query.Contains("*");
                var queryParameters = query != "**" && !string.IsNullOrEmpty(query) ? (query.StartsWith("*") ? query.Substring(1) : query).SplitFast('&') : null;
                if (queryParameters != null && queryParameters.Any(y => y.Contains('*'))) throw new ArgumentException();
                var queryParametersMandatoryValues = queryParameters != null ? new string[queryParameters.Length] : null;
                if (queryParameters != null)
                {
                    for (int i = 0; i < queryParameters.Length; i++)
                    {
                        if (queryParameters[i].IndexOf('=') != -1)
                        {
                            var val = queryParameters[i].CaptureAfter("=");
                            queryParameters[i] = queryParameters[i].CaptureBefore("=");
                            queryParametersMandatoryValues[i] = val;
                            if (canHaveExtraQueryParameters) throw new Exception("Constrained parameter values are not supported when canHaveExtraQueryParameters.");
                        }
                    }
                }
                var path = (hasHost ? "/" + rest.CaptureAfter("//").CaptureAfter("/") : rest);
                if (!path.StartsWith("/") && !path.StartsWith("*")) throw new ArgumentException();
                path = path.TryCaptureBefore("?") ?? path;
                var hasEndAnchor = path.EndsWith("$");
                if (hasEndAnchor) path = path.Substring(0, path.Length - 1);
                var a = path.Replace("**", "__starstar__").Replace("*", "__star__");
                var pathRegex = new Regex(("^" + Regex.Escape(a) + (hasEndAnchor ? "$" : "(/.*|)$"))
                    .Replace("__starstar__", @".*")
                    .Replace("__star__", @"[^/\?&]+"));

                return new Func<Uri, bool, bool?>((url, prereq) =>
                {
                    if (prereqOnly == true && !prereq) return null;
                    if (prereqOnly == false && prereq) return null;
                    //Console.WriteLine("Rule: " + x);
                    if (scheme != null && url.Scheme != scheme) return null;
                    if (hostedOn != null && !url.IsHostedOn(hostedOn)) return null;
                    if (host != null && url.Host != host) return null;
                    if (query == "**" && !url.HasQueryParameters()) return null;
                    if (!pathRegex.IsMatch(url.AbsolutePath)) return null;
                    if (queryParameters != null)
                    {
                        if (canHaveExtraQueryParameters)
                        {
                            var t = url.GetQueryParameters().Select(z => z.Key).ToList();
                            if (queryParameters.Any(z => !t.Contains(z))) return null;
                        }
                        else
                        {
                            var n = 0;
                            foreach (var item in url.GetQueryParameters())
                            {
                                if (n >= queryParameters.Length) return null;
                                if (queryParameters[n] != item.Key) return null;
                                if (queryParametersMandatoryValues[n] != null && queryParametersMandatoryValues[n] != item.Value) return null;
                                n++;
                            }
                            if (n != queryParameters.Length) return null;
                        }
                    }
                    else
                    {
                        if (!canHaveExtraQueryParameters & url.HasQueryParameters()) return null;
                    }
                    if (Configuration_DebugRules)
                        Console.WriteLine(url.AbsoluteUri + " -> " + (include ? "YES" : "NO") + ": rule " + x);
                    return include;
                });
            }).ToList() : null;
            return (url, prereq) =>
            {
                if (rulesArray != null)
                {
                    foreach (var rule in rulesArray)
                    {
                        var z = rule(url, prereq);
                        if (z != null) return z;
                    }
                }
                
                if (IsSubfolderOf(url, baseUrl, ignoreLastComponent, ignoreBaseUrlScheme)) return true;
                if (prereq) return null;

                return false;
            };
        }

        public static bool IsSubfolderOf(Uri url, Uri folder)
        {
            return IsSubfolderOf(url, folder, false);
        }
        public static bool IsSubfolderOf(Uri url, Uri folder, bool ignoreLastComponentOfInitialUrl)
        {
            return IsSubfolderOf(url, folder, ignoreLastComponentOfInitialUrl, false);
        }
        public static bool IsSubfolderOf(Uri url, Uri folder, bool ignoreLastComponentOfInitialUrl, bool ignoreBaseUrlScheme)
        {
            if (!ignoreBaseUrlScheme && url.Scheme != folder.Scheme) return false;
            if (url.Host != folder.Host) return false;
            if (url.Port != folder.Port) return false;
            var path1 = folder.AbsolutePath;
            var path2 = url.AbsolutePath;

            if (ignoreLastComponentOfInitialUrl)
            {
                var p = path1.LastIndexOf('/');
                if (p > 0)
                {
                    path1 = path1.Substring(0, p);
                }
            }

            if (path2.Length < path1.Length) return false;
            if (!path2.StartsWith(path1)) return false;

            if (path1.Length > path2.Length && path2[path1.Length] != '/') return false;

            return true;
        }
    }
}
