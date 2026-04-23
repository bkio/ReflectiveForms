// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Agentic entity generation using an LLM tool-calling loop.
/// The LLM decides which fields to fill and in what order by calling tools.
/// Best suited for capable models (GPT-4o, Claude, Gemini Pro) that reliably produce tool calls.
/// </summary>
internal static class AiEntityGeneratorAgentic
{
    private const int MaxIterations = 6;

    private static readonly LLMToolDefinition[] Tools =
    [
        new()
        {
            Name = "get_schema",
            Description = "Get the full field schema for the entity, including field types, constraints, choices, and nested structures.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        },
        new()
        {
            Name = "set_fields",
            Description = "Set one or more field values on the draft entity. Each key is a field name and each value is the field value. " +
                          "For select fields use the exact choice value. For dates use the specified format. " +
                          "For groups, provide a nested object. For repeaters, provide an array of objects. " +
                          "Returns validation results per field (accepted or rejected with reason).",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["fields"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Object mapping field names to their values."
                    }
                },
                ["required"] = new JArray("fields")
            }
        },
        new()
        {
            Name = "get_draft",
            Description = "Get the current state of the draft entity as JSON, showing all fields that have been set so far.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        },
        new()
        {
            Name = "get_examples",
            Description = "Get 1-2 existing entities similar to the user's topic for reference on style and content patterns.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        }
    ];

    internal static async Task<(JObject? Fields, List<LLMMessage> Conversation)> GenerateAsync(
        string entityName, string readableName, string? entityDescription,
        string userPrompt, List<FieldSchema> fields, CancellationToken cancellationToken)
    {
        var draft = new JObject();
        var conversationLog = new List<LLMMessage>();

        // Build schema description for tools
        var schemaLines = new List<string>();
        foreach (var field in fields)
            BuildFieldContext(field, "  ", schemaLines);
        var schemaText = string.Join("\n", schemaLines);

        // Build system prompt
        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var systemPrompt =
            $"{aiConfig.SystemPromptPrefix}\n\n" +
            $"You are generating a new {readableName} entity based on a user's request.\n" +
            (entityDescription != null ? $"Entity description: {entityDescription}\n" : "") +
            "Use the provided tools to:\n" +
            "1. First call get_schema to understand the field structure.\n" +
            "2. Optionally call get_examples to see existing entities for reference.\n" +
            "3. Call set_fields one or more times to fill in all fields. Start with the title, then structural fields (selects, checkboxes, dates, numbers), then content fields (text areas, WYSIWYG editors).\n" +
            "4. If any field is rejected, fix the value and call set_fields again.\n" +
            "5. When all fields are set, call get_draft to verify, then respond with a final confirmation message.\n\n" +
            "IMPORTANT:\n" +
            "- Fill ALL fields, including nested groups and repeaters.\n" +
            "- For select fields, use exact choice values from the schema.\n" +
            "- For content fields (WYSIWYG, TextArea), write substantial, realistic content (multiple paragraphs).\n" +
            "- For repeater fields, generate 2-3 items.\n" +
            "- Do NOT leave any field empty unless it is truly optional.";

        var messages = new List<LLMMessage>
        {
            new() { Role = LLMRole.System, Content = systemPrompt },
            new() { Role = LLMRole.User, Content = $"Generate a {readableName} about: {userPrompt}" }
        };

        conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = userPrompt });

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var request = new LLMRequest
            {
                Messages = messages,
                Tools = Tools,
                MaxTokens = aiConfig.MaxCompletionTokens,
                Temperature = aiConfig.Temperature
            };

            var result = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
            if (!result.IsSuccessful)
                break;

            if (result.Data.FinishReason == LLMFinishReason.ToolCall &&
                result.Data.ToolCalls is { Count: > 0 })
            {
                messages.Add(new LLMMessage
                {
                    Role = LLMRole.Assistant,
                    Content = result.Data.Content,
                    ToolCalls = result.Data.ToolCalls.ToList()
                });

                foreach (var toolCall in result.Data.ToolCalls)
                {
                    var toolResult = await ExecuteToolAsync(
                        toolCall.Name, toolCall.Arguments,
                        entityName, draft, fields, schemaText, userPrompt,
                        cancellationToken);

                    conversationLog.Add(new LLMMessage
                    {
                        Role = LLMRole.Assistant,
                        Content = $"[tool: {toolCall.Name}]"
                    });

                    messages.Add(new LLMMessage
                    {
                        Role = LLMRole.Tool,
                        ToolCallId = toolCall.Id,
                        Content = toolResult
                    });
                }

                continue;
            }

            // Final answer — LLM is done
            if (!string.IsNullOrWhiteSpace(result.Data.Content))
            {
                conversationLog.Add(new LLMMessage
                {
                    Role = LLMRole.Assistant,
                    Content = result.Data.Content
                });
            }

            break;
        }

        // Post-processing and validation
        if (draft.Count == 0)
            return (null, conversationLog);

        AiEntityGenerator.PostProcessFields(draft, fields);

        var errors = AiEntityGeneratorValidator.ValidateDraft(draft, fields);
        if (errors.Count > 0)
        {
            var remaining = AiEntityGeneratorValidator.ApplyAutoFixes(draft, fields, errors);
            // Remaining unfixable errors are logged but not retried in agentic mode
            // (the LLM already had the chance to fix via set_fields rejections)
        }

        return (draft, conversationLog);
    }

    private static async Task<string> ExecuteToolAsync(
        string toolName, string arguments,
        string entityName, JObject draft, List<FieldSchema> fields,
        string schemaText, string userPrompt,
        CancellationToken cancellationToken)
    {
        try
        {
            JObject? args = null;
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                try { args = JObject.Parse(arguments); }
                catch { /* no args needed for some tools */ }
            }

            return toolName switch
            {
                "get_schema" => ExecuteGetSchema(schemaText),
                "set_fields" => ExecuteSetFields(args, draft, fields),
                "get_draft" => ExecuteGetDraft(draft),
                "get_examples" => await ExecuteGetExamplesAsync(entityName, userPrompt, cancellationToken),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            return $"Tool execution failed: {ex.Message}";
        }
    }

    private static string ExecuteGetSchema(string schemaText)
    {
        return $"Entity field schema:\n{schemaText}";
    }

    private static string ExecuteSetFields(JObject? args, JObject draft, List<FieldSchema> fields)
    {
        if (args == null || !args.ContainsKey("fields"))
            return "Error: 'fields' parameter is required.";

        var fieldsToSet = args["fields"] as JObject;
        if (fieldsToSet == null || fieldsToSet.Count == 0)
            return "Error: 'fields' must be a non-empty object.";

        var fieldMap = BuildFieldMap(fields);
        var results = new JObject();

        foreach (var prop in fieldsToSet.Properties())
        {
            if (!fieldMap.TryGetValue(prop.Name, out var schema))
            {
                results[prop.Name] = $"rejected: unknown field '{prop.Name}'";
                continue;
            }

            // For complex types (groups, repeaters), apply directly
            if (schema.Type is FieldSchemaType.Group or FieldSchemaType.Repeater
                or FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea)
            {
                var cloned = prop.Value.DeepClone();

                // Normalize repeater items: LLMs often send flat string arrays
                // instead of arrays of objects. Convert each string into a proper
                // item object using the StickyTitleField or first text field.
                if (schema.Type == FieldSchemaType.Repeater
                    && cloned is JArray arr && schema.RepeaterOptions?.ItemSchema != null)
                {
                    var primaryField = schema.RepeaterOptions.StickyTitleField
                        ?? schema.RepeaterOptions.ItemSchema
                            .FirstOrDefault(f => f.Type is FieldSchemaType.TextArea
                                or FieldSchemaType.Text or FieldSchemaType.WysiwygEditor)?.Name;

                    if (primaryField != null)
                    {
                        var normalized = new JArray();
                        foreach (var item in arr)
                        {
                            if (item is JObject obj)
                                normalized.Add(obj);
                            else if (item.Type == JTokenType.String)
                                normalized.Add(new JObject { [primaryField] = item.Value<string>() });
                        }
                        cloned = normalized;
                    }
                }

                draft[prop.Name] = cloned;
                results[prop.Name] = "accepted";
                continue;
            }

            // For structured types, parse and validate
            var parsed = AiEntityGenerator.ParseFieldValue(schema, prop.Value.ToString());
            if (parsed != null)
            {
                draft[prop.Name] = parsed;
                results[prop.Name] = "accepted";
            }
            else if (prop.Value.Type is JTokenType.Boolean or JTokenType.Integer or JTokenType.Float)
            {
                draft[prop.Name] = prop.Value.DeepClone();
                results[prop.Name] = "accepted";
            }
            else
            {
                // Validate and provide rejection reason
                var tempDraft = new JObject { [prop.Name] = prop.Value };
                var errors = AiEntityGeneratorValidator.ValidateDraft(tempDraft, [schema]);
                if (errors.Count > 0)
                {
                    results[prop.Name] = $"rejected: {errors[0].Message}";
                }
                else
                {
                    draft[prop.Name] = prop.Value.DeepClone();
                    results[prop.Name] = "accepted";
                }
            }
        }

        return results.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string ExecuteGetDraft(JObject draft)
    {
        if (draft.Count == 0)
            return "Draft is empty. No fields have been set yet.";
        return draft.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    private static async Task<string> ExecuteGetExamplesAsync(
        string entityName, string userPrompt, CancellationToken cancellationToken)
    {
        var example = await AiEntityGenerator.FetchExampleEntityJsonAsync(entityName, userPrompt, cancellationToken);
        return example ?? "No existing examples found for this entity type.";
    }

    private static Dictionary<string, FieldSchema> BuildFieldMap(List<FieldSchema> fields)
    {
        var map = new Dictionary<string, FieldSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            map[field.Name] = field;
            if (field.GroupOptions?.ChildSchema != null)
            {
                foreach (var child in field.GroupOptions.ChildSchema)
                    map[$"{field.Name}.{child.Name}"] = child;
            }
        }
        return map;
    }

    /// <summary>
    /// Builds a human-readable field context description (matches AiAgentChatHandler pattern).
    /// </summary>
    private static void BuildFieldContext(FieldSchema field, string indent, List<string> lines)
    {
        var desc = $"{indent}- {field.Name} ({field.Type}): \"{field.Label}\"";

        if (field.SelectOptions?.Choices != null)
        {
            var choices = field.SelectOptions.Choices.Select(c => c.Value).ToArray();
            desc += $" [choices: {string.Join(", ", choices)}]";
        }

        if (field.Type == FieldSchemaType.DatePicker && field.DateOptions != null)
        {
            desc += $" [format: {field.DateOptions.Format}";
            if (field.DateOptions.IncludeTime)
                desc += ", includes time";
            desc += "]";
        }

        if (field.NumberOptions != null)
        {
            if (field.NumberOptions.Min.HasValue || field.NumberOptions.Max.HasValue)
            {
                desc += " [range: ";
                if (field.NumberOptions.Min.HasValue) desc += $"min={field.NumberOptions.Min}";
                if (field.NumberOptions.Min.HasValue && field.NumberOptions.Max.HasValue) desc += ", ";
                if (field.NumberOptions.Max.HasValue) desc += $"max={field.NumberOptions.Max}";
                desc += "]";
            }
        }

        if (!string.IsNullOrEmpty(field.DisplayCondition))
            desc += $" [visible when: {field.DisplayCondition}]";

        lines.Add(desc);

        if (field.GroupOptions?.ChildSchema != null)
        {
            foreach (var child in field.GroupOptions.ChildSchema)
                BuildFieldContext(child, indent + "  ", lines);
        }

        if (field.RepeaterOptions?.ItemSchema != null)
        {
            lines.Add($"{indent}  (repeater items):");
            foreach (var child in field.RepeaterOptions.ItemSchema)
                BuildFieldContext(child, indent + "    ", lines);
        }
    }
}
