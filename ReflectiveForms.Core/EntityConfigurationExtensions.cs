// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using ReflectiveForms.Core.Models;

namespace ReflectiveForms.Core;

public static class EntityConfigurationExtensions
{
    public static Type ToEntityModelType<T>(this EntityConfigurationBuilder<T> config) where T : EntityFieldsModel, new()
    {
        var hasParent = config.HasParentChildRelationship;
        var hasAuthor = config.HasAuthor;
        var hasTags = config.HasTags;
        var hasCategories = config.HasCategories;

        // Choose the correct class name based on booleans
        var typeName =
            (hasParent ? "With" : "Without") + "Parent" +
            (hasAuthor ? "With" : "Without") + "Author" +
            (hasTags ? "With" : "Without") + "Tags" +
            (hasCategories ? "With" : "Without") + "Categories`1"; // backtick 1 = one generic param

        // All classes are in ReflectiveForms.Core.Models namespace
        var fullName = $"ReflectiveForms.Core.Models.{typeName}, ReflectiveForms.Core";

        var openGenericType = Type.GetType(fullName, throwOnError: false);

        if (openGenericType == null)
        {
            throw new InvalidOperationException(
                $"Could not resolve entity model type for configuration: {fullName}");
        }

        return openGenericType.MakeGenericType(typeof(T));
    }
}
