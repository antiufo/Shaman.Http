using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
#if NATIVE_HTTP
using System.Net.Http;
using System.Net.Http.Headers;
#else
using System.Net.Reimpl.Http;
using System.Net.Reimpl.Http.Headers;
#endif
using System.Text;
using System.Threading.Tasks;
using System.Linq;
#if !NET35
using TaskEx = System.Threading.Tasks.Task;
#endif

namespace Shaman.Runtime
{
    public class OptimizedFormUrlEncodedContent : HttpContent
    {
        private byte[] mem;
        private int length;
        public OptimizedFormUrlEncodedContent(IEnumerable<KeyValuePair<string, string>> nameValueCollection)
        {
#if WEBCLIENT
            base.ContentType = "application/x-www-form-urlencoded";
#else            
            base.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
#endif
            EncodeContent(nameValueCollection);
        }

        [AllowNumericLiterals]
        private void EncodeContent(IEnumerable<KeyValuePair<string, string>> nameValueCollection)
        {
            if (nameValueCollection == null)
            {
                throw new ArgumentNullException("nameValueCollection");
            }
            mem = new byte[nameValueCollection.Sum(x => (int)(x.Key.Length * 1.2 + 4 + x.Value.Length * 1.2))];
            foreach (KeyValuePair<string, string> current in nameValueCollection)
            {
                if (length != 0)
                {
                    AddChar(38);
                }

                WriteUriEncoded(current.Key);
                AddChar(61);
                WriteUriEncoded(current.Value);
            }
        }

        private void AddChar(byte v)
        {
            if (mem.Length == length)
            {
                Array.Resize(ref mem, mem.Length * 2);
            }
            mem[length++] = v;
        }

#if NET35
        internal
#endif
        protected override bool TryComputeLength(out long length)
        {
            length = this.length;
            return true;
        }

        private void WriteUriEncoded(string value)
        {
#if DESKTOP
            mem = DirectUriEscapeByte.EscapeString(value, 0, value.Length, mem, ref length, false, DirectUriEscapeByte.c_DummyChar, DirectUriEscapeByte.c_DummyChar, DirectUriEscapeByte.c_DummyChar);
#else
            var k = Uri.EscapeDataString(value);
            var len = Encoding.UTF8.GetByteCount(k);
            if (length + len <= mem.Length)
            {
                Array.Resize(ref mem, Math.Max(length + len + 8, (int)(mem.Length * 1.3)));
            }
            Encoding.UTF8.GetBytes(k, 0, k.Length, mem, length);
#endif
        }

#if NET35
        internal
#endif
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            return stream.WriteAsync(mem, 0, length);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return TaskEx.FromResult<Stream>(new MemoryStream(mem, 0, length));
        }
    }
}
