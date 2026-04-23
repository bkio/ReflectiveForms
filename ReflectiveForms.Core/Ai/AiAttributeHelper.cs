// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Shared reflection helpers for discovering AI-related attributes on entity field models.
/// Used by both HTTP endpoints and the agent chat tool executor.
/// </summary>
internal static class AiAttributeHelper
{
    /// <summary>
    /// Finds all [AISanityCheck] attributes for a specific field by its JSON property name.
    /// </summary>
    internal static List<AISanityCheck> FindAiSanityChecks(Type fieldsModelType, string targetFieldName)
    {
        var checks = new List<AISanityCheck>();
        foreach (var member in fieldsModelType.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var jsonProp = member.GetCustomAttribute<JsonPropertyAttribute>();
            if (jsonProp?.PropertyName != targetFieldName) continue;

            var attrs = member.GetCustomAttributes<AISanityCheck>(true);
            checks.AddRange(attrs);
        }
        return checks;
    }

    /// <summary>
    /// Discovers all fields on a model type that have [AISanityCheck] attributes.
    /// Returns tuples of (jsonPropertyName, checks) for batch quality checking.
    /// </summary>
    internal static List<(string FieldName, IReadOnlyList<AISanityCheck> Checks)> FindAllFieldsWithSanityChecks(Type fieldsModelType)
    {
        var result = new List<(string FieldName, IReadOnlyList<AISanityCheck> Checks)>();
        foreach (var member in fieldsModelType.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m is FieldInfo or PropertyInfo))
        {
            var aiChecks = member.GetCustomAttributes<AISanityCheck>(true).ToList();
            if (aiChecks.Count == 0) continue;

            var jsonPropAttr = member.GetCustomAttribute<JsonPropertyAttribute>(true);
            var fieldName = jsonPropAttr?.PropertyName ?? member.Name;
            result.Add((fieldName, aiChecks));
        }
        return result;
    }
}
