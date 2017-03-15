using Newtonsoft.Json;
using Shaman.Dom;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HttpStatusCode = System.Net.HttpStatusCode;
using WebExceptionStatus = System.Net.WebExceptionStatus;
using System.Diagnostics;
#if NATIVE_HTTP
using System.Net.Http;
using System.Net;
#else
using System.Net.Reimpl.Http;
using System.Net.Reimpl;

#endif
namespace Shaman.Runtime
{
    public static class Caching
    {
        [RestrictedAccess]
        public static string GetWebCachePath(LazyUri url)
        {
            return GetWebCachePath(url, false, false);
        }

        internal static string GetWebCachePath(LazyUri url, bool hasExtraOptions, bool createLng)
        {
            if (!hasExtraOptions)
            {
                return Caching.GetFileSystemName(url, ThreadWebCache ?? WebCachePath, ".awc", createLng, false);
            }

            return null;
        }

        internal static HtmlNode TryReadFromCache(string cachePath, LazyUri url, WebRequestOptions cookieDestination)
        {
            var data = Caching.TryReadCacheFile(cachePath);
            if (data == null)
                return null;
            if (data.ExceptionType == null || !Caching.IgnoreCachedFailedRequests)
            {
                Utils.RaiseWebRequestEvent(url, true);
                return data.RecreateNode(url, cookieDestination, cachePath);
            }

            return null;
        }

#if STANDALONE
        internal static IEnumerable<T> RecursiveEnumeration<T>(this T first, Func<T, T> parent)
        {
            var current = first;

            while (current != null)
            {
                yield return current;
                current = parent(current);

            }

        }
        
#endif

        internal static WebCache GetWebCacheForException(Exception ex, LazyUri respondingUrl, long responseSize)
        {
#if DESKTOP && !CORECLR
            if (ex.RecursiveEnumeration(x => x.InnerException).Any(x => x is ThreadAbortException))
                return null;
#endif
            ex = ex.RecursiveEnumeration(x => x.InnerException).FirstOrDefault(x => (x is WebException) || (x is TimeoutException) || (x is NotSupportedResponseException)) ?? ex.RecursiveEnumeration(x => x.InnerException).SkipWhile(x => x is AggregateException).FirstOrDefault() ?? ex;
            int status = 0;
            var webex = ex as WebException;
            if (webex != null)
            {
                status = (int)webex.GetResponseStatusCode();
                if (status == 0)
                    status = (int)webex.Status;
            }

            var unexpectedContentType = ex as NotSupportedResponseException;
            return new WebCache()
            {
                ErrorCode = status,
                ExceptionMessage = ex.Message,
                ExceptionType = ex.GetType().FullName,
                ContentType = unexpectedContentType != null ? unexpectedContentType.ContentType : null,
                RedirectUrl = respondingUrl
            };
        }

        internal static WebCache TryReadCacheFile(string path, bool onlyIfFailed = false, bool fromFileSystem = false)
        {
#if STANDALONE
            HttpUtils.EnsureInitialized();
#else
            Utils.EnsureInitialized();
#endif
            Stream stream;
            if (fromFileSystem)
            {
                if (!File.Exists(path))
                    return null;
                try
                {
                    stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read);
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }
            else
            {
                if (!BlobStore.Exists(path))
                    return null;
                try
                {
                    stream = BlobStore.OpenRead(path);
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }

            Sanity.AssertFastReadByte(stream);

            using (stream)
            {
                var q = stream.ReadByte();
                if (q == 0xEF)
                {
                    stream.ReadByte();
                    stream.ReadByte();
                    using (var sr = new StreamReader(stream, Encoding.UTF8))
                    {
                        var qq = JsonConvert.DeserializeObject<WebCache>(sr.ReadToEnd());
                        stream.Dispose();
                        SaveCache(path, qq);
                        return qq;
                    }
                }

                if (q != 0x1F)
                    throw new ArgumentException("Invalid cache file.");
                stream.Seek(0, SeekOrigin.Begin);
                var gz = new GZipStream2(stream, CompressionMode.Decompress);
                q = gz.ReadByte();
                if (q < 50 || q > 80)
                    throw new ArgumentException("Invalid cache file.");
                using (var br = new BinaryReader(gz, Encoding.UTF8))
                {
                    Sanity.AssertFastReadByte(br.BaseStream);
                    var cache = new WebCache();
                    cache.ContentType = br.ReadNullableString();
                    cache.DateRetrieved = new DateTime(br.ReadInt64(), DateTimeKind.Utc);
                    cache.ErrorCode = br.ReadInt32();
                    cache.ExceptionMessage = br.ReadNullableString();
                    cache.ExceptionType = br.ReadNullableString();
                    if (onlyIfFailed && cache.ExceptionType == null)
                        return null;
                    var headerCount = br.ReadInt32();
                    cache.Headers = new Dictionary<string, string>(headerCount);
                    for (int i = 0; i < headerCount; i++)
                    {
                        var name = br.ReadString();
                        var value = br.ReadString();
                        cache.Headers[name] = value;
                    }

                    var cookieCount = br.ReadInt32();
                    cache.Cookies = new Dictionary<string, string>(cookieCount);
                    for (int i = 0; i < cookieCount; i++)
                    {
                        var name = br.ReadString();
                        var value = br.ReadString();
                        cache.Cookies[name] = value;
                    }

                    cache.DataType = (WebCacheDataType)br.ReadByte();
                    cache.RedirectUrl = br.ReadNullableString()?.AsLazyUri();
                    var p = br.ReadNullableString();
                    cache.Url = p != null ? new LazyUri(p) : null;
                    cache.Result = br.ReadNullableString();
                    if (q >= 51)
                    {
                        cache.JsExecutionResults = br.ReadNullableString();
                        if (q >= 52)
                        {
                            var pp = br.ReadNullableString();
                            cache.PageUrl = pp != null ? new LazyUri(pp) : null;
                        }
                    }

                    return cache;
                }
            }
        }
#if !STANDALONE
        internal static Uri GetAzureUrl(LazyUri url)
        {
            var container = GetAzureContainer(url);
            var name = GetFileCachePath(url);
            return AzureApi.GetUrlForBlob(container, name);
        }


        internal static async Task<HttpResponseMessage> GetAzureResponseAsync(string container, string name, long startPosition, Shaman.Types.WebFile file)
        {
            var options = new WebRequestOptions()
            { Timeout = 30000, TimeoutSecondRetrialAfterError = 10000, TimeoutStartSecondRetrial = null };
            if (startPosition != 0)
                options.AddHeader("Range", "bytes=" + startPosition + "-");
            var response = (await ExtensionMethods.SendAsync(new LazyUri(AzureApi.GetUrlForBlob(container, name)), options, null)).Response;
            IEnumerable<string> errs;
            if (response.Headers.TryGetValues("x-ms-meta-err", out errs))
            {
                throw MediaStream.ExceptionFromCachedResponse(errs.First());
            }

            return response;
        }

#endif
        public static bool IgnoreCachedFailedRequests
        {
            get;
            set;
        }

        [StaticFieldCategory(StaticFieldCategory.TODO)]
        private static Stopwatch lastFlush;

        internal static void SaveCache(string cachePath, WebCache webCache)
        {
            if (cachePath == null || webCache == null)
                return;

            using (var stream = BlobStore.OpenWrite(cachePath))
            {
                using (var gz = new GZipStream2(stream, CompressionMode.Compress))
                using (var bw = new BinaryWriter(gz, Encoding.UTF8))
                {
                    Sanity.AssertFastWriteByte(gz.BaseStream);
                    bw.Write((byte)52);
                    bw.WriteNullableString(webCache.ContentType);
                    bw.Write(webCache.DateRetrieved.Ticks);
                    bw.Write(webCache.ErrorCode);
                    bw.WriteNullableString(webCache.ExceptionMessage);
                    bw.WriteNullableString(webCache.ExceptionType);
                    bw.Write(webCache.Headers != null ? webCache.Headers.Count : 0);
                    if (webCache.Headers != null)
                    {
                        foreach (var item in webCache.Headers)
                        {
                            bw.Write(item.Key);
                            bw.Write(item.Value);
                        }
                    }

                    bw.Write(webCache.Cookies != null ? webCache.Cookies.Count : 0);
                    if (webCache.Cookies != null)
                    {
                        foreach (var item in webCache.Cookies)
                        {
                            bw.Write(item.Key);
                            bw.Write(item.Value);
                        }
                    }

                    bw.Write((byte)webCache.DataType);
                    bw.WriteNullableString(webCache.RedirectUrl != null ? webCache.RedirectUrl.AbsoluteUri : null);
                    bw.WriteNullableString(webCache.Url != null ? webCache.Url.AbsoluteUri : null);
                    bw.WriteNullableString(webCache.Result);
                    bw.WriteNullableString(webCache.JsExecutionResults);
                    bw.WriteNullableString(webCache.PageUrl?.AbsoluteUri);
                }
            }

            if (lastFlush == null) lastFlush = Stopwatch.StartNew();
            else if (lastFlush.ElapsedMilliseconds > Configuration_FlushIntervalMs)
            {
                BlobStore.FlushDirectory(Path.GetDirectoryName(cachePath));
#if NET35
                lastFlush.Stop();
                lastFlush.Start();
#else
                lastFlush.Restart();
#endif
            }

        }


        [Configuration]
        private readonly static int Configuration_FlushIntervalMs = 30000;

        private static string ReadNullableString(this BinaryReader br)
        {
            if (br.ReadBoolean())
            {
                return br.ReadString();
            }

            return null;
        }

        private static void WriteNullableString(this BinaryWriter bw, string str)
        {
            bw.Write(str != null);
            if (str != null)
                bw.Write(str);
        }

        internal static Exception RebuildException(WebCache data, LazyUri url)
        {
            var unexpectedResponseType = data.ExceptionType == typeof(NotSupportedResponseException).FullName;
            if (unexpectedResponseType)
            {
                return new NotSupportedResponseException(data.ContentType, data.RedirectUrl);
            }

            var webex = data.ExceptionType == "System.Net.WebException" || data.ExceptionType == "System.Net.Reimpl.WebException";
            if (!webex)
            {
                try
                {
                    if (data.ExceptionType == "Shaman.Runtime.NotSupportedResponseException")
                        data.ExceptionType = typeof(NotSupportedResponseException).FullName;
                    var type = new[] { typeof(int).GetTypeInfo().Assembly, typeof(Uri).GetTypeInfo().Assembly }
                    .Select(x => x.GetType(data.ExceptionType)).FirstOrDefault(x => x != null);
                    return type != null ? (Exception)Activator.CreateInstance(type) : new WebException(data.ExceptionMessage + ": " + data.ExceptionType);
                }
                catch (Exception)
                {
                }
            }

            if (data.ErrorCode != 0 || webex)
            {
                var s = (HttpStatusCode)data.ErrorCode;
                var w = new WebException(data.ExceptionMessage, (WebExceptionStatus)(int)s);
                SetDummyResponseWithUrl(w, url, s);
                return w;
            }

            return new WebException(data.ExceptionMessage + " (" + data.ExceptionType + ")");
        }

        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        internal static string WebCachePath;
        internal static void SetDummyResponseWithUrl(WebException w, LazyUri url, HttpStatusCode statusCode)
        {
#if DESKTOP
            if (w != null && url != null && w.Response == null)
            {
                var respfield = typeof(WebException).GetField("response", BindingFlags.Instance | BindingFlags.NonPublic); // m_Response on netfx
                if (respfield == null)
                    return;
#if CORECLR
                Sanity.NotImplementedButTryToContinue();
                return;
#else
                var response = FormatterServices.GetUninitializedObject(typeof(HttpWebResponse));
                var uriField = typeof(HttpWebResponse).GetField("uri", BindingFlags.Instance | BindingFlags.NonPublic); // m_Uri on netfx
                if (uriField == null)
                    return;

                var statusField = typeof(HttpWebResponse).GetField("statusCode", BindingFlags.Instance | BindingFlags.NonPublic); // m_StatusCode on netfx
                if (statusField == null)
                    return;
                statusField.SetValue(response, statusCode);
                uriField.SetValue(response, url.Url);
                respfield.SetValue(w, response);
#endif
            }
#endif
        }


        [RestrictedAccess]
        public static void EnableWebCache(string path)
        {
            if (WebCachePath != null && (path != WebCachePath && path != null))
            {
                BlobStore.FlushDirectory(WebCachePath);
                BlobStore.CloseDirectory(WebCachePath);
            }
            if (path != null)
            { 
                path = Path.GetFullPath(path);
                Directory.CreateDirectory(path);
            }
            WebCachePath = path;
        }

        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        internal static string fileCachePath;
        public static string FileCachePath
        {
            [RestrictedAccess]
            get
            {
                return fileCachePath;
            }

            [RestrictedAccess]
            set
            {
                if (value != null && !value.StartsWith("azure:")) value = Path.GetFullPath(value);
                if (value != fileCachePath)
                {
                    CachedFiles = null;
                }

                fileCachePath = value;
#if !STANDALONE
                if (value != null)
                {
                    if (value.StartsWith("azure:"))
                        InitAzureStorageAccount(value.Substring("azure:".Length));
                    else
                        AzureApi = null;
                }
#endif
            }
        }

#if !STANDALONE
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InitAzureStorageAccount(string v)
        {
            AzureApi = (ICompactAzureApi)Activator.CreateInstance(AzureApiTypeImpl);
            AzureApi.Init(v);
            // Once per blob client
            //var props = blob.GetServiceProperties();
            //props.DefaultServiceVersion = "2014-02-14";
            //blob.SetServiceProperties(props);
            // return blob;
        }
#endif
        [ThreadStatic]
        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        private static string ThreadWebCache;

        private const int CacheFileNameMaxLength = 150;
        private const int ShamanBlobsPackageNameLength = 50; // 49 actually, just to be sure
        private const int CacheFolderMaxLength = 255 - ShamanBlobsPackageNameLength - 3 - 1 - 1;
        public static string GetPath(LazyUri url, string folder, string extension = null)
        {
            return GetFileSystemName(url, folder, extension ?? ".dat", true, false);
        }

        internal static string GetFileSystemName(LazyUri url, string cacheFolder, string extension, bool createLng, bool partition = true)
        {
            var hashed = false;
            var isazure = cacheFolder != null && cacheFolder.StartsWith("azure:");
            if (cacheFolder == null)
                return null;
            if (!isazure && cacheFolder.Length > CacheFolderMaxLength)
                throw new ArgumentException("The path of the file cache folder must not be longer than " + CacheFolderMaxLength + " characters.");
            
            var str = url.AbsoluteUri;
            var hashcode = Math.Abs((long)str.GetHashCode());
            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();
            if (url.Scheme != "http" && url.Scheme != "https")
                throw new NotSupportedException("URI scheme is not supported.");
            var https = url.Scheme == "https";
            sb.Append((string)url.DnsSafeHost);
            if (!url.IsDefaultPort)
            {
                sb.Append("∴");
                sb.AppendFast((int)url.Port);
            }

            sb.Append(https ? "₰" : "ℓ");
            var abspath = url.AbsolutePath;
            sb.Append(abspath, 1, abspath.Length - 1);
            sb.Append((string)url.Query);
            sb.Append((string)url.Fragment);
            if (sb.Length <= CacheFileNameMaxLength)
            {
                FixupFabulousUrl(sb);
                foreach (var item in Path.GetInvalidFileNameChars())
                {
                    if (sb.IndexOf(item) != -1)
                    {
                        sb.Replace(item.ToString(), "℅" + ((int)item).ToString("X2"));
                    }
                }

                sb.Append(extension);
            }

            var folder = isazure ? null : partition ? Path.Combine(cacheFolder, (hashcode % 1000).ToString("000")) : cacheFolder;
            if (sb.Length > CacheFileNameMaxLength)
            {
#if NET35
                sb.Length = 0;
#else
                sb.Clear();
#endif
                sb.Append(url.DnsSafeHost.TrimSize(60, 0, false));
                if (!url.IsDefaultPort)
                {
                    sb.Append("∴");
                    sb.AppendFast((int)url.Port);
                }

                sb.Append(https ? "₰" : "ℓ");
                sb.Append("↔");
                using (var hashAlgorithm =
#if NATIVE_HTTP
                    System.Security.Cryptography.SHA256.Create()
#else
                    System.Security.Cryptography.Reimpl.SHA256.Create()
#endif
                    )
                {
                    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(str);
                    byte[] hash = hashAlgorithm.ComputeHash(inputBytes);
                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("x2"));
                    }
                }

                // IPv6 addresses
                FixupFabulousUrl(sb);
                if (!isazure)
                    sb.Append(extension);
                hashed = true;
            }

            if (isazure)
            {
                sb.Length -= 4; // remove .dat
                sb.Replace("₰", "/s/");
                sb.Replace("ℓ", "/h/");
                sb.Replace("↑", "");
                sb.Replace('\u222F', '/'); // ∯
                return ReseekableStringBuilder.GetValueAndRelease(sb);
            }

            var path = Path.Combine(folder, ReseekableStringBuilder.GetValueAndRelease(sb));
            if (createLng)
            {
                Directory.CreateDirectory(folder);
                if (hashed)
                {
                    var p = Path.ChangeExtension(path, ".lng");
                    if (!BlobStore.Exists(p))
                        BlobStore.WriteAllText(p, (string)url.AbsoluteUri, Encoding.UTF8);
                }
            }

            return path;
        }

        internal static string GetCachePathUri(string cache)
        {
            return "file:///" + cache.Replace('\\', '/');
        }

        private static void FixupFabulousUrl(StringBuilder sb)
        {
            sb.Replace(':', '\u223A'); // ∺
            sb.Replace('/', '\u222F'); // ∯
            sb.Replace('?', '\u203D'); // ‽
            for (int i = 0; i < sb.Length; i++)
            {
                var ch = sb[i];
                if (char.IsUpper(ch))
                {
                    sb.Insert(i, '\u2191'); // ↑
                    i++;
                }
            }
        }

        [RestrictedAccess]
        public static void SetThreadWebCacheFolder(string webCacheFolder)
        {
            ThreadWebCache = webCacheFolder;
        }

        internal static bool IsWebCacheEnabled()
        {
            return ThreadWebCache != null || WebCachePath != null;
        }

        public static IEnumerable<KeyValuePair<LazyUri, string>> GetCachedFiles()
        {
            return GetCachedFiles(FileCachePath, ".dat");
        }

        public static IEnumerable<KeyValuePair<LazyUri, string>> GetCachedResponses()
        {
            return GetCachedFiles(ThreadWebCache ?? WebCachePath, ".awc");
        }

#if !STANDALONE
        public static async Task RetryFailedRequestsAsync()
        {
            await GetCachedResponses().ForEachThrottledAsync(async item =>
            {
                var c = TryReadCacheFile(item.Value, true);
                if (c != null && c.ExceptionType != null)
                {
                    DeleteWebCache(item.Key);
                    try
                    {
                        var dummy = await item.Key.GetHtmlNodeAsync();
                    }
                    catch (Exception ex)
                    {
                        Sanity.ReportError(ex, ErrorCategory.CacheRetry);
                    }
                }
            }

            , Configuration_RetryFailedRequestsConcurrency);
        }
#endif
        [Configuration]
        private readonly static int Configuration_RetryFailedRequestsConcurrency = 4;
        public static IEnumerable<KeyValuePair<LazyUri, string>> GetCachedFiles(string cachedir, string extension)
        {
            if (cachedir.StartsWith("azure:")) throw new NotSupportedException();
            foreach (var dir in new[] { cachedir })
            {
                if (extension == ".awc")
                {
                    foreach (var file in BlobStore.EnumerateFiles(dir, "*" + extension))
                    {
                        var url = GetUrlFromPath(file.Path, true);
                        yield return new KeyValuePair<LazyUri, string>(url, file.Path);
                    }
                }
                else {
#if NET35
                    foreach (var file in Directory.GetFiles(dir, "*" + extension))
#else
                    foreach (var file in Directory.EnumerateFiles(dir, "*" + extension))
#endif

                    {
                        var url = GetUrlFromPath(file, true);
                        yield return new KeyValuePair<LazyUri, string>(url, file);
                    }
                }
            }
        }
        public static LazyUri GetUrlFromPath(string file)
        {
            return GetUrlFromPath(file, false);
        }

        public static LazyUri GetUrlFromPath(string file, bool tryOnly)
        {
            var name = Path.GetFileName(file);
            if (name.Contains("↔"))
            {
                try
                {
                    return BlobStore.ReadAllText(Path.ChangeExtension(file, ".lng")).AsLazyUri();
                }
                catch when (!tryOnly)
                {
                    return null;
                }
            }
            else
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var sb = new StringBuilder(fileName);
                sb.Replace("↑", string.Empty);
                sb.Replace('\u203D', '?'); // ‽
                sb.Replace('\u222F', '/'); // ∯
                sb.Replace('\u223A', ':'); // ∺
                sb.Replace('\u2234', ':'); // ∴
                if (fileName.Contains('\u20B0')) // ₰
                {
                    sb.Insert(0, "https://");
                    sb.Replace('\u20B0', '/'); // ₰
                }
                else if (fileName.Contains('\u2113')) // ℓ
                {
                    sb.Insert(0, "http://");
                    sb.Replace('\u2113', '/'); // ℓ
                }
                else if (tryOnly) return null;
                else throw new FormatException();
                var str = sb.ToString();
                if (str.Contains('\u2105')) // ℅
                {
                    str = Regex.Replace(str, @"℅[0-9a-f]{2}", x =>
                    {
                        var ch = int.Parse(x.Value.Substring(1), NumberStyles.HexNumber);
                        return ((char)ch).ToString();
                    }

                    );
                }

                return str.AsLazyUri();
            }
        }

        public static void DeleteWebCache(LazyUri url)
        {
            var task = DeleteFileCacheAsync(url, ThreadWebCache ?? WebCachePath, ".awc", false);
            if (!task.IsCompleted && !task.IsFaulted && !task.IsCanceled) throw new Exception("Task did not complete synchronosly.");
            task.GetAwaiter().GetResult();
        }

        public static Task DeleteFileCacheAsync(LazyUri url)
        {
            return DeleteFileCacheAsync(url, FileCachePath, ".dat", true);
        }

        [StaticFieldCategory(StaticFieldCategory.TODO)]
        internal static Dictionary<string, Task<HashSet<string>>> CachedFiles;
        private static async Task DeleteFileCacheAsync(LazyUri url, string cachedir, string extension, bool fileCache)
        {
            if (cachedir == null)
                throw new InvalidOperationException("The cache path is not configured.");
            var path = GetFileSystemName(url, cachedir, extension, false, false);
#if !STANDALONE
            if (fileCache && AzureApi != null)
            {
                await DeleteAzureBlobAsync(url);
            }
            else
#endif
            {
                BlobStore.Delete(path);
                if (path.Contains('\u2194')) // ↔
                {
                    BlobStore.Delete(Path.ChangeExtension(path, ".lng"));
                }

                BlobStore.Delete(Path.ChangeExtension(path, ".err"));
            }
        }

#if !STANDALONE
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task DeleteAzureBlobAsync(LazyUri url)
        {
            var c = GetAzureContainer(url);
            var m = await GetAzureCachedFiles(c);
            var name = Caching.GetFileCachePath(url);
            if (m.Contains(name))
            {
                await AzureApi.DeleteBlob(c, name);
            }
        }

        internal static Task<HashSet<string>> GetAzureCachedFiles(string container)
        {
            ObjectManager.AssertMainThread();
            if (CachedFiles == null)
                CachedFiles = new Dictionary<string, Task<HashSet<string>>>();
            var m = CachedFiles.TryGetValue(container);
            if (m == null)
            {
                m = AzureApi.GetAzureCachedFilesInternal(container);
                CachedFiles[container] = m;
            }

            return m;
        }

        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        internal static ICompactAzureApi AzureApi;
        [StaticFieldCategory(StaticFieldCategory.Configuration)]
        internal static Type AzureApiTypeImpl;
        internal static string GetAzureContainer(LazyUri url)
        {
            var p = GetAzureContainerInternal(url).ToLowerFast().Replace('.', '-');
            return p.TrimSize(40);
        }

        private static string GetAzureContainerInternal(LazyUri url)
        {
            var host = url.DnsSafeHost;
            var p = host.SplitFast('.');
            if (p.Length == 3 && p[0] == "www")
                return p[1] + "." + p[2];
            if (p.Length <= 3)
                return host;
            return string.Join(".", p.Skip(p.Length - 3));
        }
#endif
        [RestrictedAccess]
        public static string GetFileCachePath(LazyUri url, bool createLng = false)
        {
            return GetFileSystemName(url, FileCachePath, ".dat", createLng);
        }

#if !STANDALONE
        internal static string GetPrefetchedFilePath(LazyUri url, bool checkExistence)
        {
            if (Caching.AzureApi != null)
                Sanity.NotImplemented();
            var path = Caching.GetFileCachePath(url);
            if (checkExistence)
            {
                if (!BlobStore.Exists(path)) return null;
                if (BlobStore.GetLength(path) == 0)
                {
                    if (BlobStore.Exists(Path.ChangeExtension(path, ".err")))
                        return null;
                }
            }

            return path;
        }
#endif

    }
}