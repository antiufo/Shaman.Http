using Shaman.Dom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    public class CssSelectorBreakpointException : Exception
    {
        internal CssSelectorBreakpointException() : base("A CSS selector breakpoint was hit.")
        {
        }
    }
}
