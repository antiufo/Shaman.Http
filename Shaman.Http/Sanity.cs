using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace Shaman.Runtime
{
    internal static class Sanity
    {
        internal static void AssertFastReadByte(Stream stream)
        {
        }
        internal static void AssertFastWriteByte(Stream stream)
        {
        }
        internal static Exception ShouldntHaveHappened()
        {
            throw new Exception("Internal error.");
        }
        [Conditional("NEVER")]
        internal static void Assert(bool condition)
        {
        }
        
        [Conditional("NEVER")]
        internal static void NotImplementedButTryToContinue()
        {
        }
        internal static NotImplementedException NotImplemented()
        {
            throw new NotImplementedException();
        }
        
        
    }
}
