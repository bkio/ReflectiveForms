// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Attributes;

/// <summary>
/// Marks a field as eligible for AI-powered suggestions.
/// The frontend renders a "Suggest" button next to the field.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class AISuggestion : Attribute
{
    /// <summary>
    /// The prompt sent to the LLM to generate a suggestion for this field.
    /// Example: "Summarize in 2 sentences", "Suggest 3-5 relevant tags".
    /// </summary>
    public string Prompt { get; }

    /// <summary>
    /// Names of sibling fields whose values are sent as context to the LLM.
    /// Uses the JSON property name (e.g., "content", "title").
    /// If empty, sends all text-bearing fields.
    /// </summary>
    public string[] SourceFields { get; }

    public AISuggestion(string prompt, params string[] sourceFields)
    {
        Prompt = prompt;
        SourceFields = sourceFields;
    }
}
