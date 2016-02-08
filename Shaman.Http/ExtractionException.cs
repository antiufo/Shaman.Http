using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shaman;
#if !STANDALONE
using Shaman.Annotations;
#endif
using Shaman.Runtime;
using Shaman.Dom;

namespace Shaman.Runtime
{
    public class ExtractionException : UnparsableDataException
    {
#if !STANDALONE
        public Entity Entity { get; internal set; }
        public ExtractionAttribute Extraction { get; internal set; }
        public ListExtractionAttribute ListExtraction { get; internal set; }
        public Field Field { get { return Extraction != null ? Extraction.OwnerField : ListExtraction != null ? ListExtraction.OwnerField : null; } }
#endif
        public HtmlNode Node { get; internal set; }

        internal bool IsMissingKeyError;

#if STANDALONE
        public ExtractionException(HtmlNode node = null, Exception innerException = null, string sourceData = null, string beginString = null, string endString = null, string nodeQuery = null, string attribute = null, string regex = null, string userQuery = null, Uri url = null, string message = null)
            : base(innerException: innerException, sourceData: sourceData, beginString: beginString, endString: endString, nodeQuery: nodeQuery, attribute: attribute, regex: regex, userQuery: userQuery, url: url, message: message)
        {
            this.Node = node;
        }
#else
        public ExtractionException(HtmlNode node = null, Entity obj = null, ExtractionAttribute extraction = null, Exception innerException = null, string sourceData = null, string beginString = null, string endString = null, string nodeQuery = null, string attribute = null, string regex = null, string userQuery = null, Uri url = null, string message = null, ListExtractionAttribute listExtraction = null)
            : base(innerException: innerException, sourceData: sourceData, beginString: beginString, endString: endString, nodeQuery: nodeQuery, attribute: attribute, regex: regex, userQuery: userQuery, url: url, message: message)
        {
            this.Node = node;
            this.Entity = obj;
            this.Extraction = extraction;
            this.ListExtraction = listExtraction;
        }
#endif



    }
}
