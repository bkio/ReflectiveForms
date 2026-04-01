// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReflectiveForms.Core.Utilities;

public static class JsonExtensions
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        TypeNameHandling = TypeNameHandling.All,
        Formatting = Formatting.None
    };

    private static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

    public static T? ToObjectWithPolymorphism<T>(this JToken jObject)
    {
        RemoveJsonTypeProperties(jObject);
        return jObject.ToObject<T>(Serializer);
    }

    public static object? ToObjectWithPolymorphism(this JObject jObject, Type type)
    {
        RemoveJsonTypeProperties(jObject);
        return jObject.ToObject(type, Serializer);
    }

    public static T? DeserializeObjectWithPolymorphism<T>(this string serialized)
    {
        return JsonConvert.DeserializeObject<T>(serialized, Settings);
    }

    public static object? DeserializeObjectWithPolymorphism(this string serialized, Type type)
    {
        return JsonConvert.DeserializeObject(serialized, type, Settings);
    }

    public static JObject FromObjectWithPolymorphism(this object value)
    {
        var jObj = JObject.FromObject(value, Serializer);
        RemoveJsonTypeProperties(jObj);
        return jObj;
    }

    public static string SerializeObjectWithPolymorphism(this object? value)
    {
        if (value == null) return "{}";
        var jObject = value.FromObjectWithPolymorphism();
        return jObject.ToString(Formatting.None);
    }

    internal static void RemoveJsonTypeProperties(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
            {
                var obj = (JObject)token;

                // Remove $type property if exists
                obj.Property("$type")?.Remove();

                // Unwrap TypeNameHandling.All array wrappers:
                // { "$values": [...] } → replace with the inner JArray
                foreach (var child in obj.Properties().ToList())
                {
                    if (child.Value is JObject wrapper
                        && wrapper.Property("$values") is { Value: JArray innerArray })
                    {
                        wrapper.Property("$type")?.Remove();
                        if (wrapper.Properties().Count() == 1) // only "$values" left
                        {
                            RemoveJsonTypeProperties(innerArray);
                            child.Value = innerArray;
                            continue;
                        }
                    }

                    RemoveJsonTypeProperties(child.Value);
                }

                break;
            }
            case JTokenType.Array:
            {
                foreach (var item in token.Children())
                {
                    RemoveJsonTypeProperties(item);
                }

                break;
            }
        }
    }
}
