using System;
using System.IO;
using System.Reflection;
using System.Linq;
#if NATIVE_HTTP
using System.Net;
using System.Net.Http;
#else
using System.Net.Reimpl;
using System.Net.Reimpl.Http;
#endif
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Shaman.Runtime;
using System.Threading;
using Newtonsoft.Json;
#if !STANDALONE
using HttpExtensionMethods = Shaman.ExtensionMethods;
using HttpUtils = Shaman.Utils;
#endif
#if NET35
using HttpResponseMessage = System.Net.HttpWebResponse;
#else
using TaskEx = System.Threading.Tasks.Task;
#endif

namespace Shaman.Types
{
    /// <summary>
    /// Represents a file at a specific URL.
    /// </summary>
    [JsonConverter(typeof(WebFileConverter))]
    public class WebFile
#if !STANDALONE 
    : IExceptions, INotifyChanged
#endif
    {

#if !STANDALONE
        [StaticFieldCategory(StaticFieldCategory.Cache)]
        internal readonly static WeakDictionary<string, WebFile> files = new WeakDictionary<string, WebFile>();


        internal static WebFile FromDbValue(WebFile file, string content, TypeBase type)
        {
            /*if (url == null) return null;
            var u = url.AsUri();
            var file =
                type == TypeBase.WebImage ? WebImage.FromUrl(u) :
                type == TypeBase.WebAudio ? WebAudio.FromUrl(u) :
                type == TypeBase.WebVideo ? WebVideo.FromUrl(u) :
                WebFile.FromUrl(u);
                */
            if (content != null)
            {
                var serializer = new JsonSerializer();

                serializer.AddAwdeeConverters();
                using (var sr = new StringReader(content))
                using (var jr = Utils.CreateJsonReader(sr))
                {
                    serializer.Deserialize(jr, type.NativeType);
                }

            }

            return file;
        }
#endif

        internal class WebFileConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return typeof(WebFile).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                var urlString = (string)reader.Value;
                var url = urlString.AsUri();
                return
#if !STANDALONE
                    objectType == typeof(WebAudio) ? WebAudio.FromUrl(url) :
                    objectType == typeof(WebVideo) ? WebVideo.FromUrl(url) :
                    objectType == typeof(WebImage) ? WebImage.FromUrl(url) :
#endif
                    WebFile.FromUrl(url);

            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                writer.WriteValue(((WebFile)value).Url);
            }
        }

        protected WebFile(Uri url)
        {
            this.Url = url;
        }


        internal void SaveResponseInfo(HttpResponseMessage partialDownload, bool continueDownload)
        {
            if (partialDownload != null)
            {
#if NET35
                var len = partialDownload.ContentLength == -1 ? null : (long?)partialDownload.ContentLength;
#else
                var len = partialDownload.Content.Headers.ContentLength;
#endif
                if (len != null) Size = new FileSize(len.Value);

// HACK: ignore content disposition in .net 35
#if !NET35
                var contentDisposition = partialDownload.Content.Headers.ContentDisposition;
                if (contentDisposition != null) contentDispositionFileName = contentDisposition.FileName;
#endif

                contentTypeExtension = HttpUtils.GetFileExtensionFromMime(
#if NET35
                    HttpUtils.GetMimeFromContentType(partialDownload.Headers["Content-Type"])
#else
                    partialDownload.Content.Headers.ContentType?.MediaType
#endif
                );

                if (continueDownload && (manager == null || !manager.IsAlive))
                {
                    this.partialDownload = partialDownload;
                    manager = new MediaStreamManager(GetResponseAsync, true);
                }
                else
                {
                    partialDownload.AbortAndDispose();
                }
                OnChanged();
            }
        }

        private HttpResponseMessage partialDownload;

        public Uri Url { get; private set; }

        private string contentDispositionFileName;
        private string contentTypeExtension;

        public FileSize? Size { get; private set; }


        public override string ToString()
        {
            return Url.ToString();
        }


        public string Extension
        {
            get
            {
                var ext = Path.GetExtension(SuggestedFileName);
                return string.IsNullOrEmpty(ext) ? null : ext;
            }
        }

        public string SuggestedFileName
        {
            get
            {
                return HttpUtils.GetSuggestedFileName(Url, contentDispositionFileName, contentTypeExtension);
            }
        }

        public override bool Equals(object obj)
        {
            var other = obj as WebFile;
            if (other == null) return false;
            return HttpUtils.UrisEqual(other.Url, this.Url);
        }


        public override int GetHashCode()
        {
            return Url.GetHashCode();
        }

        private MediaStreamManager manager;

        public Stream OpenStream()
        {
#if !STANDALONE
            if (SynchronizationContext.Current == ObjectManager.SynchronizationContext)
                throw new InvalidOperationException("Cannot call the synchronous version of WebFile.OpenStream() from the main thread.");
#endif
            return OpenStreamAsync(true).AssumeCompleted();
        }

        public Task<Stream> OpenStreamAsync()
        {
            return OpenStreamAsync(false);
        }

        internal async Task<Stream> OpenStreamAsync(bool synchronous, bool skipCache = false, bool linger = true)
        {


            var url = new LazyUri(Url);
            await Utils.CheckLocalFileAccessAsync(url);

            var mgr = this.manager;
            Func<long, Task<HttpResponseMessage>> createStream = null;

#if !STANDALONE && DESKTOP
            if (Caching.AzureApi != null && (mgr == null || !mgr.IsAlive) && !skipCache)
            {
                var container = Caching.GetAzureContainer(url);
                HashSet<string> files = null;
                if (synchronous)
                {
                    ObjectManager.SynchronizationContext.Send(async () =>
                    {
                        files = await Caching.GetAzureCachedFiles(container);
                    });
                }
                else
                {
                    await ObjectManager.SynchronizationContext.SendAsync(async () =>
                    {
                        files = await Caching.GetAzureCachedFiles(container);
                    });

                }
                var name = Caching.GetFileCachePath(url);
                if (files.Contains(name))
                {
                    createStream = offset => Caching.GetAzureResponseAsync(container, name, offset, this);
                }
            } else 
#endif
             if (
#if !STANDALONE && DESKTOP
                Caching.AzureApi == null && 
#endif
                !skipCache)
            {
#if DESKTOP
                var cache = Caching.GetFileCachePath(url);

                if (File.Exists(cache))
                {
                    var str = new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    if (str.Length == 0)
                    {
                        var errfile = Path.ChangeExtension(cache, ".err");
                        if (File.Exists(errfile))
                        {
                            str.Dispose();
                            var errlines = File.ReadAllText(errfile);
                            return new MediaStream(MediaStream.ExceptionFromCachedResponse(errlines), this);
                        }
                    }
                    Sanity.AssertFastReadByte(str);
                    return str;
                }
#endif
            }

            lock (this)
            {
                if (manager == null)
                    manager = new MediaStreamManager(createStream ?? GetResponseAsync, true);

                var stream = manager.TryCreateStream(this, 0, linger);
                if (stream == null)
                {
                    manager = new MediaStreamManager(createStream ?? GetResponseAsync, true);
                    stream = manager.TryCreateStream(this, 0, linger);
                    Sanity.Assert(stream != null);
                }
                Sanity.AssertFastReadByte(stream);
                return stream;
            }

        }

        [RestrictedAccess]
        public async Task<HttpResponseMessage> GetResponseAsync(long startPosition)
        {
            if (partialDownload != null && startPosition == 0)
            {
                var c = partialDownload;
                partialDownload = null;
                return c;
            }
            var options = new WebRequestOptions()
            {
                Timeout = 30000,
                TimeoutSecondRetrialAfterError = 10000,
                TimeoutStartSecondRetrial = null
            };
            var url = new LazyUri(this.Url);
            HttpExtensionMethods.ProcessMetaParameters(url, options);
            if (startPosition != 0) options.AddHeader("Range", "bytes=" + startPosition + "-");
            return await HttpExtensionMethods.GetResponseAsync(url, options);
            //return (await HttpExtensionMethods.SendAsync(url, options, null)).Response;
        }


        public virtual Exception Error
        {
            get { return manager != null ? manager.Error : null; }
        }

        public virtual void ResetError()
        {
            manager = null;
#if DESKTOP
            prefetchingTask = null;
            var z = new LazyUri(Url);
            var cache = Caching.GetFileCachePath(z);
            if (cache != null)
            {
                Caching.DeleteFileCacheAsync(z).FireAndForget();
            }
#endif
            OnChanged();
        }


#if DESKTOP
        [RestrictedAccess]
        public async Task DownloadAsync(string destination, CancellationToken ct, IProgress<DataTransferProgress> progress)
        {
            var temp = MaskedFile.GetMaskedPathFromFile(destination);
            try
            {
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.Delete))
                using (var stream = await OpenStreamAsync())
                {
                    var mediaStream = stream as MediaStream;
                    if (progress != null) progress.Report(new DataTransferProgress(default(FileSize), null, default(FileSize)));
                    var buffer = new byte[4096];
                    var transferredBytes = 0L;
                    while (true)
                    {
                        var readBytes = await stream.ReadAsync(buffer, 0, buffer.Length);
                        transferredBytes += readBytes;
                        var t = new FileSize(transferredBytes);
                        if (progress != null && mediaStream != null) progress.Report(new DataTransferProgress(t, mediaStream.Size, mediaStream.DataPerSecond));
                        if (readBytes == 0)
                        {
                            break;
                        }
                        await fs.WriteAsync(buffer, 0, readBytes);
                    }
                }
                MaskedFile.PublishMaskedFile(temp, destination);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        [RestrictedAccess]
        public Task<string> DownloadAsync(string destinationFolder, string unsanitizedFileName, FileOverwriteMode mode)
        {
            return DownloadAsync(destinationFolder, unsanitizedFileName, mode, CancellationToken.None, null);
        }


        [RestrictedAccess]
        public async Task<string> DownloadAsync(string destinationFolder, string unsanitizedFileName, FileOverwriteMode mode, CancellationToken ct, IProgress<DataTransferProgress> progress)
        {
            Directory.CreateDirectory(destinationFolder);
            string path;
            if (mode == FileOverwriteMode.Rename)
            {
                path = FileSystem.GetUniqueFileName(destinationFolder, unsanitizedFileName);
            }
            else
            {
                path = FileSystem.SanitizeFileName(destinationFolder, unsanitizedFileName);
                if (File.Exists(path))
                {
                    if (mode == FileOverwriteMode.Error) throw new ArgumentException("File " + path + " already exists.");
                    else if (mode == FileOverwriteMode.Skip) return path;
                }
                if (mode == FileOverwriteMode.GenerateNameOnly) return path;
            }
            await DownloadAsync(path, ct, progress);
            return path;
        }

#endif


        public enum FileOverwriteMode
        {
            Error,
            Skip,
            Overwrite,
            Rename,
            GenerateNameOnly
        }


        public static WebFile FromUrl(Uri url)
        {
            return FromUrl(url, null, false);
        }

#if !STANDALONE

        public static WebFile FromUrlUntracked(Uri url)
        {
            return FromUrlUntracked(url, null, false);
        }
#endif



        public static WebFile FromUrl(Uri url, HttpResponseMessage partialResponse, bool continueDownload)
        {
#if STANDALONE
            var existing = new WebFile(url);
#else
            ObjectManager.AssertMainThread();

            var existing = files[url.AbsoluteUri];
            if (existing == null)
            {
                existing = new WebFile(url);
                files[url.AbsoluteUri] = existing;
            }
#endif

            existing.SaveResponseInfo(partialResponse, continueDownload);
            return existing;
            
        }


#if !STANDALONE
        public static WebFile FromUrlUntracked(Uri url, HttpResponseMessage partialResponse, bool continueDownload)
        {
            var existing = new WebFile(url);
            existing.SaveResponseInfo(partialResponse, continueDownload);
            return existing;

        }
#endif

        protected static void Initialize(WebFile existing, WebFile typed, HttpResponseMessage partialResponse, bool continueDownload)
        {
            typed.SaveResponseInfo(partialResponse, continueDownload);

            if (existing != null && !object.ReferenceEquals(typed, existing))
            {
                typed.Size = existing.Size;
                typed.contentDispositionFileName = existing.contentDispositionFileName;
                typed.contentTypeExtension = existing.contentTypeExtension;
                lock (existing)
                {
                    lock (typed)
                    {
                        if (existing.manager != null && (typed.manager == null || !typed.manager.IsAlive))
                        {
                            existing.manager = typed.manager;
                        }
                    }
                }
            }

        }

#if DESKTOP

        private Task prefetchingTask;

        [RestrictedAccess]
        public Task PrefetchAsync()
        {
            Utils.AssertMainThread();
            if (prefetchingTask != null) return prefetchingTask;
            prefetchingTask = PrefetchAsyncInternal();
            return prefetchingTask;
            
        }
        private async Task PrefetchAsyncInternal()
        {
            Utils.AssertMainThread();

            try
            {

                // allow the task to be saved to the object;
                await TaskEx.Yield();

#if !STANDALONE

                if (Caching.AzureApi != null)
                {
                    var url = new LazyUri(Url);
                    var container = Caching.GetAzureContainer(url);
                    var files = await Caching.GetAzureCachedFiles(container);
                    var name = Caching.GetFileCachePath(url);
                    if (files.Contains(name)) return;
                    try
                    {
                        await Caching.AzureApi.UploadAzureFileAsync(container, name, this);
                    }
                    finally
                    {
                        files.Add(name);
                    }
                    return;
                }
#endif


                var path = Caching.GetFileCachePath(new LazyUri(Url), true);
                if (path == null) throw new InvalidOperationException("Caching.FileCachePath must be configured for file prefetching to work.");

                if (File.Exists(path)) return;
                var folder = Path.GetDirectoryName(path);


                using (var c = new FileStream(path + "$", FileMode.Create, FileAccess.Write, FileShare.Delete))
                {
                    try
                    {

                        using (var s = await OpenStreamAsync())
                        {
                            await s.CopyToAsync(c);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (this)
                        {
                            c.SetLength(0);
                            c.Dispose();
                            File.WriteAllText(Path.ChangeExtension(path, ".err"), GetExceptionSummary(ex), Encoding.UTF8);

                            File.Delete(path);
                            File.Move(path + "$", path);
                            return;
                        }
                    }
                }
                lock (this)
                {
                    File.Delete(path);
                    File.Move(path + "$", path);
                    File.Delete(Path.ChangeExtension(path, ".err"));
                }
            }
            finally
            {
                prefetchingTask = null;
            }
        }

        internal static string GetExceptionSummary(Exception ex)
        {
            return string.Join(string.Empty, ex.RecursiveEnumeration(x => x.InnerException).Select(x =>
            {
                var webex = ex as WebException;
                var status = webex != null ? (int)webex.GetResponseStatusCode() : 0;
                return (ex.GetType().FullName + (webex != null ? "=" + (status != 0 ? status : (int)webex.Status) : null)) + "&";
            })
#if NET35
            .ToArray()
#endif
            );
        }
#endif


        public event EventHandler Changed;

        protected void OnChanged()
        {
            var c = Changed;
            if (c != null) c(this, EventArgs.Empty);
        }


    }
}
