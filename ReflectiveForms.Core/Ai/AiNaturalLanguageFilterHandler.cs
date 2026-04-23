// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

internal record NlFilterResult(
    List<NlInterpretedFilter> InterpretedFilters,
    string Combination,
    string? NaturalLanguageInterpretation,
    List<JObject> Results,
    bool UsedVectorFallback = false);

internal record NlInterpretedFilter(string Field, string Operator, string Value);

/// <summary>
/// Translates natural language queries into database filter conditions via LLM tool calling,
/// then executes the filtered query server-side.
/// </summary>
internal static class AiNaturalLanguageFilterHandler
{
    internal static async Task<NlFilterResult?> FilterAsync(
        string entityName,
        string query,
        CancellationToken cancellationToken)
    {
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return null;

        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityName);
        if (!schemaResult.IsSuccessful || schemaResult.Data == null) return null;
        var schema = schemaResult.Data;

        // Build schema context for LLM
        var schemaContext = BuildSchemaContext(schema);

        // Build tool definitions for filter construction
        var tools = BuildFilterTools();

        var systemPrompt = RfConfiguration.AiServiceConfiguration!.SystemPromptPrefix + "\n\n" +
                           "You are translating natural language queries into structured filter conditions. " +
                           "Use the provided tools to build filter conditions for the entity type. " +
                           "Call the filter tools to construct conditions, then call combine_and or combine_or to compose them. " +
                           "Each filter tool returns a filter_id you can reference in combine calls. " +
                           "IMPORTANT: field_name must use the JSON property name from the schema, with 'fields.' prefix for entity fields. " +
                           "For example, 'fields.status' for the status field.\n\n" +
                           "TITLE SEARCH: When the user's query is about what an entity is about, mentions a name/topic, or is vague " +
                           "(e.g. 'the one related to X', 'about Y', 'find Z'), use filter_title_search to search by title. " +
                           "Prefer filter_title_search over guessing which structured field to filter on. " +
                           "You can combine filter_title_search with structured field filters.\n\n" +
                           "Available fields:\n" + schemaContext;

        var request = new LLMRequest
        {
            Messages =
            [
                new LLMMessage { Role = LLMRole.System, Content = systemPrompt },
                new LLMMessage { Role = LLMRole.User, Content = query }
            ],
            Tools = tools,
            MaxTokens = RfConfiguration.AiServiceConfiguration!.MaxCompletionTokens,
            Temperature = RfConfiguration.AiServiceConfiguration!.Temperature
        };

        var result = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
        if (!result.IsSuccessful)
        {
            RfConfiguration.LogError($"AiNaturalLanguageFilterHandler: LLM call failed: {result.ErrorMessage}");
            return null;
        }

        if (result.Data.FinishReason != LLMFinishReason.ToolCall || result.Data.ToolCalls == null || result.Data.ToolCalls.Count == 0)
        {
            // LLM didn't produce tool calls — return interpretation without filters
            return new NlFilterResult([], "none", result.Data.Content?.Trim(), []);
        }

        // Process tool calls to build conditions
        var (conditions, filters, combination, titleSearchTerms) = ProcessToolCalls(result.Data.ToolCalls, entityName, schema);

        if (conditions == null && titleSearchTerms.Count == 0)
        {
            return new NlFilterResult(filters, combination, result.Data.Content?.Trim(), []);
        }

        // Execute the filtered query
        var tableName = EntityRepositoryService.GetEntityTableName(entityName);
        IReadOnlyList<JObject> scanItems;

        if (conditions != null)
        {
            var scanOp = await AiConfiguration.DatabaseService.ScanTableWithFilterAsync(
                tableName, conditions, cancellationToken);
            scanItems = scanOp.IsSuccessful ? scanOp.Data.Items : [];
        }
        else
        {
            // Title-only search: scan all items then filter in memory
            var scanOp = await AiConfiguration.DatabaseService.ScanTableAsync(tableName, cancellationToken);
            scanItems = scanOp.IsSuccessful ? scanOp.Data.Items : [];
        }

        var resultItems = new List<JObject>();
        foreach (var item in scanItems)
        {
            // Apply title search post-filter
            if (titleSearchTerms.Count > 0)
            {
                var title = item[EntityModelAttributes.Title]?[EntityModelAttributes.TitleRendered]?.Value<string>();
                if (title == null || !titleSearchTerms.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }
            resultItems.Add(item);
        }

        // If structured/title search returned zero results, fall back to vector semantic search
        if (resultItems.Count == 0)
        {
            var vectorResults = await VectorFallbackSearchAsync(entityName, query, cancellationToken);
            if (vectorResults.Count > 0)
            {
                var interpretation = filters.Count > 0
                    ? $"No exact matches for: {string.Join(", ", filters.Select(f => $"{f.Field} {f.Operator} {f.Value}"))}. Showing similar results via semantic search."
                    : "Showing similar results via semantic search.";
                return new NlFilterResult(filters, combination, interpretation, vectorResults, UsedVectorFallback: true);
            }
        }

        // Extract NL interpretation from LLM if available
        string? interpretation2 = null;
        if (filters.Count > 0)
        {
            var filterDesc = string.Join(", ", filters.Select(f => $"{f.Field} {f.Operator} {f.Value}"));
            interpretation2 = $"Filters applied: {filterDesc}";
        }

        return new NlFilterResult(filters, combination, interpretation2, resultItems);
    }

    /// <summary>
    /// Falls back to vector semantic search when structured filtering yields no results.
    /// Embeds the original query and searches the entity's vector collection.
    /// Returns full entity JObjects for matching vector points.
    /// </summary>
    private static async Task<List<JObject>> VectorFallbackSearchAsync(
        string entityName, string query, CancellationToken cancellationToken)
    {
        try
        {
            var collectionName = AiVectorIndexer.GetCollectionName(entityName);

            var vectorResults = await AiConfiguration.VectorService.SemanticSearchAsync(
                AiConfiguration.LightLlmService, collectionName, query,
                topK: 20, filter: null, includeMetadata: true, cancellationToken);

            if (!vectorResults.IsSuccessful || vectorResults.Data == null || vectorResults.Data.Count == 0)
                return [];

            var tableName = EntityRepositoryService.GetEntityTableName(entityName);
            var results = new List<JObject>();

            foreach (var point in vectorResults.Data)
            {
                if (!int.TryParse(point.Id, out var entityId)) continue;

                var getResult = await AiConfiguration.DatabaseService.GetItemAsync(
                    tableName,
                    new DbKey(EntityModelAttributes.Id, entityId),
                    null,
                    cancellationToken);

                if (getResult.IsSuccessful && getResult.Data != null)
                    results.Add(getResult.Data);
            }

            return results;
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError("AiNaturalLanguageFilterHandler: Vector fallback search failed.", ex);
            return [];
        }
    }

    private static string BuildSchemaContext(EntitySchema schema)
    {
        var lines = new List<string>();
        if (schema.Fields == null) return string.Empty;

        foreach (var field in schema.Fields)
        {
            BuildFieldContext(field, "fields", lines);
        }
        return string.Join("\n", lines);
    }

    private static void BuildFieldContext(FieldSchema field, string prefix, List<string> lines)
    {
        var fullPath = $"{prefix}.{field.Name}";
        var desc = $"- {fullPath}: {field.Type}";

        if (!string.IsNullOrEmpty(field.Label))
            desc += $" (label: \"{field.Label}\")";

        if (field.SelectOptions?.Choices != null)
        {
            var choiceValues = field.SelectOptions.Choices.Select(c => c.Value).ToArray();
            desc += $" [choices: {string.Join(", ", choiceValues)}]";
        }

        if (field.NumberOptions != null)
        {
            if (field.NumberOptions.Min.HasValue)
                desc += $" [min: {field.NumberOptions.Min}]";
            if (field.NumberOptions.Max.HasValue)
                desc += $" [max: {field.NumberOptions.Max}]";
        }

        lines.Add(desc);

        if (field.GroupOptions?.ChildSchema != null)
        {
            foreach (var child in field.GroupOptions.ChildSchema)
                BuildFieldContext(child, fullPath, lines);
        }

        if (field.RepeaterOptions?.ItemSchema != null)
        {
            foreach (var child in field.RepeaterOptions.ItemSchema)
                BuildFieldContext(child, $"{fullPath}[]", lines);
        }
    }

    private static LLMToolDefinition[] BuildFilterTools()
    {
        return
        [
            BuildTool("filter_title_search", "Search entities by title. Use this when the user asks about a topic, name, or content rather than a specific structured field.",
                ("search_text", "string", "The text to search for in entity titles (case-insensitive substring match)")),

            BuildTool("filter_equals", "Create a filter that matches when a field equals a value.",
                ("field_name", "string", "The field path (e.g. 'fields.status')"),
                ("value", "string", "The value to match")),

            BuildTool("filter_not_equals", "Create a filter that matches when a field does not equal a value.",
                ("field_name", "string", "The field path"),
                ("value", "string", "The value to not match")),

            BuildTool("filter_greater_than", "Create a filter that matches when a field is greater than a value.",
                ("field_name", "string", "The field path"),
                ("value", "string", "The threshold value")),

            BuildTool("filter_less_than", "Create a filter that matches when a field is less than a value.",
                ("field_name", "string", "The field path"),
                ("value", "string", "The threshold value")),

            BuildTool("filter_greater_or_equal", "Create a filter that matches when a field is >= a value.",
                ("field_name", "string", "The field path"),
                ("value", "string", "The threshold value")),

            BuildTool("filter_less_or_equal", "Create a filter that matches when a field is <= a value.",
                ("field_name", "string", "The field path"),
                ("value", "string", "The threshold value")),

            BuildTool("filter_contains", "Create a filter that matches when an array field contains a value.",
                ("field_name", "string", "The array field path"),
                ("value", "string", "The value to check membership for")),

            new LLMToolDefinition
            {
                Name = "combine_and",
                Description = "Combine multiple filters with AND logic. Call this after creating individual filters.",
                Parameters = JObject.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "filter_ids": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "IDs of filters to combine with AND"
                        }
                    },
                    "required": ["filter_ids"]
                }
                """)
            },

            new LLMToolDefinition
            {
                Name = "combine_or",
                Description = "Combine multiple filters with OR logic. Call this after creating individual filters.",
                Parameters = JObject.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "filter_ids": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "IDs of filters to combine with OR"
                        }
                    },
                    "required": ["filter_ids"]
                }
                """)
            }
        ];
    }

    private static LLMToolDefinition BuildTool(string name, string description,
        params (string name, string type, string desc)[] parameters)
    {
        var props = new JObject();
        var required = new JArray();
        foreach (var (pName, pType, pDesc) in parameters)
        {
            props[pName] = new JObject { ["type"] = pType, ["description"] = pDesc };
            required.Add(pName);
        }

        return new LLMToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = required
            }
        };
    }

    /// <summary>
    /// Validates that a field_name from LLM output maps to an actual developer-defined field.
    /// Prevents injection of system fields (shared_users, author, etc.).
    /// </summary>
    private static bool IsValidFieldPath(string fieldPath, EntitySchema schema)
    {
        if (schema.Fields == null) return false;

        // Must start with "fields."
        if (!fieldPath.StartsWith("fields."))
            return false;

        var path = fieldPath["fields.".Length..];
        return ValidateFieldPathRecursive(path, schema.Fields);
    }

    private static bool ValidateFieldPathRecursive(string path, IReadOnlyList<FieldSchema> fields)
    {
        var dotIndex = path.IndexOf('.');
        var currentName = dotIndex >= 0 ? path[..dotIndex] : path;

        var field = fields.FirstOrDefault(f => f.Name == currentName);
        if (field == null) return false;

        if (dotIndex < 0) return true; // Leaf field found

        var remaining = path[(dotIndex + 1)..];

        // Navigate into Group or Repeater children
        if (field.GroupOptions?.ChildSchema != null)
            return ValidateFieldPathRecursive(remaining, field.GroupOptions.ChildSchema);
        if (field.RepeaterOptions?.ItemSchema != null)
            return ValidateFieldPathRecursive(remaining, field.RepeaterOptions.ItemSchema);

        return false; // Nested path but not a group/repeater
    }

    private static (ConditionCoupling? conditions, List<NlInterpretedFilter> filters, string combination, List<string> titleSearchTerms) ProcessToolCalls(
        IReadOnlyList<LLMToolCall> toolCalls, string entityName, EntitySchema schema)
    {
        var filterConditions = new Dictionary<string, ConditionCoupling>();
        var interpretedFilters = new List<NlInterpretedFilter>();
        var titleSearchTerms = new List<string>();
        var combination = "and";
        ConditionCoupling? finalCondition = null;
        var filterIndex = 0;

        foreach (var tc in toolCalls)
        {
            var args = tc.Arguments != null ? JObject.Parse(tc.Arguments) : new JObject();

            if (tc.Name is "combine_and" or "combine_or")
            {
                combination = tc.Name == "combine_and" ? "and" : "or";
                var filterIds = args["filter_ids"]?.ToObject<string[]>();
                if (filterIds != null && filterIds.Length > 0)
                {
                    var toCompose = filterIds
                        .Where(filterConditions.ContainsKey)
                        .Select(id => filterConditions[id])
                        .ToList();

                    if (toCompose.Count > 0)
                    {
                        finalCondition = toCompose[0];
                        for (var i = 1; i < toCompose.Count; i++)
                        {
                            finalCondition = tc.Name == "combine_and"
                                ? finalCondition.And(toCompose[i])
                                : finalCondition.Or(toCompose[i]);
                        }
                    }
                }
                continue;
            }

            if (tc.Name == "filter_title_search")
            {
                var searchText = args["search_text"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    titleSearchTerms.Add(searchText);
                    interpretedFilters.Add(new NlInterpretedFilter("title", "contains", searchText));
                }
                continue;
            }

            var fieldName = args["field_name"]?.Value<string>();
            var valueStr = args["value"]?.Value<string>();

            if (string.IsNullOrEmpty(fieldName) || valueStr == null) continue;

            // Security: validate field path against schema
            if (!IsValidFieldPath(fieldName, schema))
            {
                RfConfiguration.LogError($"AiNaturalLanguageFilterHandler: LLM produced invalid field path '{fieldName}' for {entityName}. Skipping.");
                continue;
            }

            var primitive = ParseValueToPrimitive(valueStr);
            var filterId = $"f{filterIndex++}";

            Condition? condition = tc.Name switch
            {
                "filter_equals" => ConditionBuilder.AttributeEquals(fieldName, primitive),
                "filter_not_equals" => ConditionBuilder.AttributeNotEquals(fieldName, primitive),
                "filter_greater_than" => ConditionBuilder.AttributeIsGreaterThan(fieldName, primitive),
                "filter_less_than" => ConditionBuilder.AttributeIsLessThan(fieldName, primitive),
                "filter_greater_or_equal" => ConditionBuilder.AttributeIsGreaterOrEqual(fieldName, primitive),
                "filter_less_or_equal" => ConditionBuilder.AttributeIsLessOrEqual(fieldName, primitive),
                "filter_contains" => ConditionBuilder.ArrayElementExists(fieldName, primitive),
                _ => null
            };

            if (condition != null)
            {
                filterConditions[filterId] = condition;
                interpretedFilters.Add(new NlInterpretedFilter(fieldName, tc.Name.Replace("filter_", ""), valueStr));
            }
        }

        // If no combine was called but we have individual filters, AND them together
        if (finalCondition == null && filterConditions.Count > 0)
        {
            var all = filterConditions.Values.ToList();
            finalCondition = all[0];
            for (var i = 1; i < all.Count; i++)
                finalCondition = finalCondition.And(all[i]);
        }

        return (finalCondition, interpretedFilters, combination, titleSearchTerms);
    }

    private static Primitive ParseValueToPrimitive(string value)
    {
        // Try parsing as long
        if (long.TryParse(value, out var longVal))
            return new Primitive(longVal);

        // Try parsing as double
        if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
            return new Primitive(doubleVal);

        // Try parsing as bool
        if (bool.TryParse(value, out var boolVal))
            return new Primitive(boolVal);

        // Default: string
        return new Primitive(value);
    }
}
