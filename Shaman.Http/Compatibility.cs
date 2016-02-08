#if NET35
using System.Net;
using System;

namespace Shaman
{
    internal static class CompatibilityExtensions
    {

        public static Type GetTypeInfo(this Type type)
        {
            return type;
        }
        
        public static void Dispose(this HttpWebResponse response)
        {
            response.Close();
            
        }
    }
}

namespace System
{

    public interface IProgress<T>
    {
        void Report(T value);
    }
}

namespace System.Net.Http
{
    public static class HttpMethod
    {
        public static readonly string Get = "GET";
        public static readonly string Post = "POST";
        public static readonly string Put = "PUT";
        public static readonly string Delete = "DELETE";

    }
    namespace Headers {
        internal interface _Dummy{}
    }
}

#endif

#if STANDALONE
internal class AllowNumericLiterals : System.Attribute
{

}
#endif