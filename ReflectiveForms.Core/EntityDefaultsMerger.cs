// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;

namespace ReflectiveForms.Core;

/// <summary>
/// Merges C# model defaults into a raw DB JObject, injecting default values
/// for fields that are missing due to schema evolution (new properties added
/// to the entity model after the entity was created).
///
/// Object nesting (Group fields) is handled by the standard JObject.Merge.
/// The only gap is arrays of objects (Repeater fields), where each element
/// must be individually merged with a default template of the item type.
/// </summary>
public static class EntityDefaultsMerger
{
    /// <summary>
    /// Merge model defaults into a raw DB entity JObject.
    /// Existing values are never overwritten. Extra keys not in the model are preserved.
    /// </summary>
    /// <param name="dbEntity">The raw entity JObject from the database.</param>
    /// <param name="configuration">The entity's final configuration (provides DefaultJObject and repeater map).</param>
    /// <returns>A new JObject with defaults merged in.</returns>
    internal static JObject MergeDefaults(JObject dbEntity, EntityFinalConfigurationBase configuration)
    {
        var templateMap = configuration.RepeaterTemplateMap;

        JObject enhanced;
        if (templateMap.Count == 0)
        {
            // No repeaters — skip the recursive walk entirely
            enhanced = dbEntity;
        }
        else
        {
            // Enhance repeater elements — inject defaults into each array-of-objects element
            enhanced = EnhanceRepeaterElements(dbEntity, "", templateMap);
        }

        // Merge enhanced DB entity into the full-entity defaults.
        //    Defaults provide the base structure; enhanced DB values override.
        //    JObject.Merge handles all object nesting (Group fields, title, etc.) automatically.
        var result = (JObject)configuration.DefaultJObject.DeepClone();
        result.Merge(enhanced, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Merge
        });

        return result;
    }

    /// <summary>
    /// Recursively walk the JObject tree. Where a property is a JArray of JObjects
    /// whose path matches a known repeater template, merge each element with the
    /// template to inject missing defaults.
    /// </summary>
    private static JObject EnhanceRepeaterElements(
        JObject node,
        string currentPath,
        IReadOnlyDictionary<string, JObject> templateMap)
    {
        var result = (JObject)node.DeepClone();

        foreach (var prop in result.Properties().ToList())
        {
            var childPath = currentPath.Length == 0
                ? prop.Name
                : $"{currentPath}.{prop.Name}";

            if (prop.Value is JArray arr && arr.Count > 0 && arr[0] is JObject
                && templateMap.TryGetValue(childPath, out var template))
            {
                // This is a known Repeater array — merge each element with the template
                var merged = new JArray();
                foreach (var elem in arr)
                {
                    var mergedElem = (JObject)template.DeepClone();
                    if (elem is JObject elemObj)
                    {
                        mergedElem.Merge(elemObj, new JsonMergeSettings
                        {
                            MergeArrayHandling = MergeArrayHandling.Replace,
                            MergeNullValueHandling = MergeNullValueHandling.Merge
                        });
                    }
                    // Recurse into the merged element for nested repeaters
                    merged.Add(EnhanceRepeaterElements(mergedElem, childPath, templateMap));
                }
                prop.Value = merged;
            }
            else if (prop.Value is JObject nestedObj)
            {
                prop.Value = EnhanceRepeaterElements(nestedObj, childPath, templateMap);
            }
            // Primitive arrays and leaf values pass through unchanged
        }

        return result;
    }

    /// <summary>
    /// Build a template map for all Repeater fields in an entity type.
    /// Key: dot-separated JSON property path (e.g. "fields.variants").
    /// Value: default JObject for one repeater item.
    ///
    /// Walk is recursive to handle nested Repeaters (e.g. "fields.variants.specs").
    /// </summary>
    internal static Dictionary<string, JObject> BuildRepeaterTemplateMap(
        Type entityFieldsModelType,
        string currentPath = "fields")
    {
        var map = new Dictionary<string, JObject>();
        PopulateTemplateMap(entityFieldsModelType, currentPath, map);
        return map;
    }

    private static void PopulateTemplateMap(
        Type modelType,
        string parentPath,
        Dictionary<string, JObject> map)
    {
        foreach (var member in modelType.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                 .Where(m => m is FieldInfo or PropertyInfo))
        {
            var memberType = member is FieldInfo fi ? fi.FieldType : ((PropertyInfo)member).PropertyType;
            var jsonPropAttr = member.GetCustomAttribute<JsonPropertyAttribute>(true);
            var fieldName = jsonPropAttr?.PropertyName ?? member.Name;
            var path = $"{parentPath}.{fieldName}";

            var repeaterAttr = member.GetCustomAttribute<Repeater>(true);
            if (repeaterAttr != null)
            {
                var itemType = repeaterAttr.RepeaterFor;

                // Create default instance of the repeater item type
                var defaultInstance = Activator.CreateInstance(itemType, nonPublic: true);
                if (defaultInstance == null) continue;

                var template = JObject.FromObject(defaultInstance,
                    JsonSerializer.Create(new JsonSerializerSettings
                    {
                        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor
                    }));

                map[path] = template;

                // Recurse — the repeater item type may contain nested Repeaters or Groups
                PopulateTemplateMap(itemType, path, map);
            }
            else
            {
                // Recurse into nested model types (Group fields) to find repeaters at deeper levels.
                // Matches the structural pattern used by EntityModelDefaultsBuilder and ScanTypeForByteFields:
                //   List<T> → recurse into element type T
                //   Class (not string) → recurse into the type itself
                if (memberType.IsGenericType &&
                    memberType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    var elementType = memberType.GetGenericArguments()[0];
                    if (elementType.IsClass && elementType != typeof(string))
                        PopulateTemplateMap(elementType, path, map);
                }
                else if (memberType.IsClass && memberType != typeof(string))
                {
                    PopulateTemplateMap(memberType, path, map);
                }
            }
        }
    }
}
