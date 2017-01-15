using Fizzler;
using Shaman.Dom;
#if !SALTARELLE
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
#if SALTARELLE
using StringBuilder = System.Text.Saltarelle.StringBuilder;
using JToken = System.Object;
using JObject = System.Object;
using JArray = System.Array;
using HtmlNodeHashSet = System.Collections.Generic.List<Shaman.Dom.HtmlNode>;
#else
using HtmlNodeHashSet = System.Collections.Generic.HashSet<Shaman.Dom.HtmlNode>;
#endif
#if !STANDALONE
using HttpUtils = Shaman.Utils;
using HttpExtensionMethods = Shaman.ExtensionMethods;
#endif

namespace Shaman.Runtime
{
    public static class FizzlerCustomSelectors
    {

#if !SALTARELLE
        public static void CleanupThreadCache()
        {
            lastConvertedJsonHtml = null;
            lastConvertedJsonString = null;
            lastConvertedJsonParentDocument = null;
            lastTextSplitNode = null;
            lastTextSplitSeparator = null;
            lastTextSplitResult = null;
        }

#endif

#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        internal static HtmlNode lastTextSplitNode;
#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        internal static IEnumerable<HtmlNode> lastTextSplitResult;
#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        internal static string lastTextSplitSeparator;


#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        internal static HtmlNode lastConvertedJsonHtml;

#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        internal static string lastConvertedJsonString;


#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        internal static HtmlDocument lastConvertedJsonParentDocument;

#if !SALTARELLE
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.TrimmedCache)]
#endif
        private static int lastConvertedJsonIndex;

#if SALTARELLE
        private static dynamic JSON5 = Script.Eval("JSON5");
#endif

        internal static HtmlNode JsonToHtml(string source, int startIndex, HtmlDocument parentDocument)
        {
            if (object.ReferenceEquals(source, lastConvertedJsonString) && object.ReferenceEquals(parentDocument, lastConvertedJsonParentDocument) && lastConvertedJsonIndex == startIndex)
                return lastConvertedJsonHtml;

            JToken token;
#if SALTARELLE
            token = JSON5.parse(source.Substring(startIndex));
            //token = System.Serialization.Json.Parse(source.Substring(startIndex));
#else
            token = HttpUtils.ReadJsonToken(source, startIndex);
            
#endif
            lastConvertedJsonHtml = JsonToHtml(null, token, CreateDocument(parentDocument), false);
            // base url doesn't probably apply here.
            lastConvertedJsonParentDocument = parentDocument;
            var doc = lastConvertedJsonHtml.OwnerDocument;
            var xmlver = doc.CreateElement("?xml");
            xmlver.SetAttributeValue("version", "1.0");
            doc.DocumentNode.PrependChild(xmlver);
            doc.DocumentNode.SetAttributeValue("awdee-converted-json", "1");
            lastConvertedJsonString = source;
            lastConvertedJsonIndex = startIndex;

            return lastConvertedJsonHtml;
        }

        internal static HtmlDocument CreateDocument(HtmlDocument parentDocument)
        {
            var doc = new HtmlDocument();
            if (parentDocument != null)
            {
                var page = parentDocument.PageUrl;
                if (page != null) doc.SetPageUrl(page);
                var date = parentDocument.DocumentNode.GetAttributeValue("date-retrieved");
                if (date != null) doc.DocumentNode.SetAttributeValue("date-retrieved", date);
            }
            return doc;
        }

        private static HtmlNode JsonToHtml(string name, JToken token, HtmlDocument document, bool isArrayItem)
        {
            HtmlNode root = null;
            if (name == null)
            {
                root = document.CreateElement("json-root");
                document.DocumentNode.AppendChild(root);
            }
            var obj = token as JObject;

            HtmlNode el;
            if (name != null)
            {
                var tagName = name;
                StringBuilder sb = null;

                for (int i = 0; i < name.Length; i++)
                {
                    var ch = name[i];
                    if (char.IsLower(ch) || ch == '-' || ch == '_' ||
#if SALTARELLE
 AwdeeUtils.IsDigit(ch)
#else
 char.IsDigit(ch)
#endif
)
                    {
                        if (sb != null) sb.Append(ch);
                    }
                    else
                    {
                        if (sb == null)
                        {
                            sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
                            sb.Append(name.Substring(0, i)
#if SALTARELLE
.ToLower());
#else
.ToLowerFast());
#endif
                        }
                        if (char.IsUpper(ch))
                        {
#if SALTARELLE
                            sb.Append(ch.ToString().ToLower());
#else
                            sb.Append(char.ToLowerInvariant(ch));
#endif
                        }
                    }
                }
                if (sb != null) tagName = ReseekableStringBuilder.GetValueAndRelease(sb);
                if (string.IsNullOrEmpty(tagName)) tagName = "obj";
                if (tagName.Length != 0 &&
#if SALTARELLE
 AwdeeUtils.IsDigit(tagName[0])
#else
 char.IsDigit(tagName[0])
#endif
)
                    tagName = "node-" + tagName;
                el = document.CreateElement(tagName);
            }
            else
            {
                el = root;
            }



            if (name != null && !isArrayItem)
            {
                el.SetAttributeValue("key", name);
            }
            var arr = token as JArray;
            if (arr != null)
            {
                el.SetAttributeValue("awdee-json-array", "1");

#if !SALTARELLE
                arr = NormalizeArrayOrder(arr);
#endif

                foreach (var item in arr)
                {
                    el.AppendChild(JsonToHtml("item", item, document, true));
                }
                return el;
            }
#if SALTARELLE
            if (obj != null && Script.TypeOf(obj) == "object")
            {
                var dict = JsDictionary<string, object>.GetDictionary(obj);

                foreach (var prop in object.GetOwnPropertyNames(obj))
                {
                    el.AppendChild(JsonToHtml(prop, dict[prop], document, false));
                }

                return el;
            }
#else
            if (obj != null)
            {

                foreach (var item in obj)
                {
                    el.AppendChild(JsonToHtml(item.Key, item.Value, document, false));
                }

                return el;
            }
#endif


#if SALTARELLE
            var val = token;
#else
            var val = token as JValue;

            // json.net uses a JToken for null, js uses null itself
            if (val != null)
#endif
            {
                var text = document.CreateTextNode();
#if SALTARELLE
                text.Text = Script.IsNullOrUndefined(val) ? string.Empty : val.ToString();
#else
                text.Text = Convert.ToString(val.Value);
#endif
                el.AppendChild(text);
                return el;
            }

            throw new NotSupportedException();
        }

#if !SALTARELLE
        private static JArray NormalizeArrayOrder(JArray arr)
        {
            if (arr.Count <= 1) return arr;
            var order = new List<string>();
            bool ok = true;
            foreach (var item in arr)
            {
                if (item.Type == JTokenType.Null) continue;
                if (item.Type == JTokenType.Undefined) continue;
                if (item.Type != JTokenType.Object) return arr;

                var obj = (JObject)item;
                var expectedIndex = 0;
                var previdx = -1;
                foreach (var pair in obj)
                {
                    var idx = order.IndexOf(pair.Key);

                    if (idx == -1)
                    {
                        idx = previdx + 1;
                        order.Insert(idx, pair.Key);
                    }
                    else if (idx < expectedIndex)
                    {
                        ok = false;
                    }


                    expectedIndex = idx + 1;
                    previdx = idx;
                }

            }

            if (!ok)
            {
                var c = new JArray();
                foreach (var item in arr)
                {

                    var newobj = new JObject();

                    foreach (var prop in order)
                    {
                        var v = item[prop];
                        if (v != null)
                        {
                            newobj.Add(prop, v);
                        }
                    }
                    c.Add(newobj);
                }

                return c;
            }

            return arr;
        }
#endif

        private static int registered;
        internal static HtmlNode ReparseHtml(HtmlDocument doc, string html, HtmlDocument parentDocument)
        {
            if (object.ReferenceEquals(html, lastConvertedJsonString) && object.ReferenceEquals(parentDocument, lastConvertedJsonParentDocument) && lastConvertedJsonIndex == 0)
                return lastConvertedJsonHtml;

            doc.LoadHtml("<reparsed-html>" + html + "</reparsed-html>");
            lastConvertedJsonHtml = doc.DocumentNode.FirstChild;


            lastConvertedJsonParentDocument = parentDocument;
            lastConvertedJsonString = html;
            lastConvertedJsonIndex = 0;

            return lastConvertedJsonHtml;
        }

        internal static HtmlNode WrapText(HtmlNode source, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var doc = CreateDocument(source.OwnerDocument);
            var el = doc.CreateElement("fizzler-node-group");
            el.AppendTextNode(text);
            return el;
        }

        public static void RegisterAll()
        {
#if SALTARELLE
            if (registered != 0) return;
            registered = 1;
#else
            if (Interlocked.Increment(ref registered) != 1) return;
#endif
            Parser.RegisterCustomSelector<HtmlNode>("ldjson", () =>
            {
                return nodes =>
                {
                    return nodes.Where(x => x.TagName == "script" && x.GetAttributeValue("type") == "application/ld+json").Select(x => JsonToHtml(x.InnerText, 0, x.OwnerDocument));
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("property", (property) =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        if ((x.TagName == "meta" && x.GetAttributeValue("name") == property) || x.GetAttributeValue("property") == property || x.GetAttributeValue("itemprop") == property)
                        {
                            var val = x.GetAttributeValue("value") ?? x.GetAttributeValue("content") ?? (x.GetAttributeValue("src") != null || x.GetAttributeValue("href") != null ? x.TryGetLinkUrl()?.AbsoluteUri : null) ?? x.TryGetValue();
                            return WrapText(x, val);
                        }
                        return null;
                    }).WhereNotNull();
                };
            });
            Parser.RegisterCustomSelector<HtmlNode, string>("compose-url", (model) =>
            {
                if (string.IsNullOrEmpty(model)) throw new ArgumentNullException();
                return nodes =>
                {
                    var n = nodes.FirstOrDefault();
                    if (n == null) return Enumerable.Empty<HtmlNode>();
                    var u = n.TryGetValue();
                    if (u == null) return Enumerable.Empty<HtmlNode>();
                    model = HttpUtils.GetAbsoluteUriAsString(n.OwnerDocument.PageUrl, model);
                    return new[] { WrapText(n, model.Replace("@", HttpUtils.EscapeDataString(u))) };
                };
            });

            Parser.RegisterCustomSelector<HtmlNode>("debug", () =>
            {
                return nodes =>
                {
                    return nodes.Select<HtmlNode, HtmlNode>(x =>
                    {
                        throw new CssSelectorBreakpointException();
                    });
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("debug-when", (selector) =>
            {
                return nodes =>
                {
                    var sub = selector(nodes);
                    if (sub.Any()) throw new CssSelectorBreakpointException();
                    return nodes;
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("debug-when-not", (selector) =>
            {
                return nodes =>
                {
                    var sub = selector(nodes);
                    if (!sub.Any()) throw new CssSelectorBreakpointException();
                    return nodes;
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, string>("find-attribute", name =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var attr = x.GetAttributeValue(name);
                        return attr != null ? WrapText(x, attr) : null;
                    }).WhereNotNull();
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, string>("select-attribute", name =>
            {
                return nodes =>
                {
                    var first = nodes.FirstOrDefault();
                    if (first != null)
                    {
                        var v = first.GetAttributeValue(name);
                        if (v != null)
                        {
                            var doc = CreateDocument(first.OwnerDocument);
                            var el = doc.CreateElement("fizzler-node-group");
                            el.AppendTextNode(v);
                            return new[] { el };
                        }
                    }

                    return Enumerable.Empty<HtmlNode>();
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("capture", regex =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var p = x.TryGetValue();
                        return p != null ? WrapText(x, p.TryCapture(regex)) : null;
                    }).Where(x => x != null);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("link-parameter", name =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var z = x.GetAttributeValue("href") ?? x.GetAttributeValue("src") ?? x.TryGetValue();
                        if (z == null) return null;

                        var url = new Uri("http://dummy/".AsUri(), z);
                        var p = url.GetQueryParameter(name);
                        return p != null ? WrapText(x, p) : null;
                    }).Where(x => x != null);
                };
            });



            Parser.RegisterCustomSelector<HtmlNode>("html-deentitize", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var z = x.TryGetValue();
                        if (z == null) return null;
                        return WrapText(x, HtmlEntity.DeEntitize(z));
                    }).Where(x => x != null);
                };
            });
            Parser.RegisterCustomSelector<HtmlNode>("html-entitize", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var z = x.TryGetValue();
                        if (z == null) return null;
                        return WrapText(x, HtmlEntity.Entitize(z));
                    }).Where(x => x != null);
                };
            });
            Parser.RegisterCustomSelector<HtmlNode>("uri-escape", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var z = x.TryGetValue();
                        if (z == null) return null;
                        return WrapText(x, HttpUtils.EscapeDataString(z));
                    }).Where(x => x != null);
                };
            });
            Parser.RegisterCustomSelector<HtmlNode>("uri-unescape", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var z = x.TryGetValue();
                        if (z == null) return null;
                        return WrapText(x, HttpUtils.UnescapeDataString(z));
                    }).Where(x => x != null);
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, string, string>("text-between", (before, after) =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var p = x.TryGetValue();
                        return p != null ? WrapText(x, p.TryCaptureBetween(before, after)) : null;
                    }).Where(x => x != null);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("text-before", (after) =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var p = x.TryGetValue();
                        if (p == null) return null;

                        var idx = p.IndexOf(after);
                        return idx != -1 ? WrapText(x, p.Substring(0, idx)) : null;
                    }).Where(x => x != null);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("text-after", (before) =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var p = x.TryGetValue();
                        if (p == null) return null;

                        var idx = p.IndexOf(before);
                        return idx != -1 ? WrapText(x, p.Substring(idx + before.Length)) : null;
                    }).Where(x => x != null);
                };
            });






            Parser.RegisterCustomSelector<HtmlNode>("reparse-html", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var doc = CreateDocument(x.OwnerDocument);
                        return ReparseHtml(doc, x.InnerText, x.OwnerDocument);
                    });
                };
            });
            Parser.RegisterCustomSelector<HtmlNode, string>("text-is", (text) =>
            {
                if (text == string.Empty) text = null;
                return nodes =>
                {
                    return nodes.Where(x => x.GetText() == text);
                };
            });
            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>, Selector<HtmlNode>>("either", (alt1, alt2) =>
            {
                return nodes =>
                {
                    var first = alt1(nodes).FirstOrDefault();
                    if (first != null) return new[] { first };
                    var other = alt2(nodes).FirstOrDefault();
                    if (other != null) return new[] { other };
                    return Enumerable.Empty<HtmlNode>();
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("direct-text-is", (text) =>
            {
                if (text == string.Empty) text = null;

                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        var count = x.ChildNodes.Count;
                        if (count > 1) return false;
                        if (count == 0) return text == null;
                        var n = x.FirstChild as HtmlTextNode;
                        if (n == null) return false;
                        var found = n.Text;
                        if (text == null) return IsNullOrWhiteSpace(found);
                        if (found.Length == text.Length) return found == text;
                        if (found.Length < text.Length) return false;
                        return found.Trim() == text;
                    });
                };
            });
            Parser.RegisterCustomSelector<HtmlNode, string>("first-text-is", (text) =>
            {
                if (text == string.Empty) text = null;

                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        var n = x.FirstChild as HtmlTextNode;
                        if (n == null) return text == null;
                        var found = n.Text;
                        if (text == null) return IsNullOrWhiteSpace(found);
                        if (found.Length == text.Length) return found == text;
                        if (found.Length < text.Length) return false;
                        return found.Trim() == text;
                    });
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("first-after-text", (text) =>
            {
                if (text == string.Empty) throw new ArgumentException(":first-after-text requires a non-empty value.");

                return nodes =>
                {
                    return nodes.Select(root =>
                    {
                        if (root == null) return null;
                        var textnode = root.ChildNodes.FirstOrDefault(x =>
                        {
                            var n = x as HtmlTextNode;
                            if (n == null) return false;
                            var found = n.Text;
                            if (text == null) return IsNullOrWhiteSpace(found);
                            if (found.Length == text.Length) return found == text;
                            if (found.Length < text.Length) return false;
                            return found.Trim() == text;
                        });
                        if (textnode != null && textnode.NextSibling != null && textnode.NextSibling.NodeType == HtmlNodeType.Element)
                        {
                            return textnode.NextSibling;
                        }
                        return null;
                    }).Where(x => x != null);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("malformed-selector", (selector) =>
            {
                return nodes => Enumerable.Empty<HtmlNode>();
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("make-url", format =>
            {
                return nodes =>
                {
                    var s = nodes.FirstOrDefault();
                    if (s != null)
                    {
                        var v = s.TryGetValue();
                        if (v != null)
                        {
                            var m = HttpUtils.FormatEscaped(format, v).AbsoluteUri;
                            var doc = CreateDocument(s.OwnerDocument);
                            doc.DocumentNode.SetAttributeValue("awdee-converted-json", "1");
                            var a = doc.CreateElement("href");
                            a.AppendTextNode(m);
                            return new[] { a };

                        }
                    }
                    return Enumerable.Empty<HtmlNode>();
                };
            });

            Parser.RegisterCustomSelector<HtmlNode>("make-img", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var v = x.TryGetValue();
                        if (v != null)
                        {
                            var doc = CreateDocument(x.OwnerDocument);
                            var img = doc.CreateElement("img");
                            img.SetAttributeValue("src", v);
                            return img;
                        }
                        return null;
                    }).Where(x => x != null);
                };
            });
            Parser.RegisterCustomSelector<HtmlNode>("split-text-lines", () =>
            {

                return nodes =>
                {
                    var t = nodes.FirstOrDefault();
                    if (t != null)
                    {
                        var m = t.OwnerDocument.IsPlainText() ? ((HtmlTextNode)t.ChildNodes.Single()).Text : t.GetText();
#if SALTARELLE
                        var lines = m.Split('\n');
#else
                        var lines = m.SplitFast('\n');
#endif
                        return lines.Select(x =>
                        {
                            if (IsNullOrWhiteSpace(x)) return null;
                            var d = t.OwnerDocument.CreateElement("fizzler-node-group");

                            var txt = t.OwnerDocument.CreateTextNode(x);
                            d.AppendChild(txt);

                            return d;
                        }).Where(x => x != null);
                    }
                    return Enumerable.Empty<HtmlNode>();
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, string>("split-text", separator =>
            {

                return nodes =>
                {
                    var t = nodes.FirstOrDefault();
                    if (t != null)
                    {
                        if (lastTextSplitNode == t && lastTextSplitSeparator == separator) return lastTextSplitResult;
                        var m = t.OwnerDocument.IsPlainText() ? ((HtmlTextNode)t.ChildNodes.Single()).Text : t.GetText();
                        string[] parts;
                        if (separator.Length == 1)
                        {
#if SALTARELLE
                            parts = m.Split(separator[0]);
#else
                            parts = m.SplitFast(separator[0]);
#endif
                        }
                        else
                        {

                            parts = m.Split(new[] { separator }, StringSplitOptions.None);

                        }
                        lastTextSplitNode = null;
                        lastTextSplitResult = parts.Select(x =>
                        {
                            if (IsNullOrWhiteSpace(x)) return null;
                            var d = t.OwnerDocument.CreateElement("fizzler-node-group");

                            var txt = t.OwnerDocument.CreateTextNode(x);
                            d.AppendChild(txt);

                            return d;
                        }).Where(x => x != null).ToList();
                        lastTextSplitNode = t;
                        lastTextSplitSeparator = separator;
                        return lastTextSplitResult;
                    }
                    return Enumerable.Empty<HtmlNode>();
                };
            });






            Parser.RegisterCustomSelector<HtmlNode, string, string>("link-has-host-path-regex", (host, regex) =>
            {
#if SALTARELLE
                var regexp = new Regex(regex);
#endif
                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        Uri link;
                        try
                        {
                            link = x.TryGetLinkUrl();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (link == null || (link.Scheme != HttpUtils.UriSchemeHttp && link.Scheme != HttpUtils.UriSchemeHttps)) return false;

                        if (!link.IsHostedOn(host)) return false;
#if SALTARELLE
                        if (!regexp.Test(link.AbsolutePath)) return false;
#else
                        if (!Regex.IsMatch(link.AbsolutePath, regex)) return false;
#endif
                        return true;
                    });

                };
            });







            Parser.RegisterCustomSelector<HtmlNode, string>("link-has-path-regex", (regex) =>
            {
#if SALTARELLE
                var regexp = new Regex(regex);
#endif
                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        Uri link;
                        try
                        {
                            link = x.TryGetLinkUrl();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (link == null || (link.Scheme != HttpUtils.UriSchemeHttp && link.Scheme != HttpUtils.UriSchemeHttps)) return false;

                        var host = x.OwnerDocument.PageUrl.Host;

                        if (!link.IsHostedOn(host)) return false;
#if SALTARELLE
                        if (!regexp.Test(link.AbsolutePath)) return false;
#else
                        if (!Regex.IsMatch(link.AbsolutePath, regex)) return false;
#endif
                        return true;
                    });

                };
            });



















            Parser.RegisterCustomSelector<HtmlNode, string, string>("link-has-host-path", (host, path) =>
            {
                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        Uri link;
                        try
                        {
                            link = x.TryGetLinkUrl();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (link == null || (link.Scheme != HttpUtils.UriSchemeHttp && link.Scheme != HttpUtils.UriSchemeHttps)) return false;

                        if (!link.AbsolutePath.StartsWith(path)) return false;
                        if (!link.IsHostedOn(host)) return false;
                        return true;
                    });

                };
            });



            Parser.RegisterCustomSelector<HtmlNode, string>("link-has-path", (path) =>
            {
                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        Uri link;
                        try
                        {
                            link = x.TryGetLinkUrl();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (link == null || (link.Scheme != HttpUtils.UriSchemeHttp && link.Scheme != HttpUtils.UriSchemeHttps)) return false;

                        if (!link.AbsolutePath.StartsWith(path)) return false;

                        var host = x.OwnerDocument.PageUrl.Host;
                        if (!link.IsHostedOn(host)) return false;
                        return true;
                    });

                };
            });







            Parser.RegisterCustomSelector<HtmlNode, string>("link-has-host", host =>
            {
                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        Uri link;
                        try
                        {
                            link = x.TryGetLinkUrl();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (link == null || (link.Scheme != HttpUtils.UriSchemeHttp && link.Scheme != HttpUtils.UriSchemeHttps)) return false;
                        if (!link.IsHostedOn(host)) return false;
                        return true;

                    });

                };
            });


            Parser.RegisterCustomSelector<HtmlNode>("link-is-internal", () =>
            {
                return nodes =>
                {
                    return nodes.Where(x =>
                    {
                        Uri link;
                        try
                        {
                            link = x.TryGetLinkUrl();
                        }
                        catch (Exception)
                        {
                            return false;
                        }

                        if (link == null || (link.Scheme != HttpUtils.UriSchemeHttp && link.Scheme != HttpUtils.UriSchemeHttps)) return false;
                        var host = x.OwnerDocument.PageUrl.Host;
                        if (!link.IsHostedOn(host)) return false;
                        return true;

                    });

                };
            });


            Parser.RegisterCustomSelector<HtmlNode>("following-text", () =>
            {

                return nodes =>
                {
                    var k = nodes.FirstOrDefault();
                    if (k == null || k.NextSibling == null) return Enumerable.Empty<HtmlNode>();
                    var n = k.OwnerDocument.CreateElement("fizzler-node-group");
                    n.ChildNodes.Add(k.NextSibling);
                    return new[] { n };
                };
            });

            Parser.RegisterCustomSelector<HtmlNode>("previous-text", () =>
            {

                return nodes =>
                {
                    var k = nodes.FirstOrDefault();
                    if (k == null || k.PreviousSibling == null) return Enumerable.Empty<HtmlNode>();
                    var n = k.OwnerDocument.CreateElement("fizzler-node-group");
                    n.ChildNodes.Add(k.PreviousSibling);
                    return new[] { n };
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, int>("select-ancestor", (ancestorIndex) =>
            {
                return nodes =>
                {
                    var n = nodes.FirstOrDefault();
                    if (n == null) return Enumerable.Empty<HtmlNode>();

                    for (int i = 0; i < ancestorIndex; i++)
                    {
                        n = n.ParentNode;
                        if (n == null) break;
                    }
                    return new[] { n };
                };
            });

            Parser.RegisterCustomSelector<HtmlNode>("reparse-json", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var v = x.InnerText;
                        if (v == null) return null;
                        return JsonToHtml(v, 0, x.OwnerDocument);
                    }).WhereNotNull();
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, string>("reparse-attr-html", attributeName =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var doc = CreateDocument(x.OwnerDocument);
                        var v = x.GetAttributeValue(attributeName);
                        if (v == null) return null;
                        return ReparseHtml(doc, v, x.OwnerDocument);
                    }).WhereNotNull();
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("json-attr", attributeName =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var jattr = x.GetAttributeValue(attributeName);
                        if (string.IsNullOrEmpty(jattr)) return null;
                        return JsonToHtml(HttpExtensionMethods.CleanupJsonp(jattr), 0, x.OwnerDocument);
                    })
                    .WhereNotNull();


                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string, string>("json-attr-token", (attributeName, startToken) =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var jattr = x.GetAttributeValue(attributeName);
                        if (string.IsNullOrEmpty(jattr)) return null;

                        var startIndex = jattr.IndexOf(startToken);
                        if (startIndex == -1) return null;

                        return JsonToHtml(jattr, startIndex + startToken.Length, x.OwnerDocument);
                    })
                    .WhereNotNull();


                };
            });

            Parser.RegisterCustomSelector<HtmlNode, string>("json-token", startToken =>
            {
                return nodes =>
                {
                    return nodes.Select(f =>
                    {
                        var firstChild = f.FirstChild;
                        var content = firstChild != null && firstChild.NextSibling == null ? firstChild.InnerText : f.InnerText;

                        var startIndex = content.IndexOf(startToken);
                        if (startIndex == -1) return null;
                        return JsonToHtml(content, startIndex + startToken.Length, f.OwnerDocument);
                    })
                    .WhereNotNull();
                };
            });



            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("before-weak", condition =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var limit = condition(new[] { x }).FirstOrDefault();
                        if (limit == null) return x;
                        var node = x.OwnerDocument.CreateElement("fizzler-node-group");
                        foreach (var item in x.ChildNodes)
                        {
                            if (item == limit) break;
                            node.ChildNodes.Add(item);
                        }
                        return node;
                    });
                };
            });
            Parser.RegisterCustomSelector<HtmlNode>("distinct", () =>
            {
                return nodes =>
                {
                    return nodes.Distinct();
                };
            });
            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("except-children", condition =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var excluded = condition(new[] { x }).ToList();
                        var node = x.OwnerDocument.CreateElement("fizzler-node-group");
                        foreach (var item in x.ChildNodes)
                        {
                            if (!excluded.Contains(item))
                                node.ChildNodes.Add(item);
                        }
                        return node;
                    });
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("take-while", condition =>
            {
                return nodes =>
                {
                    return nodes.TakeWhile(x =>
                    {
                        return condition(new[] { x }).Any();
                    });
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("take-until", condition =>
            {
                return nodes =>
                {
                    return nodes.TakeWhile(x =>
                    {
                        return !condition(new[] { x }).Any();
                    });
                };
            });
            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("skip-while", condition =>
            {
                return nodes =>
                {
                    return nodes.SkipWhile(x =>
                    {
                        return condition(new[] { x }).Any();
                    });
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("skip-until", condition =>
            {
                return nodes =>
                {
                    return nodes.SkipWhile(x =>
                    {
                        return !condition(new[] { x }).Any();
                    });
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, int>("take", (count) =>
            {
                return nodes =>
                {
                    return nodes.Take(count);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, int>("take-last", (count) =>
            {
                return nodes =>
                {
                    var n = nodes.ToList();
                    if (count >= n.Count) return n;
                    n.RemoveRange(0, n.Count - count);
                    return n;
                };
            });


            Parser.RegisterCustomSelector<HtmlNode, int>("skip", (count) =>
            {
                return nodes =>
                {
                    return nodes.Skip(count);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, int>("skip-last", (count) =>
            {
                return nodes =>
                {
                    var n = nodes.ToList();
                    if (count >= n.Count) return Enumerable.Empty<HtmlNode>();
                    n.RemoveRange(n.Count - count, count);
                    return n;
                };
            });

            Parser.RegisterCustomSelector<HtmlNode>("heading-content", () =>
            {
                return nodes =>
                {
                    return nodes.Select(heading =>
                    {
                        var isdt = heading.TagName == "dt";

                        if (!isdt && !heading.IsHeading()) throw new FormatException("The matched heading element is not a <h*> or <dt> element.");

                        var originalLevel = isdt ? 0 : heading.TagName[1] - '0';

                        var group = heading.OwnerDocument.CreateElement("awdee_heading_group");

                        var node = heading.NextSibling;
                        while (node != null)
                        {
                            if (node != heading && (node.IsHeading() || node.TagName == "dt"))
                            {
                                if (isdt) break;
                                var level = node.TagName[1] - '0';
                                if (level <= originalLevel) break;
                            }
                            group.ChildNodes.Add(node);
                            node = node.NextSibling;
                        }

                        return group;
                    });
                };
            });

            //Parser.RegisterCustomSelector<HtmlNode>("no-nested", () =>
            //{
            //    return nodes =>
            //    {
            //        var list = new List<HtmlNode>();
            //        foreach (var item in nodes)
            //        {

            //        }
            //        return list;
            //    };
            //});

            //Parser.RegisterCustomSelector<HtmlNode, int, int, int>("rows", (skipFirst, skipLast, group) =>
            //{
            //    return nodes =>
            //    {
            //        return nodes.SelectMany(table =>
            //        {
            //            if (table.Name != "table" && table.Name != "tbody") throw new FormatException("The matched element must be a <table> or <tbody>");
            //            table = table.ChildNodes.FirstOrDefault(x => x.Name == "tbody") ?? table;
            //            var list = new List<HtmlNode>(table.ChildNodes.Count / group);
            //            var idx = 0;
            // TODO grouping
            //            foreach (var item in table.ChildNodes)
            //            {
            //                if (item.Name == "tr")
            //                {
            //                    if (skipFirst != 0) skipFirst--;
            //                    else list.Add(item);
            //                }
            //            }
            //            list.RemoveRange(list.Count - skipLast, skipLast);
            //            return list;
            //        });
            //    };
            //});


            Parser.RegisterCustomSelector<HtmlNode>("merge", () =>
            {
                return (IEnumerable<HtmlNode> nodes) =>
                {
                    var k = nodes.ToList();
                    if (k.Count == 0) return Enumerable.Empty<HtmlNode>();
                    var n = k[0].OwnerDocument.CreateElement("fizzler-node-group");
                    foreach (var item in k)
                    {
                        n.ChildNodes.Add(item);
                    }
                    return (IEnumerable<HtmlNode>)new[] { n };
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("take-next-siblings-while", condition =>
            {
                return (IEnumerable<HtmlNode> nodes) =>
                {
                    var p = nodes.FirstOrDefault();
                    if (p == null) return Enumerable.Empty<HtmlNode>();

                    var items = condition(p.RecursiveEnumeration(x => x.NextSibling));

                    var l = new List<HtmlNode>();
                    using (var enumerator = items.GetEnumerator())
                    {

                        foreach (var item in p.RecursiveEnumeration(x => x.NextSibling).Skip(1))
                        {
                            if (item.NodeType != HtmlNodeType.Element) continue;
                            if (!enumerator.MoveNext()) break;
                            if (enumerator.Current != item) break;
                            l.Add(item);
                        }

                    }


                    return l;
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>, Selector<HtmlNode>>("union", (first, second) =>
            {
                return (IEnumerable<HtmlNode> nodes) =>
                {
                    var p = nodes.ToList();



                    var firstItems = first(p);
                    var secondItems = second(p);


                    return firstItems.Union(secondItems);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>, Selector<HtmlNode>, Selector<HtmlNode>>("union3", (first, second, third) =>
            {
                return (IEnumerable<HtmlNode> nodes) =>
                {
                    var p = nodes.ToList();



                    var firstItems = first(p);
                    var secondItems = second(p);
                    var thirdItems = third(p);


                    return firstItems.Union(secondItems).Union(thirdItems);
                };
            });

            Parser.RegisterCustomSelector<HtmlNode, int>("nth-cell", cellNumber =>
            {
                return nodes =>
                {
                    return nodes.Select(row =>
                    {
                        if (row.TagName != "tr") throw new FormatException("The matched element must be a <tr>");
                        var i = 0;
                        foreach (var item in row.ChildNodes)
                        {
                            if (item.NodeType == HtmlNodeType.Element && (item.TagName == "td" || item.TagName == "th"))
                            {
                                i += item.TryGetNumericAttributeValue("colspan", 1);
                                if (i > cellNumber) return item;
                            }
                        }
                        return null;
                    })
                    .WhereNotNull();
                };

            });
            

            Parser.RegisterCustomSelector<HtmlNode, Selector<HtmlNode>>("without-subnodes", condition =>
            {
                return nodes =>
                {
                    var stack = new List<HtmlNode>();
                    return nodes.Select(x =>
                    {
                        var excluded = condition(new[] { x }).ToList();
                        if (excluded.Count == 0) return x;
                        var torebuild = new HtmlNodeHashSet(excluded);
                        stack.Clear();
                        stack.Add(x);
                        AddNodesToRemove(stack, torebuild);
                        stack.Clear();

                        return CloneWithout(x, torebuild, excluded);
                    });
                };
            });

            Parser.RegisterCustomSelector<HtmlNode>("reparse-comment", () =>
            {
                return nodes =>
                {
                    return nodes.Select(x =>
                    {
                        var comment = (HtmlCommentNode) x.ChildNodes.FirstOrDefault(y => y.NodeType == HtmlNodeType.Comment);
                        if (comment == null) return null;
                        var doc = CreateDocument(x.OwnerDocument);
                        var c = comment.Comment.AsValueString();
                        c = c.Substring(4);
                        c = c.Substring(0, c.Length - 3);
                        c = c.Trim();
                        return ReparseHtml(doc, c.ToClrString(), x.OwnerDocument);
                    }).WhereNotNull();
                };
            });
        }

        }
        

        private static bool IsNullOrWhiteSpace(string str)
        {
#if SALTARELLE || NET35
            if (str == null || str.Length == 0) return true;
            for (int i = 0; i < str.Length; i++)
            {
                var ch = str[i];
                if (!(ch == '\r' || ch == '\n' || ch == '\t' || ch == ' ' || ch == '\xA0')) return false;
            }
            return true;       
#else
            return string.IsNullOrWhiteSpace(str);
#endif
        }

        private static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T> items)
        {
            return items.Where(x => x != null);
        }


#if !SALTARELLE
        [RestrictedAccess]
#endif
        public class PartialStringReader : TextReader
        {
            private string text;
            private int index;
            public PartialStringReader(string text, int startIndex)
            {
                this.text = text;
                this.index = startIndex;
            }

            public override int Peek()
            {
                throw new NotSupportedException();
            }

            public override int Read()
            {
                if (index == text.Length) return -1;
                return (int)text[index++];
            }

#if !SALTARELLE




            protected override void Dispose(bool disposing)
            {
                this.text = null;
                base.Dispose(disposing);
            }
#else
            public override void Close()
            {
                Dispose();
            }
            public override void Dispose()
            {
                Close();
            }

            public override int Read(char[] buffer, int index, int count)
            {
                throw new NotSupportedException();
            }

            public override int ReadBlock(char[] buffer, int index, int count)
            {
                throw new NotSupportedException();
            }

            public override string ReadLine()
            {
                throw new NotSupportedException();
            }

            public override string ReadToEnd()
            {
                if (index == 0) return text;
                return text.Substring(index);
            }
#endif
        }
    }
}
