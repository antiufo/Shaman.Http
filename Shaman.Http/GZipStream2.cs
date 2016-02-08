using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    public class GZipStream2 : GZipStream
    {
#if !NET35
        public GZipStream2(Stream stream, CompressionLevel compressionLevel) : base(stream, compressionLevel) { }
#endif
        public GZipStream2(Stream stream, CompressionMode compressionMode) : base(stream, compressionMode) { }
#if !NET35
        public GZipStream2(Stream stream, CompressionLevel compressionLevel, bool leaveOpen) : base(stream, compressionLevel, leaveOpen) { }
#endif
        public GZipStream2(Stream stream, CompressionMode compressionMode, bool leaveOpen) : base(stream, compressionMode, leaveOpen) { }

        private byte[] singleByteArray = new byte[1];

        public override int ReadByte()
        {
            if (Read(singleByteArray, 0, 1) == 0) return -1;
            return singleByteArray[0];
        }

        public override void WriteByte(byte value)
        {
            singleByteArray[0] = value;
            Write(singleByteArray, 0, 1);
        }
    }
}
