// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Models;

namespace ReflectiveForms.Core.Utilities;

internal static class EntityFieldsModelValidation
{
    internal static bool Validate(Type type, out string? error)
    {
        return
            ValidateType(type, out error)
            && ValidateJsonTypeFieldNameNotExists(type, out error);
    }

    private static bool ValidateType(Type type, out string? error)
    {
        // Avoid infinite recursion on circular references
        if (!VisitedTypesForFieldValidation.Add(type))
        {
            error = null;
            return true;
        }

        // Must derive from BaseModel
        if (!typeof(BaseModel).IsAssignableFrom(type))
        {
            error = $"Type {type.FullName} does not inherit from BaseModel.";
            return false;
        }

        // Walk fields and properties
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var field in fields)
        {
            if (!Attribute.IsDefined(field, typeof(Field), true)) continue;

            var fieldType = field.FieldType;

            if (IsCollection(fieldType, out var isList, out var elementType))
            {
                if (!isList)
                {
                    error = $"Type {type.FullName}->{field.Name} must be a -List- type.";
                    return false;
                }

                // Validate collection element type
                if (!ValidateType(elementType.NotNull(), out error))
                    return false;
            }
            else if (fieldType.IsClass && fieldType != typeof(string))
            {
                // Validate a direct field/property type
                if (!ValidateType(fieldType, out error))
                    return false;
            }
        }

        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var prop in properties)
        {
            if (!Attribute.IsDefined(prop, typeof(Field), true)) continue;

            var propType = prop.PropertyType;

            if (IsCollection(propType, out var isList, out var elementType))
            {
                if (!isList)
                {
                    error = $"Type {type.FullName}->{prop.Name} must be a -List- type.";
                    return false;
                }

                if (!ValidateType(elementType.NotNull(), out error))
                    return false;
            }
            else if (propType.IsClass && propType != typeof(string))
            {
                if (!ValidateType(propType, out error))
                    return false;
            }
        }

        error = null;
        return true;
    }
    private static readonly HashSet<Type> VisitedTypesForFieldValidation = [];

    private static bool IsCollection(Type memberType, out bool isList, out Type? elementType)
    {
        isList = false;
        elementType = null;

        // Arrays
        if (memberType.IsArray)
        {
            elementType = memberType.GetElementType();
            return true;
        }

        // Generic types
        if (!memberType.IsGenericType) return false;

        var genericDef = memberType.GetGenericTypeDefinition();

        // Check for List<T>
        if (genericDef == typeof(List<>))
        {
            elementType = memberType.GetGenericArguments()[0];
            isList = true;
            return true;
        }

        // Check for ICollection<T> or IEnumerable<T>
        if (genericDef != typeof(ICollection<>) && genericDef != typeof(IEnumerable<>)) return false;

        elementType = memberType.GetGenericArguments()[0];
        return true;
    }

    private static bool ValidateJsonTypeFieldNameNotExists(Type type, out string? error)
    {
        // Avoid infinite recursion on circular references
        if (!VisitedTypesForJsonTypeFieldNameCheck.Add(type))
        {
            error = null;
            return true;
        }

        // Check fields
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var field in fields)
        {
            var jPropAttr = field.GetCustomAttribute<JsonPropertyAttribute>(true);
            if (jPropAttr?.PropertyName == "$type")
            {
                error = $"Type {type.FullName} has a field '{field.Name}' with JSON property '$type'.";
                return false;
            }

            // Recurse into a field type
            if (field.FieldType.IsPrimitive || field.FieldType == typeof(string)) continue;

            if (!ValidateJsonTypeFieldNameNotExists(field.FieldType, out error))
                return false;
        }

        // Check properties
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var prop in properties)
        {
            var jPropAttr = prop.GetCustomAttribute<JsonPropertyAttribute>(true);
            if (jPropAttr?.PropertyName == "$type")
            {
                error = $"Type {type.FullName} has a property '{prop.Name}' with JSON property '$type'.";
                return false;
            }

            // Recurse into a property type
            if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string)) continue;

            if (!ValidateJsonTypeFieldNameNotExists(prop.PropertyType, out error))
                return false;
        }

        error = null;
        return true;
    }
    private static readonly HashSet<Type> VisitedTypesForJsonTypeFieldNameCheck = [];
}
