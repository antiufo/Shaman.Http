using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shaman.Dom;

#if SMALL_LIB_AWDEE
namespace Shaman.Runtime
#else
namespace Xamasoft
#endif
{
    public class UnparsableDataException : Exception
    {

        internal UnparsableDataException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        internal UnparsableDataException()
        {
            // Empty
        }

        public UnparsableDataException(
            Exception innerException = null,
            string sourceData = null,
            string beginString = null,
            string endString = null,
            string nodeQuery = null,
            string attribute = null,
            string regex = null,
            string userQuery = null,
            LazyUri url = null,
            string message = null
            )
            : base(message, innerException)
        {
            this.SourceData = sourceData;
            this.BeginString = beginString;
            this.EndString = endString;
            this.NodeQuery = nodeQuery;
            this.Attribute = attribute;
            this.Regex = regex;
            this.UserQuery = userQuery;
            this.Url = url;
        }



        internal HtmlNode SourceDataNode
        {
            set
            {
                SourceData = value != null ? value.WriteTo() : null;
                Url = value.OwnerDocument.GetLazyPageUrl();
            }
        }


        public string SourceData { get; internal set; }
        public string BeginString { get; internal set; }
        public string EndString { get; internal set; }
        public string NodeQuery { get; internal set; }
        public string Attribute { get; internal set; }
        public string Regex { get; internal set; }
        public string UserQuery { get; internal set; }
        public LazyUri Url { get; internal set; }




    }
}
