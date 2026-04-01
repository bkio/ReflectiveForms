// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Attributes;

/// <summary>
/// Marks a model class with the field name to display in repeater/group sticky headers.
/// The value must match the JSON property name of the field to use as the title.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class StickyTitle(string fieldName) : Attribute
{
    public readonly string FieldName = fieldName;
}
