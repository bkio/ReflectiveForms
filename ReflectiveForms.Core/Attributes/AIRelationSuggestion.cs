// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Attributes;

/// <summary>
/// Optional attribute on Relation fields to enable AI-powered relation suggestions.
/// When a Relation field with this attribute is focused, the frontend can request
/// semantically similar entities from the target entity type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AIRelationSuggestion : Attribute
{
    /// <summary>
    /// Number of suggestions to return. Default: 5.
    /// </summary>
    public int TopK { get; }

    public AIRelationSuggestion(int topK = 5)
    {
        TopK = topK;
    }
}
