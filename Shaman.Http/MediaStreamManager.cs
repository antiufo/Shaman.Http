using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

using Shaman.Types;
#if NATIVE_HTTP
using System.Net.Http;
#else
using System.Net.Reimpl.Http;
#endif
#if NET35
using HttpResponseMessage = System.Net.HttpWebResponse;
#else
using TaskEx = System.Threading.Tasks.Task;
#endif

namespace Shaman.Runtime
{
    [RestrictedAccess]
    public class MediaStreamManager
    {

        public static MediaStreamManager Create(Func<long, Task<HttpResponseMessage>> getResponse, bool willStartRightNow)
        {
            return new MediaStreamManager(getResponse, willStartRightNow);
        }
        
        internal MediaStreamManager(Func<long, Task<HttpResponseMessage>> getResponse, bool willStartRightNow)
        {
            this.getResponse = getResponse;
            this.data = new List<byte[]>(200);
#if DESKTOP
            var reader = new Thread(() => ReadSource());
            reader.Name = "MediaStreamManager Reader";
            reader.IsBackground = true;
            reader.Start();
#else
            Task.Run(() => ReadSource());
#endif
            if (!willStartRightNow)
            {
                disposalCancellation = new CancellationTokenSource();
                DelayedDisposalAsync(disposalCancellation.Token, true, true);
            }
        }

        internal const int SlotSize = 10 * 32 * 1024;
        internal const int MaxSlots = 15000000 / SlotSize;
        private const int ReadGranularity = 4096;
        private volatile int maxRequestedByte;


        private static void EnsureCorrectNumberOfBytes(long actual, long expected)
        {
            if (expected == -1) return;
            if (actual < expected)
                throw new InvalidDataException("Less bytes were received than expected.");
            else if (actual > expected)
                throw new InvalidDataException("More bytes were received than expected.");
        }

        private int firstAvailableSlot;

        private async void ReadSource()
        {
            Exception possibleError = null;
            bool tryAgain = true;
            while (tryAgain)
            {
                tryAgain = false;
                currentAttemptStopwatch = null;
                currentAttemptLastReceivedData = TimeSpan.Zero;
                currentAttemptReceivedBytes = 0;
                Stream sourceStream;
                try
                {
                    var response = await getResponse(this.availableBytes);
                    if (this.availableBytes == 0)
                    {
#if WEBCLIENT
                        size = response.ContentLength != -1 ? response.ContentLength : (long?)null;
#else
                        var ce = response.Content.Headers.ContentEncoding;
                        if (ce == null || !ce.Any())
                            size = response.Content.Headers.ContentLength;
#endif
                    }
                    if (availableBytes != 0)
                    {
#if WEBCLIENT
                        var cr = response.Headers["Content-Range"];
                        // HACK: trust the server to return the expected range
                        if (string.IsNullOrEmpty(cr))
#else
                        if (response.Content == null || response.Content.Headers.ContentRange == null || response.Content.Headers.ContentRange.From.GetValueOrDefault(-1) != availableBytes)
#endif
                        {
                            response.Dispose();
                            throw new NotSupportedException("The web server did not return the expected range of data.");
                        }
                    }
                    if (data == null)
                    {
                        response.Dispose();
                        return;
                    }
#if WEBCLIENT
                    sourceStream = await TaskEx.Run(() => response.GetResponseStream());
#else
                    sourceStream = await response.Content.ReadAsStreamAsync();
#endif
                }
                catch (Exception ex)
                {
                    if (possibleError == null) possibleError = ex;
                    break;
                }


                this.source = sourceStream;





                var timedOut = false;
                using (sourceStream)
                using (var w = new Watchdog(Configuration_AudioReadTimeout, () => { timedOut = true; var s = sourceStream; if (s != null) s.Dispose(); }))
                {

                    while (true)
                    {
                        if (data == null) return;
                        byte[] slot;
                        if (currentSlotFreeSpace == 0)
                        {
                            slot = new byte[SlotSize];
                            MaybeCleanupOldSlots();
                            currentSlotFreeSpace = SlotSize;
                            data.Add(slot);
                        }
                        else
                        {
                            slot = data[data.Count - 1];
                        }

                        int waitAndRetry = 0;

                        try
                        {
                            var readBytes = source.Read(slot,
                                  SlotSize - currentSlotFreeSpace,
                                  Math.Min(currentSlotFreeSpace, ReadGranularity));

                            w.Pulse();



                            if (currentAttemptStopwatch == null)
                            {
                                currentAttemptStopwatch = Stopwatch.StartNew();
                            }
                            currentAttemptLastReceivedData = currentAttemptStopwatch.Elapsed;

                            if (readBytes == 0)
                            {
                                completed = true;
                                CloseSource();
                                if (size.HasValue) EnsureCorrectNumberOfBytes(availableBytes, size.Value);
                                size = availableBytes;
                                OnNewDataAvailable();
                                currentAttemptStopwatch.Stop();
                                return;
                            }
                            else
                            {
                                possibleError = null;
                                currentAttemptReceivedBytes += readBytes;
                                var modified = availableBytes + readBytes;

                                Interlocked.Exchange(ref availableBytes, modified);
                                currentSlotFreeSpace -= readBytes;

                                OnNewDataAvailable();
                                if (availableBytes >= Configuration_PrefetchLength && (consumers == 0 || availableBytes > maxRequestedByte + Configuration_NonRequestedDataLimit))
                                {
                                    await TaskEx.Delay(1000 * readBytes / (consumers == 0 ? Configuration_AfterPrefetchIdleSpeed : Configuration_IdleMaxSpeed));
                                }

                            }

                        }
                        catch (Exception ex)
                        {
                            if (data == null) return;
                            if (timedOut) ex = new TimeoutException();
                            if (possibleError == null) possibleError = ex;
                            if (currentAttemptStopwatch != null) currentAttemptStopwatch.Stop();
                            if (currentAttemptReceivedBytes >= 1 * 1024 * 1024 && !(ex is InvalidDataException)) waitAndRetry = 2;
                            else waitAndRetry = 1;
                        }
                        if (waitAndRetry != 0)
                        {
                            if (waitAndRetry == 2)
                            {
                                await TaskEx.Delay(ZeroSpeedAfterSilenceMilliseconds);
                                OnNewDataAvailable();
                                await TaskEx.Delay(10000 - ZeroSpeedAfterSilenceMilliseconds);
                                tryAgain = true;
                            }
                            break;
                        }

                    }
                }



            }
            if (possibleError != null)
            {
                error = possibleError;
                OnNewDataAvailable();
                CloseSource();
            }
        }

        private void MaybeCleanupOldSlots()
        {
            while (data.Count - firstAvailableSlot >= MaxSlots)
            {
                var resultingFirstAvailableByte = (firstAvailableSlot + 1) * SlotSize;
                bool ok = true;
                // for loop: avoid collection changed error (we just add or set to null)
                for (int i = 0; i < mediaStreams.Count; i++)
                {
                    var user = mediaStreams[i];
                    if (user != null)
                    {
                        if (user.Position < resultingFirstAvailableByte) { ok = false; break; }
                    }
                }
                if (!ok) break;
                lock (syncObj)
                {
                    firstAvailableByte = resultingFirstAvailableByte;
                    data[firstAvailableSlot] = null;
                    firstAvailableSlot++;
                }
            }
        }


        public int TryReadFromCache(
            int position,
            byte[] buffer,
            int bufferOffset,
            int count,
            bool readFully
            )
        {

            maxRequestedByte = Math.Max(maxRequestedByte, position + count);


            if (error != null) throw error;

            if (size.HasValue && position == size.Value)
            {
                return 0;
            }

            var avail = Interlocked.Read(ref availableBytes);

            if (position >= avail) return -1;

            var slot = (int)(position / SlotSize);
            var offset = (int)(position % SlotSize);

            var slotFirstMissing = (int)(avail / SlotSize);
            var offsetFirstMissing = (int)(avail % SlotSize);

            var actualBytes = Math.Min(count, SlotSize - offset);
            if (slot == slotFirstMissing) actualBytes = Math.Min(actualBytes, offsetFirstMissing - offset);

            if (readFully && actualBytes != count && !completed) return -1;

            var slotData = data[slot];
            if (slotData == null) throw new Exception("The requested data interval has been discarded and is no longer available.");
            Buffer.BlockCopy(slotData, offset, buffer, bufferOffset, actualBytes);

            Sanity.Assert(actualBytes >= 0);
            return actualBytes;

        }


        private void OnNewDataAvailable()
        {
            if (NewDataAvailable != null)
                NewDataAvailable(this, EventArgs.Empty);
        }


        private void CloseSource()
        {
            var s = source;
            if (s != null)
            {
                source = null;
                TaskEx.Run(() => s.Dispose());
            }

        }

        private void DisposeAll()
        {
            data = null;
            CloseSource();
            if (disposalCancellation != null)
                disposalCancellation.Dispose();
        }

        public bool IsAlive
        {
            get
            {
                return data != null;
            }
        }

        private long? size;
        private volatile Stream source;
        private volatile List<byte[]> data;
        private long availableBytes;
        private volatile int currentSlotFreeSpace;
        private volatile Exception error;
        public event EventHandler NewDataAvailable;
        private volatile bool completed;
        private int consumers;
        private volatile Func<long, Task<HttpResponseMessage>> getResponse;
        private volatile CancellationTokenSource disposalCancellation;

        public long AvailableBytes
        {
            get
            {
                return availableBytes;
            }
        }

        [Configuration]
        private readonly static int Configuration_PrefetchLength = 100000;
        [Configuration]
        private readonly static int Configuration_IdleMaxSpeed = 50 * 1024;
        [Configuration]
        private readonly static int Configuration_AfterPrefetchIdleSpeed = 10 * 1024;
        [Configuration]
        private readonly static int Configuration_DisposalSurvivalTimePrefetching = 10000;
        [Configuration]
        private readonly static int Configuration_DisposalSurvivalTimeNotCompleted = 5000;
        [Configuration]
        private readonly static int Configuration_DisposalSurvivalTimeCompleted = 90000;
        [Configuration]
        private readonly static int Configuration_NonRequestedDataLimit = 4096 * 3;
        //[Configuration]
        //private readonly static int Configuration_SourcePrefetchDelay = 2000;
        [Configuration]
        private readonly static int Configuration_AudioReadTimeout = 13000;
        //[Configuration]
        //private readonly static int Configuration_FileDownloadReadTimeout = 30000;

        private long firstAvailableByte;

        public MediaStream TryCreateStream(WebFile file, ulong position, bool linger)
        {
            lock (syncObj)
            {
                if ((ulong)firstAvailableByte > position) return null;
                if (disposalCancellation != null)
                {
                    disposalCancellation.Cancel();
                    disposalCancellation.Dispose();
                    disposalCancellation = null;
                }

                consumers++;
                var stream = new MediaStream(this, file, mediaStreams.Count, linger);
                mediaStreams.Add(stream);
                stream.Seek(position);

                return stream;
            }
        }


        private List<MediaStream> mediaStreams = new List<MediaStream>();
        private object syncObj = new object();

        internal void NotifyConsumerRemoved(int id, bool linger)
        {
            lock (syncObj)
            {
                var newCount = consumers--;
                Sanity.Assert(newCount >= 0);

                mediaStreams[id] = null;

                if (newCount == 0)
                {
                    disposalCancellation = new CancellationTokenSource();
        
                    DelayedDisposalAsync(disposalCancellation.Token, false, linger);
                    
                }
            }
        }

        private async void DelayedDisposalAsync(CancellationToken ct, bool initialPrefetching, bool linger)
        {
            if (linger)
            {
                await TaskEx.Delay(initialPrefetching ?
                    Configuration_DisposalSurvivalTimePrefetching :
                    Configuration_DisposalSurvivalTimeNotCompleted);

                if (ct.IsCancellationRequested) return;

                if (completed)
                {
                    await TaskEx.Delay(Configuration_DisposalSurvivalTimeCompleted - Configuration_DisposalSurvivalTimeNotCompleted);
                    if (ct.IsCancellationRequested) return;
                }
            }
            else
            {
                await TaskEx.Yield();
                if (ct.IsCancellationRequested) return;
            }
            DisposeAll();
        }

        public ulong? Size
        {
            get
            {
                return size != null ? (ulong?)size : null;
            }
        }

        public long? Speed
        {
            get
            {
                return CalculateSpeedNow((int)currentAttemptReceivedBytes, currentAttemptStopwatch, currentAttemptLastReceivedData);
            }
        }

        private long currentAttemptReceivedBytes;
        private Stopwatch currentAttemptStopwatch;
        private TimeSpan currentAttemptLastReceivedData;

        private static long? CalculateSpeedNow(long downloadedBytes, Stopwatch sw, TimeSpan lastReceivedData)
        {
            if (sw == null) return null;

            var elapsed = sw.Elapsed;
            if (elapsed.TotalMilliseconds < 1000) return null;
            if (lastReceivedData != TimeSpan.Zero)
            {
                var diff = elapsed - lastReceivedData;
                if (diff.Ticks < 0 || diff.TotalMilliseconds > ZeroSpeedAfterSilenceMilliseconds) return 0;
            }
            return (long)(downloadedBytes / elapsed.TotalSeconds);
        }

        private const int ZeroSpeedAfterSilenceMilliseconds = 1000;


        public Exception Error { get { return error; } }
    }
}
