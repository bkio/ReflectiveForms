// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Handles AI-powered field suggestions. Given a target field and current field values,
/// builds a prompt from the [AISuggestion] attribute and returns a suggestion.
/// </summary>
internal static class AiFieldSuggestionHandler
{
    internal static async Task<string?> SuggestAsync(
        string entityName,
        string targetFieldName,
        JObject currentFields,
        CancellationToken cancellationToken)
    {
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return null;

        var fieldsModelType = configBase.EntityConfiguration.EntityFieldsModelType;

        // Find the [AISuggestion] attribute on the target field
        var aiSuggestion = FindAiSuggestionAttribute(fieldsModelType, targetFieldName);
        if (aiSuggestion == null)
            return null;

        // Build context from source fields
        var context = BuildContext(entityName, targetFieldName, aiSuggestion.SourceFields, currentFields);

        var systemPrompt = RfConfiguration.AiServiceConfiguration!.SystemPromptPrefix + "\n" +
                           "You suggest values for entity fields based on the other fields already filled in. " +
                           "Generate a realistic, contextually appropriate value that matches the field's purpose. " +
                           "Match the tone and style of the existing content. " +
                           "Return ONLY the suggested value — no labels, explanations, or formatting.";

        var userPrompt = $"Field to suggest: {targetFieldName}\n" +
                         $"Instruction: {aiSuggestion.Prompt}\n\n" +
                         $"Context:\n{context}";

        var request = new LLMRequest
        {
            Messages =
            [
                new LLMMessage { Role = LLMRole.System, Content = systemPrompt },
                new LLMMessage { Role = LLMRole.User, Content = userPrompt }
            ],
            MaxTokens = RfConfiguration.AiServiceConfiguration!.MaxLightCompletionTokens,
            Temperature = RfConfiguration.AiServiceConfiguration.LightTemperature
        };

        var result = await AiConfiguration.LightLlmService.CompleteAsync(request, cancellationToken);
        if (!result.IsSuccessful)
        {
            RfConfiguration.LogError($"AiFieldSuggestionHandler: LLM call failed: {result.ErrorMessage}");
            return null;
        }

        return result.Data.Content?.Trim();
    }

    private static AISuggestion? FindAiSuggestionAttribute(Type fieldsModelType, string targetFieldName)
    {
        // Search all fields and properties for matching JsonProperty name with [AISuggestion]
        foreach (var member in fieldsModelType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var jsonProp = member.GetCustomAttribute<JsonPropertyAttribute>();
            if (jsonProp?.PropertyName != targetFieldName) continue;

            var suggestion = member.GetCustomAttribute<AISuggestion>();
            if (suggestion != null)
                return suggestion;
        }
        return null;
    }

    private static string BuildContext(string entityName, string targetFieldName, string[] sourceFields, JObject currentFields)
    {
        var parts = new List<string>();

        if (sourceFields.Length > 0)
        {
            // Use only specified source fields
            foreach (var fieldName in sourceFields)
            {
                var value = currentFields[fieldName];
                if (value != null && value.Type != JTokenType.Null)
                    parts.Add($"{fieldName}: {value}");
            }
        }
        else
        {
            // Use all text-bearing fields (excluding the target itself)
            var schemaResult = EntitySchemaGenerator.GenerateSchema(entityName);
            if (schemaResult.IsSuccessful)
            {
                foreach (var field in schemaResult.Data.Fields)
                {
                    if (field.Name == targetFieldName) continue;
                    if (field.Type is FieldSchemaType.Text or FieldSchemaType.TextArea or FieldSchemaType.WysiwygEditor)
                    {
                        var value = currentFields[field.Name];
                        if (value != null && value.Type != JTokenType.Null)
                            parts.Add($"{field.Label}: {value}");
                    }
                }
            }
        }

        return parts.Count > 0 ? string.Join("\n", parts) : "(no context available)";
    }
}
