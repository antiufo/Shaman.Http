using System;
using System.Collections.Generic;
using System.Linq;
#if !NET35
using System.Runtime.ExceptionServices;
#endif
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    static class ExceptionExtensions
    {

        public static Exception Rethrow(this Exception ex)
        {
#if NET35
            throw ex;
#else
            ExceptionDispatchInfo.Capture(ex).Throw();
            return ex;
#endif
        }
    }
}
