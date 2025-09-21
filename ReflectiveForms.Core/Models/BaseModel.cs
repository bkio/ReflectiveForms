// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Models;

public abstract class BaseModel
{
    [JsonIgnore]
    public const string UniqueFieldIdPropertyName = "_unique_field_id";

    [JsonProperty(UniqueFieldIdPropertyName)]
    public string UniqueFieldId = "";

    public bool ShouldSerializeUniqueFieldId()
    {
        return MustSerializeUniqueFieldId || !string.IsNullOrEmpty(UniqueFieldId);
    }
    public bool MustSerializeUniqueFieldId = false;
    public static bool ShouldSerializeMustSerializeUniqueFieldId() { return false; }

    private readonly HashSet<string> _overriderOfShouldSerialize = [];
    protected bool CheckOverrideShouldSerialize()
    {
        var st = new StackTrace();
        var sf = st.GetFrame(1);
        var methodName = sf?.GetMethod()?.Name;
        if (methodName == null || !methodName.StartsWith("ShouldSerialize")) return false;
        var variableName = methodName["ShouldSerialize".Length..];
        return _overriderOfShouldSerialize.Contains(variableName);
    }

    public void OverrideShouldSerializeFor(string variableName)
    {
        _overriderOfShouldSerialize.Add(variableName);
    }

    [OnError]
    internal static void OnError(StreamingContext context, ErrorContext errorContext)
    {
        errorContext.Handled = true;
    }
}

public static class UniqueFieldIdRemovalJsonSerializer
{
    public static string SerializeObject(object obj)
    {
        var jObj = obj.FromObjectWithPolymorphism();
        RemoveUniqueFieldIds(jObj);
        return jObj.ToString(Formatting.None);
    }

    private static void RemoveUniqueFieldIds(JObject jObj)
    {
        var properties = jObj.Properties().ToList();
        var propertiesCount = properties.Count;
        for (var i = propertiesCount - 1; i >= 0; i--)
        {
            var p = properties[i];

            switch (p.Value.Type)
            {
                case JTokenType.Object:
                    RemoveUniqueFieldIds((JObject)p.Value);
                    break;
                case JTokenType.Array:
                {
                    var arr = (JArray)p.Value;
                    foreach (var item in arr)
                    {
                        if (item.Type == JTokenType.Object)
                        {
                            RemoveUniqueFieldIds((JObject)item);
                        }
                    }
                    break;
                }
                default:
                {
                    if (p.Name == BaseModel.UniqueFieldIdPropertyName)
                    {
                        p.Remove();
                    }
                    break;
                }
            }
        }
    }
}
