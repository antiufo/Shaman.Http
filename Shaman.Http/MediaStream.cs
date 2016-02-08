using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Shaman.Types;
#if NATIVE_HTTP
using System.Net;
#else
using System.Net.Reimpl;
#endif



namespace Shaman.Runtime
{
    public class MediaStream : Stream
    {
        private MediaStreamManager manager;


        private volatile int position;


        internal MediaStream(MediaStreamManager manager, WebFile file, int id, bool linger)
        {
            this.id = id;
            this.file = file;
            this.manager = manager;
            this.currentReadOperationWaitHandle = new AutoResetEvent(false);
            manager.NewDataAvailable += manager_NewDataAvailable;
            this.linger = linger;
        }

        internal MediaStream(Exception ex, WebFile file)
        {
            this.file = file;
            this.prebuiltException = ex;
            this.linger = true;
        }

        private WebFile file;
        public WebFile File
        {
            get
            {
                return file;
            }
        }

        public void Seek(ulong position)
        {
            if (position > 10000000000000000000) throw new NotSupportedException();
            this.position = (int)position;
        }


        private int disposed;
        private int id;
        protected override void Dispose(bool disposing)
        {

            if (disposing && manager != null && prebuiltException == null)
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    manager.NotifyConsumerRemoved(id, linger);
                    manager.NewDataAvailable -= manager_NewDataAvailable;
                    readNotification.Set();
#if NET35
                    readNotification.Close();
#else
                    readNotification.Dispose();
#endif
                }
                manager = null;
            }
            base.Dispose(disposing);
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Length
        {
            get
            {
                if (prebuiltException == null)
                {
                    var size = manager.Size;
                    if (size.HasValue) return (long)size.Value;
                }
                throw new NotSupportedException();
            }
        }

        public long? LengthIfKnown
        {
            get
            {
                if (prebuiltException == null)
                {
                    return (long)manager.Size;
                }
                return null;
            }
        }

        public override long Position
        {
            get { return (long)position; }
            set { position = (int)value; }
        }

        private volatile EventWaitHandle currentReadOperationWaitHandle;
        private volatile bool waitingForNotifications;
        private EventWaitHandle readNotification = new AutoResetEvent(false);

        private byte[] singleByteArray = new byte[1];

        public override int ReadByte()
        {
            var k = Read(singleByteArray, 0, 1);
            if (k == 0) return -1;
            return singleByteArray[0];
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (prebuiltException != null) throw prebuiltException;
            while (true)
            {
                var readBytes = manager.TryReadFromCache(position, buffer, offset, count, false);
                if (readBytes != -1)
                {
                    waitingForNotifications = false;
                    position += readBytes;
                    return readBytes;
                }
                waitingForNotifications = true;
                readNotification.WaitOne(5000);
            }

        }

        void manager_NewDataAvailable(object sender, EventArgs e)
        {
            if (waitingForNotifications)
                readNotification.Set();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin) position = (int)offset;
            else if (origin == SeekOrigin.Current) position += (int)offset;
            else if (origin == SeekOrigin.End) throw new NotSupportedException();
            else throw new ArgumentOutOfRangeException();

            return (long)position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public FileSize DataPerSecond
        {
            get
            {
                if (prebuiltException != null) return FileSize.Zero;
                return new FileSize(manager.Speed.GetValueOrDefault());
            }
        }

        public FileSize? Size
        {
            get
            {
                if (prebuiltException != null) return null;
                var s = manager.Size;
                if (s != null) return new FileSize((long)s.Value);
                return null;
            }
        }

        private Exception prebuiltException;
        private bool linger;

        internal static Exception ExceptionFromCachedResponse(string errlines)
        {
            var errs = errlines.Replace("\r", "").Replace('\n', '&').Replace("&", ", ");
            return new WebException("Error from cached response: " + errs);
        }


    }
}
