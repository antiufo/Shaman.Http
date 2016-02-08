using System;
using Newtonsoft.Json;

namespace Shaman.Runtime
{
    internal class LazyUriConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(LazyUri);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType == JsonToken.String)
            {
                var urlString = (string)reader.Value;
                return new LazyUri(urlString);
            }
            throw new FormatException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null) writer.WriteNull();
            else writer.WriteValue(((LazyUri)value).AbsoluteUri);
        }
    }
}