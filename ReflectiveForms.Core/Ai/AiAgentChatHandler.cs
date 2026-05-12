// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Net;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Endpoints.Mapped.Api;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Core.Schema.Models;
using static ReflectiveForms.Core.Endpoints.Mapped.Api.Crud;

namespace ReflectiveForms.Core.Ai;

internal record AgentChatResult(string Response, List<AgentToolCallLog> ToolCallsMade, List<ProposedAction> ProposedActions);
internal record AgentToolCallLog(string ToolName, JObject Arguments, string Result);

internal record ProposedAction
{
    internal required string ActionId { get; init; }
    internal required string ActionType { get; init; } // create_entity, update_entity, delete_entity, set_field, navigate, show_quality_report
    internal string? EntityType { get; init; }
    internal int? EntityId { get; init; }
    internal JObject? Payload { get; set; }
    internal required string Description { get; init; }
    internal required bool RequiresApproval { get; init; }
}

internal record AgentChatRequest
{
    internal required string Message { get; init; }
    internal AgentContext? Context { get; init; }
    internal List<ActionConfirmation>? ConfirmedActions { get; init; }
    internal List<ActionExecutionResult>? ExecutedActionResults { get; init; }
    /// <summary>
    /// Previous conversation turns (user + assistant messages) for multi-turn context.
    /// Tool call details are excluded — only the final assistant text per turn is included.
    /// </summary>
    internal List<ChatHistoryEntry>? History { get; init; }
}

internal record ChatHistoryEntry
{
    internal required string Role { get; init; } // "user" or "assistant"
    internal required string Content { get; init; }
}

internal record AgentContext
{
    internal string? CurrentPage { get; init; }
    internal string? EntityType { get; init; }
    internal int? EntityId { get; init; }
    internal JObject? CurrentFields { get; init; }
    internal List<string>? Errors { get; init; }
    internal string? SelectedField { get; init; }
    internal List<string>? SheetSources { get; init; }
    internal string? SelectedCell { get; init; }
}

internal record ActionConfirmation
{
    internal required string ActionId { get; init; }
    internal required bool Approved { get; init; }
}

internal record ActionExecutionResult
{
    internal required string ActionId { get; init; }
    internal required bool Success { get; init; }
    internal required string Message { get; init; }
}

/// <summary>
/// Multi-turn agent loop: the Heavy LLM decides what data to fetch via tools,
/// iterates until it has enough context, then produces a final answer.
/// All data access is filtered by the requesting user's permissions.
/// </summary>
internal static class AiAgentChatHandler
{
    private const int MaxIterations = 8;

    private static readonly LLMToolDefinition[] Tools =
    [
        new()
        {
            Name = "list_entity_types",
            Description = "List all entity types the user has access to, with counts and descriptions.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        },
        new()
        {
            Name = "search_entities",
            Description = "Semantic search across entities by natural language query. Returns matching entities ranked by relevance with their titles and summaries.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "The search query in natural language" },
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "Optional: limit search to a specific entity type (e.g. 'objective', 'survey')" },
                    ["top_k"] = new JObject { ["type"] = "integer", ["description"] = "Number of results to return (default 5, max 20)" }
                },
                ["required"] = new JArray { "query" }
            }
        },
        new()
        {
            Name = "get_entity",
            Description = "Get full details of a specific entity by type and ID.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type (e.g. 'objective', 'blog-post')" },
                    ["entity_id"] = new JObject { ["type"] = "integer", ["description"] = "The entity ID" }
                },
                ["required"] = new JArray { "entity_type", "entity_id" }
            }
        },
        new()
        {
            Name = "get_entity_schema",
            Description = "Get the field schema of an entity type: field names, types, labels, and allowed values.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type to get schema for" }
                },
                ["required"] = new JArray { "entity_type" }
            }
        },
        new()
        {
            Name = "generate_entity",
            Description = "Generate a draft entity from a natural language description. Returns draft field values (NOT saved). The user should review before saving.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type to generate (e.g. 'blog-post', 'objective')" },
                    ["prompt"] = new JObject { ["type"] = "string", ["description"] = "Natural language description of the entity to create" }
                },
                ["required"] = new JArray { "entity_type", "prompt" }
            }
        },
        new()
        {
            Name = "filter_entities",
            Description = "Filter entities using structured criteria (status, dates, numeric comparisons). Use this for precise queries like 'objectives with status at-risk' or 'posts created after January'. For conceptual/semantic queries, use search_entities instead.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type to filter" },
                    ["query"] = new JObject { ["type"] = "string", ["description"] = "Natural language filter query (e.g. 'status is active and age greater than 30')" }
                },
                ["required"] = new JArray { "entity_type", "query" }
            }
        },
        new()
        {
            Name = "summarize_changes",
            Description = "Summarize what changed between two revisions of an entity. Returns a human-readable diff summary.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type" },
                    ["entity_id"] = new JObject { ["type"] = "integer", ["description"] = "The entity ID" },
                    ["revision_index"] = new JObject { ["type"] = "integer", ["description"] = "The revision number to compare (compares revision N with N+1)" }
                },
                ["required"] = new JArray { "entity_type", "entity_id", "revision_index" }
            }
        },
        new()
        {
            Name = "check_entity_quality",
            Description = "Run AI quality checks on an entity's fields. Returns a pass/fail report for each configured check (spelling, tone, completeness, etc.).",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type" },
                    ["entity_id"] = new JObject { ["type"] = "integer", ["description"] = "The entity ID to check" }
                },
                ["required"] = new JArray { "entity_type", "entity_id" }
            }
        },
        new()
        {
            Name = "propose_create_entity",
            Description = "Propose creating a new entity with the given fields. The entity is NOT created immediately — the user must approve first. Use generate_entity to draft fields from a description, then pass the fields object EXACTLY as returned — do NOT reformat or localize any field values (especially dates).",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type to create" },
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "The entity title" },
                    ["fields"] = new JObject { ["type"] = "object", ["description"] = "The field values for the new entity (JSON object)" }
                },
                ["required"] = new JArray { "entity_type", "title", "fields" }
            }
        },
        new()
        {
            Name = "propose_update_entity",
            Description = "Propose updating fields on an existing entity. The update is NOT applied immediately — the user must approve first.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type" },
                    ["entity_id"] = new JObject { ["type"] = "integer", ["description"] = "The entity ID to update" },
                    ["fields"] = new JObject { ["type"] = "object", ["description"] = "The field values to update (JSON object, partial update)" }
                },
                ["required"] = new JArray { "entity_type", "entity_id", "fields" }
            }
        },
        new()
        {
            Name = "propose_delete_entity",
            Description = "Propose deleting an entity. The deletion is NOT performed immediately — the user must approve first.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type" },
                    ["entity_id"] = new JObject { ["type"] = "integer", ["description"] = "The entity ID to delete" }
                },
                ["required"] = new JArray { "entity_type", "entity_id" }
            }
        },
        new()
        {
            Name = "suggest_field_value",
            Description = "Suggest a value for a specific field based on the entity's other field values. Returns a proposed field value the user can apply.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type" },
                    ["target_field"] = new JObject { ["type"] = "string", ["description"] = "The field name to suggest a value for" },
                    ["current_fields"] = new JObject { ["type"] = "object", ["description"] = "Current field values of the entity (JSON object)" }
                },
                ["required"] = new JArray { "entity_type", "target_field", "current_fields" }
            }
        },
        new()
        {
            Name = "navigate",
            Description = "Navigate to a page. For 'dashboard', only 'page' is needed (no entity_type). For entity pages, 'entity_type' is required.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["page"] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = "The page to navigate to: 'dashboard', 'entity-list', 'entity-edit', 'entity-create', 'revision-diff'",
                        ["enum"] = new JArray { "dashboard", "entity-list", "entity-edit", "entity-create", "revision-diff" }
                    },
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type (required for entity pages, not needed for dashboard)" },
                    ["entity_id"] = new JObject { ["type"] = "integer", ["description"] = "The entity ID (required for entity-edit and revision-diff)" }
                },
                ["required"] = new JArray { "page" }
            }
        },
        new()
        {
            Name = "list_entities",
            Description = "List entities of a specific type with their titles and IDs. Returns a paginated list. Use this to browse or find entities when semantic search is not needed.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type to list" },
                    ["limit"] = new JObject { ["type"] = "integer", ["description"] = "Maximum number of entities to return (default 20, max 50)" }
                },
                ["required"] = new JArray { "entity_type" }
            }
        },
        new()
        {
            Name = "set_form_fields",
            Description = "Set multiple field values on the entity form the user is currently editing or creating. " +
                          "Use this when the user asks you to fill in, populate, or set field values directly (e.g. 'fill with random data', 'set all fields'). " +
                          "Each field is proposed as an action — the user can apply or reject. " +
                          "Do NOT ask the user to confirm — the proposal UI handles approval.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "The entity type being edited" },
                    ["title"] = new JObject { ["type"] = "string", ["description"] = "Optional: the entity title to set" },
                    ["fields"] = new JObject
                    {
                        ["type"] = "object",
                        ["description"] = "Object mapping field names to their values. Use the field schema to determine correct types and allowed values."
                    }
                },
                ["required"] = new JArray { "entity_type", "fields" }
            }
        }
    ];

    /// <summary>
    /// Sheet-specific tools — only included in the LLM tool list when the user is on a sheet page.
    /// </summary>
    private static readonly LLMToolDefinition[] SheetTools =
    [
        new()
        {
            Name = "list_sheets",
            Description = "List all spreadsheets (RF Sheets) the user has access to, with titles and entity sources.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject(),
                ["required"] = new JArray()
            }
        },
        new()
        {
            Name = "get_sheet_summary",
            Description = "Get a summary of a specific sheet: title, entity sources, formula inventory (which RF formulas are used and how many), total cell count, and refresh interval.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sheet_id"] = new JObject { ["type"] = "integer", ["description"] = "The sheet ID" }
                },
                ["required"] = new JArray { "sheet_id" }
            }
        },
        new()
        {
            Name = "get_sheet_cell_value",
            Description = "Read cell values from a sheet. Returns the cached value and formula for each cell in the range.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sheet_id"] = new JObject { ["type"] = "integer", ["description"] = "The sheet ID" },
                    ["range"] = new JObject { ["type"] = "string", ["description"] = "Cell reference (e.g. 'A1') or range (e.g. 'A1:C5'). Column letters A-Z, row numbers 1-based." }
                },
                ["required"] = new JArray { "sheet_id", "range" }
            }
        },
        new()
        {
            Name = "suggest_formula",
            Description = "Suggest an RF formula based on a natural language description. Returns a formula string (e.g. '=RF.SUM(\"product\",\"price\")') that the user can paste into a cell. Does NOT modify the sheet.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["description"] = new JObject { ["type"] = "string", ["description"] = "What the user wants to calculate or display (e.g. 'total price of all products', 'list of employee names')" },
                    ["entity_type"] = new JObject { ["type"] = "string", ["description"] = "Optional: the entity type to use in the formula" },
                    ["field_name"] = new JObject { ["type"] = "string", ["description"] = "Optional: the field name to use in the formula" }
                },
                ["required"] = new JArray { "description" }
            }
        },
        new()
        {
            Name = "propose_sheet_formulas",
            Description = "Propose writing values or formulas to cells in a sheet. Each operation sets a cell's content. The user must approve before changes are applied. Use this to build tables, add headers, or insert formulas.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sheet_id"] = new JObject { ["type"] = "integer", ["description"] = "The sheet ID to edit" },
                    ["operations"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Array of cell operations. Each has row (0-indexed), col (0-indexed), and either 'value' (plain text/number) or 'formula' (starts with =). Max 500 operations.",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["row"] = new JObject { ["type"] = "integer", ["description"] = "Row index (0-based)" },
                                ["col"] = new JObject { ["type"] = "integer", ["description"] = "Column index (0-based)" },
                                ["value"] = new JObject { ["type"] = "string", ["description"] = "Plain text or number to set (mutually exclusive with formula)" },
                                ["formula"] = new JObject { ["type"] = "string", ["description"] = "Formula string starting with = (e.g. '=RF.SUM(\"product\",\"price\")')" }
                            },
                            ["required"] = new JArray { "row", "col" }
                        }
                    }
                },
                ["required"] = new JArray { "sheet_id", "operations" }
            }
        },
        new()
        {
            Name = "propose_add_sheet_source",
            Description = "Propose adding a new entity data source to a sheet. Once added, the sheet can use RF formulas to reference data from this entity type. The user must approve before the source is added.",
            Parameters = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["sheet_id"] = new JObject { ["type"] = "integer", ["description"] = "The sheet ID" },
                    ["entity"] = new JObject { ["type"] = "string", ["description"] = "The entity type to add as a source (e.g. 'product', 'objective')" },
                    ["fields"] = new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional: specific fields to include from this entity (if omitted, all fields are available)",
                        ["items"] = new JObject { ["type"] = "string" }
                    }
                },
                ["required"] = new JArray { "sheet_id", "entity" }
            }
        }
    ];

    private static int _actionIdCounter;

    internal static async Task<AgentChatResult> ChatAsync(
        AgentChatRequest chatRequest, EntityModel<UserEntityFieldsModel> user, CancellationToken cancellationToken)
    {
        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var toolCallLog = new List<AgentToolCallLog>();
        var proposedActions = new List<ProposedAction>();
        // Cache generated fields so propose_create_entity can use them even if the LLM
        // drops or simplifies fields when re-serializing the tool call arguments.
        var lastGeneratedFields = new Dictionary<string, (string? title, JObject fields)>();

        // Process confirmed actions from previous turn
        var confirmationContext = "";
        if (chatRequest.ConfirmedActions is { Count: > 0 })
        {
            confirmationContext = await ProcessConfirmedActionsAsync(
                chatRequest.ConfirmedActions, chatRequest.ExecutedActionResults, user, cancellationToken);
        }

        // Build environment context for the system prompt
        var entityTypesSummary = string.Join("\n", RfConfiguration.EntityNameToConfiguration
            .Select(kv =>
            {
                var ec = kv.Value.EntityConfiguration;
                var desc = !string.IsNullOrWhiteSpace(ec.EntityDescription) ? $" — {ec.EntityDescription}" : "";
                return $"  - {ec.EntityReadableNamePlural} ({kv.Key}){desc}";
            }));

        var userDisplayName = user.Title?.Text ?? "Unknown";
        var userEmail = user.Fields?.EmailAddress ?? "";
        var userRoleIds = user.Fields?.Roles?.Select(r => r.RoleId).ToArray() ?? [];
        var userRolesDesc = userRoleIds.Length > 0
            ? string.Join(", ", userRoleIds.Select(id =>
            {
                var role = RfConfiguration.IamRoleEntitiesCache.GetEntityCopy(id);
                return role != null ? (role.Title?.Text ?? $"Role #{id}") : $"Role #{id}";
            }))
            : "none";

        var systemPrompt = "ABSOLUTE RULE — LANGUAGE: You MUST detect the language of the user's message and reply in EXACTLY that language. " +
            "Default to English, unless the user's message is written in that specific language. " +
            "If the user writes in English, every word of your response must be in English. NO EXCEPTIONS.\n\n" +
            "SYSTEM:\n" +
            "This is a content management system built with ReflectiveForms. " +
            "Data is organized into typed entities, each with a defined schema of fields. " +
            "Users create, read, update, and delete entities through the UI or via your assistance. " +
            "Each entity type has its own set of fields, validation rules, and access controls.\n\n" +
            "ENVIRONMENT:\n" +
            $"- Current date/time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}\n" +
            $"- User: {userDisplayName} (ID: {user.Id}, email: {userEmail}, roles: {userRolesDesc})\n" +
            $"- Entity types:\n{entityTypesSummary}\n\n" +
            aiConfig.SystemPromptPrefix + "\n\n" +
            "You are an AI assistant embedded in this content management system. " +
            "You help the user browse, search, create, update, and manage entities. " +
            "You can only see data the user has permission to access. " +
            "Always use tools to verify facts — do not guess or make up data.\n\n" +
            "IMPORTANT RULES:\n" +
            "- For ANY write operation (create, update, delete), use the propose_ tools. " +
            "These create proposals that the user must approve via UI buttons before execution.\n" +
            "- NEVER claim you have created, updated, or deleted something — you can only propose.\n" +
            "- NEVER ask the user to confirm in chat — the approval UI handles that automatically.\n" +
            "- For read operations (list, search, get, schema), execute directly.\n" +
            "- When suggesting a field value, use suggest_field_value and describe what you're proposing.\n" +
            "- When the user asks to fill, populate, or set multiple field values (e.g. 'fill with random data', 'populate all fields', 'set fields to test data'), " +
            "first call get_entity_schema to know the fields, then call set_form_fields with all the values at once. Do NOT list values in chat and ask for confirmation.\n\n" +
            "Tool guidance:\n" +
            "- If the user's request is ambiguous about which entity type to use, call list_entity_types first to discover available types, then pick the best match or ask the user.\n" +
            "- Use search_entities for semantic queries, filter_entities for structured criteria, list_entities to browse.\n" +
            "- When asked to CREATE an entity and the user references existing entities (e.g. 'inspired by', 'similar to', 'based on', 'like the others'): " +
            "you MUST read actual entity content before generating. Use search_entities to find the most relevant entities — " +
            "results include summaries with content. If you need more detail, call get_entity on 1-2 top results. " +
            "Then call generate_entity with the gathered content included in the prompt, " +
            "then IMMEDIATELY call propose_create_entity with the generated content in the SAME turn. Do NOT stop to ask the user.\n" +
            "- When asked to CREATE an entity WITHOUT referencing existing ones: call generate_entity to draft the fields, then IMMEDIATELY call propose_create_entity " +
            "with the generated content in the SAME turn. Do NOT stop to ask the user first — the proposal UI will collect approval.\n" +
            "- Use propose_update_entity to propose field changes on existing entities.\n" +
            "- Use propose_delete_entity to propose removing an entity.\n" +
            "- Use suggest_field_value to suggest a value for a specific field.\n" +
            "- Use set_form_fields to set multiple field values at once on the current form (e.g. when asked to fill with random or test data).\n" +
            "- Use check_entity_quality to validate content quality.\n" +
            "- Use summarize_changes to explain revision diffs.\n" +
            "- Use navigate to take the user to a specific page (dashboard, list, edit, create, revisions). " +
            "For 'dashboard', use page='dashboard' with no entity_type.\n\n" +
            "WORKFLOW: When the user asks you to create something, you must: " +
            "(0) if the user references existing entities, call search_entities to find relevant ones (results include content summaries), " +
            "optionally call get_entity on 1-2 top results for full detail, " +
            "(1) call generate_entity (include the gathered entity content in the prompt so it can produce similar output), " +
            "(2) call propose_create_entity — steps 1-2 must happen in the same turn without stopping. " +
            "After proposing, briefly describe what you proposed. The user will see Apply/Reject buttons.\n\n" +
            "CRITICAL: After calling generate_entity, you MUST call propose_create_entity in your next tool call. " +
            "Do NOT respond to the user between these two calls. Do NOT ask 'Would you like me to...' or 'Shall I...'. " +
            "The generate→propose sequence must be uninterrupted.\n\n" +
            "REMINDER: Your response text MUST be in the same language as the user's message. Default to English.";

        // Conditionally append RF Sheet formula reference when user is on a sheet page AND has access AND sheets are enabled
        var isOnSheetPage = RfConfiguration.SheetsEnabled
                            && chatRequest.Context?.CurrentPage?.StartsWith("sheet", StringComparison.OrdinalIgnoreCase) == true
                            && user.Fields?.CanUserDo("PEEK_ALL", RfReservedEntities.SheetsEntityName) == true;
        if (isOnSheetPage)
        {
            systemPrompt += SheetSystemPromptExtension;
        }

        // Build the user message with context
        var userContent = chatRequest.Message;
        if (chatRequest.Context != null)
        {
            var ctx = chatRequest.Context;
            var contextParts = new List<string>();
            if (!string.IsNullOrEmpty(ctx.CurrentPage))
                contextParts.Add($"Current page: {ctx.CurrentPage}");
            if (!string.IsNullOrEmpty(ctx.EntityType))
                contextParts.Add($"Entity type: {ctx.EntityType}");
            if (ctx.EntityId.HasValue)
                contextParts.Add($"Entity ID: {ctx.EntityId}");
            if (!string.IsNullOrEmpty(ctx.SelectedField))
                contextParts.Add($"Selected field: {ctx.SelectedField}");
            if (ctx.Errors is { Count: > 0 })
                contextParts.Add($"Current errors: {string.Join("; ", ctx.Errors)}");
            if (ctx.CurrentFields != null)
                contextParts.Add($"Current field values: {TruncateToolResult(ctx.CurrentFields.ToString(Newtonsoft.Json.Formatting.None))}");
            if (ctx.SheetSources is { Count: > 0 })
                contextParts.Add($"Sheet data sources: {string.Join(", ", ctx.SheetSources)}");
            if (!string.IsNullOrEmpty(ctx.SelectedCell))
                contextParts.Add($"Selected cell: {ctx.SelectedCell}");

            if (contextParts.Count > 0)
                userContent = $"[Context: {string.Join(", ", contextParts)}]\n\n{chatRequest.Message}";
        }

        if (!string.IsNullOrEmpty(confirmationContext))
            userContent = $"[Action results: {confirmationContext}]\n\n{userContent}";

        var messages = new List<LLMMessage>
        {
            new() { Role = LLMRole.System, Content = systemPrompt }
        };

        // Inject previous conversation turns for multi-turn context
        if (chatRequest.History is { Count: > 0 })
        {
            // Keep only the last N turns to stay within token budget
            const int maxHistoryTurns = 20;
            var historyToInclude = chatRequest.History.Count > maxHistoryTurns
                ? chatRequest.History[^maxHistoryTurns..]
                : chatRequest.History;

            foreach (var entry in historyToInclude)
            {
                var role = entry.Role == "assistant" ? LLMRole.Assistant : LLMRole.User;
                messages.Add(new LLMMessage { Role = role, Content = entry.Content });
            }
        }

        messages.Add(new LLMMessage { Role = LLMRole.User, Content = userContent });

        // Conditionally include sheet tools when user is on a sheet page
        var activeTools = isOnSheetPage ? Tools.Concat(SheetTools).ToArray() : Tools;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var request = new LLMRequest
            {
                Messages = messages,
                Tools = activeTools,
                MaxTokens = aiConfig.MaxCompletionTokens,
                Temperature = aiConfig.Temperature
            };

            var result = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
            if (!result.IsSuccessful)
            {
                return new AgentChatResult(
                    $"I encountered an error while processing your request: {result.ErrorMessage}",
                    toolCallLog, proposedActions);
            }

            // If the LLM wants to call tools, execute them and loop
            if (result.Data.FinishReason == LLMFinishReason.ToolCall &&
                result.Data.ToolCalls is { Count: > 0 })
            {
                // Add the assistant's response (with tool calls) to message history
                messages.Add(new LLMMessage { Role = LLMRole.Assistant, Content = result.Data.Content, ToolCalls = result.Data.ToolCalls.ToList() });

                foreach (var toolCall in result.Data.ToolCalls)
                {
                    JObject args;
                    string toolResult;

                    try
                    {
                        args = string.IsNullOrWhiteSpace(toolCall.Arguments)
                            ? new JObject()
                            : JObject.Parse(toolCall.Arguments);
                    }
                    catch (Exception)
                    {
                        args = new JObject { ["_raw"] = toolCall.Arguments };
                        toolResult = $"Tool execution failed: could not parse arguments as JSON.";
                        toolCallLog.Add(new AgentToolCallLog(toolCall.Name, args, toolResult));
                        messages.Add(new LLMMessage
                        {
                            Role = LLMRole.Tool,
                            ToolCallId = toolCall.Id,
                            Content = toolResult
                        });
                        continue;
                    }

                    toolResult = await ExecuteToolAsync(toolCall.Name, args, user, proposedActions, lastGeneratedFields, cancellationToken);

                    toolCallLog.Add(new AgentToolCallLog(toolCall.Name, args, toolResult));

                    messages.Add(new LLMMessage
                    {
                        Role = LLMRole.Tool,
                        ToolCallId = toolCall.Id,
                        Content = toolResult
                    });
                }

                continue;
            }

            // LLM is done — return the final answer
            return new AgentChatResult(
                result.Data.Content?.Trim() ?? "I wasn't able to generate a response.",
                toolCallLog, proposedActions);
        }

        // Max iterations reached — return whatever we have
        return new AgentChatResult(
            "I've reached my tool-use limit. Based on what I've gathered so far, I'm unable to fully answer your question. Please try a more specific question.",
            toolCallLog, proposedActions);
    }

    private static async Task<string> ExecuteToolAsync(
        string toolName, JObject args, EntityModel<UserEntityFieldsModel> user,
        List<ProposedAction> proposedActions,
        Dictionary<string, (string? title, JObject fields)> lastGeneratedFields,
        CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                "list_entity_types" => await ExecuteListEntityTypes(user, ct),
                "search_entities" => await ExecuteSearchEntities(args, user, ct),
                "get_entity" => await ExecuteGetEntity(args, user, ct),
                "get_entity_schema" => ExecuteGetEntitySchema(args, user),
                "generate_entity" => await ExecuteGenerateEntity(args, user, lastGeneratedFields, ct),
                "filter_entities" => await ExecuteFilterEntities(args, user, ct),
                "summarize_changes" => await ExecuteSummarizeChanges(args, user, ct),
                "check_entity_quality" => await ExecuteCheckEntityQuality(args, user, ct),
                "propose_create_entity" => await ExecuteProposeCreate(args, user, proposedActions, lastGeneratedFields, ct),
                "propose_update_entity" => await ExecuteProposeUpdate(args, user, proposedActions, ct),
                "propose_delete_entity" => await ExecuteProposeDelete(args, user, proposedActions, ct),
                "suggest_field_value" => await ExecuteSuggestFieldValue(args, user, ct, proposedActions),
                "navigate" => ExecuteNavigate(args, user, proposedActions),
                "list_entities" => await ExecuteListEntities(args, user, ct),
                "set_form_fields" => ExecuteSetFormFields(args, user, proposedActions),
                // Sheet tools
                "list_sheets" => await ExecuteListSheets(user, ct),
                "get_sheet_summary" => await ExecuteGetSheetSummary(args, user, ct),
                "get_sheet_cell_value" => await ExecuteGetSheetCellValue(args, user, ct),
                "suggest_formula" => await ExecuteSuggestFormula(args, user, ct),
                "propose_sheet_formulas" => ExecuteProposeSheetFormulas(args, user, proposedActions),
                "propose_add_sheet_source" => ExecuteProposeAddSheetSource(args, user, proposedActions),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError(ex);
            return $"Tool execution failed: {ex.Message}";
        }
    }

    private static async Task<string> ExecuteListEntityTypes(
        EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var result = new JArray();

        foreach (var (entityName, configBase) in RfConfiguration.EntityNameToConfiguration)
        {
            if (!user.Fields.CanUserDo("PEEK_ALL", entityName))
                continue;

            var config = configBase.EntityConfiguration;

            // Count entities
            var scanResult = await AiConfiguration.DatabaseService.ScanTableAsync(
                EntityRepositoryService.GetEntityTableName(entityName), ct);
            var count = scanResult.IsSuccessful ? scanResult.Data.Items.Count : 0;

            result.Add(new JObject
            {
                ["name"] = entityName,
                ["readable_name"] = config.EntityReadableNamePlural,
                ["description"] = config.EntityDescription,
                ["count"] = count,
                ["supports_search"] = config.SupportsSemanticSearch,
                ["supports_generation"] = config.SupportsAiGeneration,
                ["supports_filter"] = config.SupportsNaturalLanguageFilter,
                ["supports_diff_summary"] = config.SupportsAiDiffSummary,
                ["supports_quality_check"] = AiAttributeHelper.FindAllFieldsWithSanityChecks(
                    config.EntityFieldsModelType).Count > 0
            });
        }

        return result.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static async Task<string> ExecuteSearchEntities(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var query = args["query"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(query))
            return "Error: 'query' parameter is required.";

        var entityType = args["entity_type"]?.Value<string>();
        var topK = Math.Clamp(args["top_k"]?.Value<int>() ?? 5, 1, 20);

        // Determine which entity types to search
        var targetEntities = new List<(string EntityName, EntityFinalConfigurationBase Config)>();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
                return $"Error: Entity type '{entityType}' not found.";
            if (!configBase.EntityConfiguration.SupportsSemanticSearch)
                return $"Error: Entity type '{entityType}' does not support semantic search.";
            if (!user.Fields.CanUserDo("PEEK_ALL", entityType))
                return $"Error: You do not have access to '{entityType}'.";
            targetEntities.Add((entityType, configBase));
        }
        else
        {
            foreach (var (name, configBase) in RfConfiguration.EntityNameToConfiguration)
            {
                if (configBase.EntityConfiguration.SupportsSemanticSearch &&
                    user.Fields.CanUserDo("PEEK_ALL", name))
                {
                    targetEntities.Add((name, configBase));
                }
            }
        }

        if (targetEntities.Count == 0)
            return "No searchable entity types available.";

        var allResults = new List<(string EntityName, int EntityId, string Title, string Summary, double Score)>();

        foreach (var (targetEntityName, config) in targetEntities)
        {
            var collectionName = AiVectorIndexer.GetCollectionName(targetEntityName);

            var vectorResults = await AiConfiguration.VectorService.SemanticSearchAsync(
                AiConfiguration.LightLlmService, collectionName, query,
                topK: topK * 3, filter: null, includeMetadata: true, ct);

            if (!vectorResults.IsSuccessful || vectorResults.Data == null)
                continue;

            foreach (var candidate in vectorResults.Data)
            {
                if (!int.TryParse(candidate.Id, out var candidateEntityId))
                    continue;

                // Verify entity still exists
                var exists = await AiConfiguration.DatabaseService.GetItemAsync(
                    targetEntityName,
                    new DbKey(EntityModelAttributes.Id, candidateEntityId),
                    null, ct);

                if (!exists.IsSuccessful || exists.Data == null)
                    continue;

                // Per-entity sharing check
                if (config.EntityConfiguration.HasIndividualSharing)
                {
                    var accessLevel = GetEntitySharingAccessLevel(targetEntityName, exists.Data, user);
                    if (accessLevel == SharingAccessLevel.None)
                        continue;
                }

                var title = candidate.Metadata?["title"]?.Value<string>() ?? "";
                var summary = candidate.Metadata?["summary"]?.Value<string>() ?? "";
                allResults.Add((targetEntityName, candidateEntityId, title, summary, candidate.Score));
            }
        }

        allResults.Sort((a, b) => b.Score.CompareTo(a.Score));
        var finalResults = allResults.Count > topK ? allResults.GetRange(0, topK) : allResults;

        if (finalResults.Count == 0)
            return "No matching entities found.";

        var responseArray = new JArray();
        foreach (var (name, id, title, summary, score) in finalResults)
        {
            responseArray.Add(new JObject
            {
                ["entity_type"] = name,
                ["entity_id"] = id,
                ["title"] = title,
                ["summary"] = summary,
                ["score"] = Math.Round(score, 3)
            });
        }

        return responseArray.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static async Task<string> ExecuteGetEntity(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var entityType = args["entity_type"]?.Value<string>();
        var entityId = args["entity_id"]?.Value<int>();

        if (string.IsNullOrWhiteSpace(entityType) || entityId == null)
            return "Error: 'entity_type' and 'entity_id' are required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!user.Fields.CanUserDo("READ", entityType))
            return $"Error: You do not have read access to '{entityType}'.";

        var result = await AiConfiguration.DatabaseService.GetItemAsync(
            EntityRepositoryService.GetEntityTableName(entityType),
            new DbKey(EntityModelAttributes.Id, entityId.Value),
            null, ct);

        if (!result.IsSuccessful || result.Data == null)
            return $"Entity '{entityType}' with ID {entityId} not found.";

        var entity = result.Data;

        // Per-entity sharing check
        if (configBase.EntityConfiguration.HasIndividualSharing)
        {
            var accessLevel = GetEntitySharingAccessLevel(entityType, entity, user);
            if (accessLevel == SharingAccessLevel.None)
                return $"Error: You do not have access to this {entityType}.";
        }

        // Build a clean response: title + fields (strip internal metadata)
        var response = new JObject
        {
            ["entity_type"] = entityType,
            ["entity_id"] = entityId
        };

        if (entity.TryGetValue(EntityModelAttributes.Title, out var titleToken))
        {
            if (titleToken is JObject titleObj &&
                titleObj.TryGetValue(EntityModelAttributes.TitleRendered, out var renderedToken))
                response["title"] = renderedToken.Value<string>();
            else
                response["title"] = titleToken.ToString();
        }

        if (entity[EntityModelAttributes.Fields] is JObject fields)
            response["fields"] = fields;

        if (entity.TryGetValue(EntityModelAttributes.ModifiedGmt, out var modifiedToken))
            response["modified_gmt"] = modifiedToken.ToString();

        return response.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string ExecuteGetEntitySchema(JObject args, EntityModel<UserEntityFieldsModel> user)
    {
        var entityType = args["entity_type"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(entityType))
            return "Error: 'entity_type' is required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!user.Fields.CanUserDo("PEEK_ALL", entityType))
            return $"Error: You do not have access to '{entityType}'.";

        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityType);
        if (!schemaResult.IsSuccessful || schemaResult.Data == null)
            return $"Error: Could not generate schema for '{entityType}'.";

        var schema = schemaResult.Data;
        var lines = new List<string>
        {
            $"Entity: {configBase.EntityConfiguration.EntityReadableNameSingular}",
            $"Description: {configBase.EntityConfiguration.EntityDescription ?? "N/A"}"
        };

        if (schema.Fields != null)
        {
            lines.Add("Fields:");
            foreach (var field in schema.Fields)
                BuildFieldContext(field, "  ", lines);
        }

        return string.Join("\n", lines);
    }

    private static async Task<string> ExecuteGenerateEntity(
        JObject args, EntityModel<UserEntityFieldsModel> user,
        Dictionary<string, (string? title, JObject fields)> lastGeneratedFields,
        CancellationToken ct)
    {
        var entityType = args["entity_type"]?.Value<string>();
        var prompt = args["prompt"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(prompt))
            return "Error: 'entity_type' and 'prompt' are required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!configBase.EntityConfiguration.SupportsAiGeneration)
            return $"Error: Entity type '{entityType}' does not support AI generation.";

        if (!user.Fields.CanUserDo("CREATE", entityType))
            return $"Error: You do not have create permission for '{entityType}'.";

        var (fields, _) = await AiEntityGenerator.GenerateAsync(entityType, prompt, ct);
        if (fields == null)
            return "Error: AI generation failed. Please try again with a different prompt.";

        // Cache the generated fields so propose_create_entity can use them
        // even if the LLM drops or reformats fields when calling propose.
        var title = fields.Value<string>("title");
        var fieldsOnly = fields.DeepClone() as JObject ?? new JObject();
        fieldsOnly.Remove("title");
        lastGeneratedFields[entityType] = (title, fieldsOnly);

        var response = new JObject
        {
            ["entity_type"] = entityType,
            ["status"] = "draft",
            ["fields"] = fields
        };

        return "INSTRUCTION: Now you MUST call propose_create_entity with this data. Do NOT stop to ask the user. " +
               "Pass the fields object EXACTLY as shown below — do NOT reformat dates or change any field values. " +
               "The proposal UI will handle approval.\n" +
               TruncateToolResult(response.ToString(Newtonsoft.Json.Formatting.None));
    }

    private static async Task<string> ExecuteFilterEntities(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var entityType = args["entity_type"]?.Value<string>();
        var query = args["query"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(query))
            return "Error: 'entity_type' and 'query' are required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!configBase.EntityConfiguration.SupportsNaturalLanguageFilter)
            return $"Error: Entity type '{entityType}' does not support natural language filtering.";

        if (!user.Fields.CanUserDo("PEEK_ALL", entityType))
            return $"Error: You do not have access to '{entityType}'.";

        var filterResult = await AiNaturalLanguageFilterHandler.FilterAsync(entityType, query, ct);
        if (filterResult == null)
            return "Error: Filter operation failed.";

        var response = new JObject
        {
            ["entity_type"] = entityType,
            ["combination"] = filterResult.Combination,
            ["interpretation"] = filterResult.NaturalLanguageInterpretation
        };

        var filtersArray = new JArray();
        foreach (var f in filterResult.InterpretedFilters)
        {
            filtersArray.Add(new JObject
            {
                ["field"] = f.Field,
                ["operator"] = f.Operator,
                ["value"] = f.Value
            });
        }
        response["filters"] = filtersArray;

        // Apply per-entity sharing filter first, then cap at 20
        var accessibleResults = new List<JObject>();
        foreach (var item in filterResult.Results)
        {
            if (configBase.EntityConfiguration.HasIndividualSharing)
            {
                var accessLevel = GetEntitySharingAccessLevel(entityType, item, user);
                if (accessLevel == SharingAccessLevel.None)
                    continue;
            }
            accessibleResults.Add(item);
        }

        var capped = accessibleResults.Count > 20 ? accessibleResults.GetRange(0, 20) : accessibleResults;

        var resultsArray = new JArray();
        foreach (var item in capped)
        {
            var entry = new JObject { ["entity_type"] = entityType };

            if (item.TryGetValue(EntityModelAttributes.Id, out var idToken))
                entry["entity_id"] = idToken.Value<int>();

            if (item.TryGetValue(EntityModelAttributes.Title, out var titleToken) &&
                titleToken is JObject titleObj &&
                titleObj.TryGetValue(EntityModelAttributes.TitleRendered, out var rendered))
                entry["title"] = rendered.Value<string>();

            resultsArray.Add(entry);
        }
        response["results"] = resultsArray;
        response["total_count"] = accessibleResults.Count;

        return TruncateToolResult(response.ToString(Newtonsoft.Json.Formatting.None));
    }

    private static async Task<string> ExecuteSummarizeChanges(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var entityType = args["entity_type"]?.Value<string>();
        var entityId = args["entity_id"]?.Value<int>();
        var revisionIndex = args["revision_index"]?.Value<int>();

        if (string.IsNullOrWhiteSpace(entityType) || entityId == null || revisionIndex == null)
            return "Error: 'entity_type', 'entity_id', and 'revision_index' are required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!configBase.EntityConfiguration.SupportsAiDiffSummary)
            return $"Error: Entity type '{entityType}' does not support diff summaries.";

        if (!user.Fields.CanUserDo("READ", entityType))
            return $"Error: You do not have read access to '{entityType}'.";

        // Per-entity sharing check
        if (configBase.EntityConfiguration.HasIndividualSharing)
        {
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                EntityRepositoryService.GetEntityTableName(entityType),
                new DbKey(EntityModelAttributes.Id, entityId.Value), null, ct);

            if (entityResult is { IsSuccessful: true, Data: not null })
            {
                var accessLevel = GetEntitySharingAccessLevel(entityType, entityResult.Data, user);
                if (accessLevel == SharingAccessLevel.None)
                    return $"Error: You do not have access to this {entityType}.";
            }
        }

        var summary = await AiDiffSummaryHandler.SummarizeAsync(entityType, entityId.Value, revisionIndex.Value, ct);
        if (summary == null)
            return $"Error: Could not generate diff summary. Entity ID {entityId} may not exist or revision {revisionIndex} is invalid.";

        return summary;
    }

    private static async Task<string> ExecuteCheckEntityQuality(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var entityType = args["entity_type"]?.Value<string>();
        var entityId = args["entity_id"]?.Value<int>();

        if (string.IsNullOrWhiteSpace(entityType) || entityId == null)
            return "Error: 'entity_type' and 'entity_id' are required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!user.Fields.CanUserDo("UPDATE", entityType))
            return $"Error: You do not have update permission for '{entityType}'.";

        // Discover fields with [AISanityCheck] attributes
        var fieldsModelType = configBase.EntityConfiguration.EntityFieldsModelType;
        var fieldsWithChecks = AiAttributeHelper.FindAllFieldsWithSanityChecks(fieldsModelType);
        if (fieldsWithChecks.Count == 0)
            return $"Entity type '{entityType}' has no AI quality checks configured.";

        // Fetch the entity
        var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
            EntityRepositoryService.GetEntityTableName(entityType),
            new DbKey(EntityModelAttributes.Id, entityId.Value), null, ct);

        if (!entityResult.IsSuccessful || entityResult.Data == null)
            return $"Entity '{entityType}' with ID {entityId} not found.";

        // Per-entity sharing check
        if (configBase.EntityConfiguration.HasIndividualSharing)
        {
            var accessLevel = GetEntitySharingAccessLevel(entityType, entityResult.Data, user);
            if (accessLevel == SharingAccessLevel.None)
                return $"Error: You do not have access to this {entityType}.";
        }

        var entityFields = entityResult.Data[EntityModelAttributes.Fields] as JObject;
        if (entityFields == null)
            return $"Entity '{entityType}' with ID {entityId} has no fields.";

        // Run sanity checks on all applicable fields
        var report = new JArray();
        foreach (var (fieldName, checks) in fieldsWithChecks)
        {
            var fieldValue = entityFields[fieldName];
            if (fieldValue == null || fieldValue.Type == JTokenType.Null)
                continue;

            var results = await AiSanityCheckHandler.CheckFieldAsync(entityType, fieldName, fieldValue, checks, ct);
            foreach (var r in results)
            {
                report.Add(new JObject
                {
                    ["field"] = fieldName,
                    ["check"] = r.Check,
                    ["passed"] = r.Passed,
                    ["severity"] = r.Severity.ToString(),
                    ["message"] = r.Message
                });
            }
        }

        if (report.Count == 0)
            return "All quality checks passed or no checkable fields had values.";

        var allPassed = report.All(r => r["passed"]?.Value<bool>() == true);
        var response = new JObject
        {
            ["entity_type"] = entityType,
            ["entity_id"] = entityId,
            ["all_passed"] = allPassed,
            ["checks"] = report
        };

        return TruncateToolResult(response.ToString(Newtonsoft.Json.Formatting.None));
    }

    private const int MaxToolResultLength = 2000;

    /// <summary>
    /// Normalizes date field values in the payload to match the configured date format.
    /// LLMs may output dates in YYYY-MM-DD or other formats even when the field expects yyyyMMdd.
    /// This runs just before entity creation/update to prevent sanity check failures.
    /// </summary>
    private static void NormalizeDateFields(string entityType, JObject fields)
    {
        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityType);
        if (!schemaResult.IsSuccessful || schemaResult.Data?.Fields == null) return;

        NormalizeDateFieldsRecursive(fields, schemaResult.Data.Fields);
    }

    private static void NormalizeDateFieldsRecursive(JObject target, List<FieldSchema> schema)
    {
        foreach (var field in schema)
        {
            if (field.Type == FieldSchemaType.DatePicker && target[field.Name] is JValue dateVal)
            {
                var targetFmt = field.DateOptions?.Format ?? "yyyy-MM-dd";

                // Handle integer values (LLM may serialize 20251001 as a number)
                if (dateVal.Type is JTokenType.Integer or JTokenType.Float)
                {
                    var numStr = dateVal.Value<long>().ToString();
                    if (DateTime.TryParseExact(numStr, "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var parsedNum))
                    {
                        target[field.Name] = parsedNum.ToString(targetFmt);
                    }
                    else
                    {
                        // Can't parse — convert to string as-is for the sanity check to handle
                        target[field.Name] = numStr;
                    }
                    continue;
                }

                if (dateVal.Type != JTokenType.String) continue;

                var raw = dateVal.Value<string>();
                if (string.IsNullOrEmpty(raw)) continue;
                // Already in correct format?
                if (DateTime.TryParseExact(raw, targetFmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _)) continue;
                // Try common formats and reformat
                string[] tryFormats = ["yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd",
                    "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"];
                if (DateTime.TryParseExact(raw, tryFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                {
                    target[field.Name] = parsed.ToString(targetFmt);
                }
                else if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedGeneral))
                {
                    target[field.Name] = parsedGeneral.ToString(targetFmt);
                }
                // CJK date patterns: 2023年10月1日, 2023年10月01日, etc.
                else if (TryParseCjkDate(raw, out var cjkParsed))
                {
                    target[field.Name] = cjkParsed.ToString(targetFmt);
                }
                // Last resort: try extracting year/month/day digits from any remaining pattern
                else if (TryExtractDateFromText(raw, out var extractedDate))
                {
                    target[field.Name] = extractedDate.ToString(targetFmt);
                }
            }
            else if (field.Type == FieldSchemaType.Group && field.GroupOptions?.ChildSchema != null
                     && target[field.Name] is JObject groupObj)
            {
                NormalizeDateFieldsRecursive(groupObj, field.GroupOptions.ChildSchema);
            }
            else if (field.Type == FieldSchemaType.Repeater && field.RepeaterOptions?.ItemSchema != null
                     && target[field.Name] is JArray arr)
            {
                foreach (var item in arr.OfType<JObject>())
                    NormalizeDateFieldsRecursive(item, field.RepeaterOptions.ItemSchema);
            }
        }
    }

    private static bool TryParseCjkDate(string raw, out DateTime result)
    {
        result = default;
        // Match patterns like: 2023年10月1日, 2023年10月01日, 2023年1月15日
        var cjkMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日?");
        if (cjkMatch.Success)
        {
            var y = int.Parse(cjkMatch.Groups[1].Value);
            var m = int.Parse(cjkMatch.Groups[2].Value);
            var d = int.Parse(cjkMatch.Groups[3].Value);
            try { result = new DateTime(y, m, d); return true; } catch { return false; }
        }
        return false;
    }

    private static bool TryExtractDateFromText(string raw, out DateTime result)
    {
        result = default;
        // Try month-name patterns: "October 1, 2023", "1 October 2023", "Oct 1 2023"
        if (DateTime.TryParse(raw, new CultureInfo("en-US"), DateTimeStyles.None, out result))
            return true;
        // Try extracting yyyy-MM-dd from embedded text (e.g. "start date is 2023-10-01 ok")
        var isoMatch = System.Text.RegularExpressions.Regex.Match(raw, @"(\d{4})[-/](\d{1,2})[-/](\d{1,2})");
        if (isoMatch.Success)
        {
            var y = int.Parse(isoMatch.Groups[1].Value);
            var m = int.Parse(isoMatch.Groups[2].Value);
            var d = int.Parse(isoMatch.Groups[3].Value);
            try { result = new DateTime(y, m, d); return true; } catch { return false; }
        }
        return false;
    }

    /// <summary>
    /// Injects the current user's ID into any mandatory Relation fields pointing to "users"
    /// that the LLM left empty. LLMs skip Relation fields during generation, so nested structures
    /// like comments with mandatory author relations would fail sanity checks without this.
    /// </summary>
    private static void InjectUserRelationFields(string entityType, JObject fields, int userId)
    {
        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityType);
        if (!schemaResult.IsSuccessful || schemaResult.Data?.Fields == null) return;

        InjectUserRelationFieldsRecursive(fields, schemaResult.Data.Fields, userId);
    }

    private static void InjectUserRelationFieldsRecursive(JObject target, List<FieldSchema> schema, int userId)
    {
        foreach (var field in schema)
        {
            if (field.Type == FieldSchemaType.Relation
                && field.RelationOptions?.RelationEntityName == RfReservedEntities.UsersEntityName
                && field.Required)
            {
                // Only inject if the field is missing or has an invalid value
                if (target[field.Name] == null
                    || target[field.Name]!.Type == JTokenType.Null
                    || (target[field.Name]!.Type == JTokenType.Integer && target[field.Name]!.Value<int>() <= 0))
                {
                    target[field.Name] = userId;
                }
            }
            else if (field.Type == FieldSchemaType.Group && field.GroupOptions?.ChildSchema != null
                     && target[field.Name] is JObject groupObj)
            {
                InjectUserRelationFieldsRecursive(groupObj, field.GroupOptions.ChildSchema, userId);
            }
            else if (field.Type == FieldSchemaType.Repeater && field.RepeaterOptions?.ItemSchema != null
                     && target[field.Name] is JArray arr)
            {
                foreach (var item in arr.OfType<JObject>())
                    InjectUserRelationFieldsRecursive(item, field.RepeaterOptions.ItemSchema, userId);
            }
        }
    }

    /// <summary>
    /// Removes fields from the payload that don't exist in the entity schema.
    /// LLMs often invent fields like "description", "status", "author_id", "priority", etc.
    /// These fake fields would be ignored by the DB but can cause confusion and bloat.
    /// </summary>
    private static void RemoveUnknownFields(string entityType, JObject fields)
    {
        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityType);
        if (!schemaResult.IsSuccessful || schemaResult.Data?.Fields == null) return;

        RemoveUnknownFieldsRecursive(fields, schemaResult.Data.Fields);
    }

    private static void RemoveUnknownFieldsRecursive(JObject target, List<FieldSchema> schema)
    {
        var knownNames = new HashSet<string>(schema.Select(f => f.Name));
        var toRemove = target.Properties()
            .Where(p => !knownNames.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
        foreach (var name in toRemove)
            target.Remove(name);

        foreach (var field in schema)
        {
            if (field.Type == FieldSchemaType.Group && field.GroupOptions?.ChildSchema != null
                && target[field.Name] is JObject groupObj)
            {
                RemoveUnknownFieldsRecursive(groupObj, field.GroupOptions.ChildSchema);
            }
            else if (field.Type == FieldSchemaType.Repeater && field.RepeaterOptions?.ItemSchema != null
                     && target[field.Name] is JArray arr)
            {
                foreach (var item in arr.OfType<JObject>())
                    RemoveUnknownFieldsRecursive(item, field.RepeaterOptions.ItemSchema);
            }
        }
    }

    /// <summary>
    /// Normalizes repeater arrays so that every element is a proper JObject
    /// matching the item schema. LLMs often send repeater items as flat strings
    /// (e.g. ["text1", "text2"]) instead of objects. This converts each string
    /// into a JObject with the string placed in the repeater's StickyTitleField
    /// or, failing that, the first text-like field in the item schema.
    /// Recurses into groups and nested repeaters.
    /// </summary>
    private static void NormalizeRepeaterItems(JObject target, List<FieldSchema> schema)
    {
        foreach (var field in schema)
        {
            if (field.Type == FieldSchemaType.Repeater && field.RepeaterOptions?.ItemSchema != null
                && target[field.Name] is JArray arr)
            {
                var primaryField = field.RepeaterOptions.StickyTitleField
                    ?? field.RepeaterOptions.ItemSchema
                        .FirstOrDefault(f => f.Type is FieldSchemaType.TextArea
                            or FieldSchemaType.Text or FieldSchemaType.WysiwygEditor)?.Name;

                var normalized = new JArray();
                foreach (var item in arr)
                {
                    if (item is JObject obj)
                    {
                        NormalizeRepeaterItems(obj, field.RepeaterOptions.ItemSchema);
                        normalized.Add(obj);
                    }
                    else if (item.Type == JTokenType.String && primaryField != null)
                    {
                        var itemObj = new JObject { [primaryField] = item.Value<string>() };
                        normalized.Add(itemObj);
                    }
                    // Non-string, non-object items are dropped
                }

                target[field.Name] = normalized;
            }
            else if (field.Type == FieldSchemaType.Group && field.GroupOptions?.ChildSchema != null
                     && target[field.Name] is JObject groupObj)
            {
                NormalizeRepeaterItems(groupObj, field.GroupOptions.ChildSchema);
            }
        }
    }

    private static void NormalizeRepeaterItems(string entityType, JObject fields)
    {
        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityType);
        if (!schemaResult.IsSuccessful || schemaResult.Data?.Fields == null) return;
        NormalizeRepeaterItems(fields, schemaResult.Data.Fields);
    }

    private static string TruncateToolResult(string result)
    {
        if (result.Length <= MaxToolResultLength)
            return result;
        return result[..MaxToolResultLength] + "\n...[truncated]";
    }

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

        lines.Add(desc);

        if (field.GroupOptions?.ChildSchema != null)
        {
            foreach (var child in field.GroupOptions.ChildSchema)
                BuildFieldContext(child, indent + "  ", lines);
        }

        if (field.RepeaterOptions?.ItemSchema != null)
        {
            foreach (var child in field.RepeaterOptions.ItemSchema)
                BuildFieldContext(child, indent + "  ", lines);
        }
    }

    // --- Propose tools (create actions for user approval) ---

    private static async Task<string> ExecuteProposeCreate(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> actions,
        Dictionary<string, (string? title, JObject fields)> lastGeneratedFields,
        CancellationToken ct)
    {
        var entityType = args.Value<string>("entity_type");
        if (string.IsNullOrEmpty(entityType))
            return "Error: entity_type is required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var config))
            return $"Error: Unknown entity type '{entityType}'.";
        if (!user.Fields.CanUserDo("CREATE", entityType))
            return "Error: You do not have permission to create this entity type.";

        var title = args.Value<string>("title") ?? "";
        var fields = args["fields"] as JObject ?? new JObject();

        // If generate_entity was never called but the entity has complex fields (repeaters/groups)
        // and supports AI generation, auto-generate now. LLMs sometimes skip generate_entity
        // and call propose_create_entity directly, producing badly structured nested data
        // (e.g. dumping all survey questions into a single section_description blob).
        if (!lastGeneratedFields.ContainsKey(entityType) && config.EntityConfiguration.SupportsAiGeneration)
        {
            var schema = EntitySchemaGenerator.GenerateSchema(entityType);
            var hasComplexFields = schema.IsSuccessful && schema.Data?.Fields != null
                && schema.Data.Fields.Any(f => f.Type is FieldSchemaType.Repeater or FieldSchemaType.Group);

            if (hasComplexFields)
            {
                var prompt = !string.IsNullOrWhiteSpace(title) ? title : fields.ToString(Newtonsoft.Json.Formatting.None);
                var (generated, _) = await AiEntityGenerator.GenerateAsync(entityType, prompt, ct);
                if (generated != null)
                {
                    var genTitle = generated.Value<string>("title");
                    var genFields = generated.DeepClone() as JObject ?? new JObject();
                    genFields.Remove("title");
                    lastGeneratedFields[entityType] = (genTitle, genFields);
                }
            }
        }

        // Merge cached generate_entity fields as the base layer.
        // The LLM often drops or reformats fields when calling propose_create_entity;
        // using the cached fields ensures the complete, correctly-formatted data is preserved.
        if (lastGeneratedFields.TryGetValue(entityType, out var cached))
        {
            var merged = cached.fields.DeepClone() as JObject ?? new JObject();

            // LLMs frequently mangle complex fields (repeaters, groups, rich text)
            // when relaying generate_entity output to propose_create_entity — e.g.
            // dumping the same summary blob into every repeater item. The cached
            // version from generate_entity is always higher quality for these types.
            // Strip complex fields from the LLM's input before merging so the
            // cached version is preserved; only allow scalar overrides through.
            var propsToStrip = fields.Properties()
                .Where(p => merged[p.Name] != null
                    && p.Value.Type is JTokenType.Array or JTokenType.Object)
                .Select(p => p.Name)
                .ToList();
            foreach (var name in propsToStrip)
                fields.Remove(name);

            merged.Merge(fields, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace,
                MergeNullValueHandling = MergeNullValueHandling.Ignore
            });
            fields = merged;

            // Use cached title if the LLM provided a different/empty one
            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(cached.title))
                title = cached.title;

            // Clear the cache for this entity type so it's not reused across unrelated proposals
            lastGeneratedFields.Remove(entityType);
        }

        // Deduplicate: if the same create was already proposed, update its payload with richer data
        var description = $"Create new {entityType}: \"{title}\"";
        var existing = actions.FirstOrDefault(a => a.ActionType == "create_entity" && a.Description == description);
        if (existing != null)
        {
            // Update the existing proposal's payload if the new one has more content
            var newPayload = new JObject { ["title"] = title, ["fields"] = fields };
            var existingFieldCount = (existing.Payload?["fields"] as JObject)?.Properties().Count(p => !string.IsNullOrEmpty(p.Value?.ToString())) ?? 0;
            var newFieldCount = fields.Properties().Count(p => !string.IsNullOrEmpty(p.Value?.ToString()));
            if (newFieldCount > existingFieldCount)
            {
                existing.Payload = newPayload;
            }
            return $"Updated proposal '{existing.ActionId}' with richer content for {entityType} \"{title}\".";
        }

        var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
        actions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "create_entity",
            EntityType = entityType,
            Payload = new JObject
            {
                ["title"] = title,
                ["fields"] = fields
            },
            Description = description,
            RequiresApproval = true
        });

        return $"Proposed action '{actionId}': Create new {entityType} with title \"{title}\". The user must approve this before it is saved.";
    }

    private static async Task<string> ExecuteProposeUpdate(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> actions, CancellationToken ct)
    {
        var entityType = args.Value<string>("entity_type");
        if (string.IsNullOrEmpty(entityType))
            return "Error: entity_type is required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var config))
            return $"Error: Unknown entity type '{entityType}'.";
        if (!user.Fields.CanUserDo("UPDATE", entityType))
            return "Error: You do not have permission to update this entity type.";

        var entityId = args.Value<int?>("entity_id");
        if (!entityId.HasValue)
            return "Error: entity_id is required.";

        // Per-entity sharing check
        if (config.EntityConfiguration.HasIndividualSharing)
        {
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                EntityRepositoryService.GetEntityTableName(entityType),
                new DbKey(EntityModelAttributes.Id, entityId.Value), null, ct);
            if (!entityResult.IsSuccessful || entityResult.Data == null)
                return $"Error: Entity {entityType} #{entityId} not found.";
            var accessLevel = GetEntitySharingAccessLevel(entityType, entityResult.Data, user);
            if (accessLevel is SharingAccessLevel.None or SharingAccessLevel.View)
                return "Error: You do not have edit access to this entity.";
        }

        var fields = args["fields"] as JObject ?? new JObject();

        var fieldNames = string.Join(", ", fields.Properties().Select(p => p.Name));
        var description = $"Update {entityType} #{entityId}: set {fieldNames}";
        if (actions.Any(a => a.ActionType == "update_entity" && a.EntityId == entityId && a.EntityType == entityType))
            return $"Already proposed updating {entityType} #{entityId} in this conversation turn.";

        var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
        actions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "update_entity",
            EntityType = entityType,
            EntityId = entityId,
            Payload = new JObject { ["fields"] = fields },
            Description = description,
            RequiresApproval = true
        });

        return $"Proposed action '{actionId}': Update {entityType} #{entityId} fields [{fieldNames}]. The user must approve this before it is applied.";
    }

    private static async Task<string> ExecuteProposeDelete(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> actions, CancellationToken ct)
    {
        var entityType = args.Value<string>("entity_type");
        if (string.IsNullOrEmpty(entityType))
            return "Error: entity_type is required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var config))
            return $"Error: Unknown entity type '{entityType}'.";
        if (!user.Fields.CanUserDo("DELETE", entityType))
            return "Error: You do not have permission to delete this entity type.";

        var entityId = args.Value<int?>("entity_id");
        if (!entityId.HasValue)
            return "Error: entity_id is required.";

        // Per-entity sharing check — require Owner access to propose delete
        if (config.EntityConfiguration.HasIndividualSharing)
        {
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                EntityRepositoryService.GetEntityTableName(entityType),
                new DbKey(EntityModelAttributes.Id, entityId.Value), null, ct);
            if (!entityResult.IsSuccessful || entityResult.Data == null)
                return $"Error: Entity {entityType} #{entityId} not found.";
            var accessLevel = GetEntitySharingAccessLevel(entityType, entityResult.Data, user);
            if (accessLevel != SharingAccessLevel.Owner)
                return "Error: You do not have owner access to delete this entity.";
        }

        if (actions.Any(a => a.ActionType == "delete_entity" && a.EntityId == entityId && a.EntityType == entityType))
            return $"Already proposed deleting {entityType} #{entityId} in this conversation turn.";

        var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
        actions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "delete_entity",
            EntityType = entityType,
            EntityId = entityId,
            Description = $"Delete {entityType} #{entityId}",
            RequiresApproval = true
        });

        return $"Proposed action '{actionId}': Delete {entityType} #{entityId}. The user must approve this before it is executed.";
    }

    private static async Task<string> ExecuteSuggestFieldValue(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct,
        List<ProposedAction> actions)
    {
        var entityType = args.Value<string>("entity_type");
        if (string.IsNullOrEmpty(entityType))
            return "Error: entity_type is required.";

        if (!RfConfiguration.EntityNameToConfiguration.ContainsKey(entityType))
            return $"Error: Unknown entity type '{entityType}'.";
        if (!user.Fields.CanUserDo("UPDATE", entityType))
            return $"Error: You do not have update permission for '{entityType}'.";

        var targetField = args.Value<string>("target_field");
        if (string.IsNullOrEmpty(targetField))
            return "Error: target_field is required.";

        var currentFields = args["current_fields"] as JObject ?? new JObject();

        var suggestion = await AiFieldSuggestionHandler
            .SuggestAsync(entityType, targetField, currentFields, ct);

        if (suggestion == null)
            return $"Could not generate a suggestion for field '{targetField}'. The field may not have AI suggestion configured.";

        var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
        actions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "set_field",
            EntityType = entityType,
            Payload = new JObject
            {
                ["field_name"] = targetField,
                ["suggested_value"] = suggestion
            },
            Description = $"Set {targetField} to AI-suggested value",
            RequiresApproval = true
        });

        return $"Suggested value for '{targetField}': \"{suggestion}\". Proposed as action '{actionId}' — the user can apply or reject this.";
    }

    // --- Set form fields tool (bulk set fields without AI suggestion) ---

    private static string ExecuteSetFormFields(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> actions)
    {
        var entityType = args.Value<string>("entity_type") ?? "";
        if (!RfConfiguration.EntityNameToConfiguration.ContainsKey(entityType))
            return $"Error: Unknown entity type '{entityType}'.";

        if (!user.Fields.CanUserDo("CREATE", entityType) && !user.Fields.CanUserDo("UPDATE", entityType))
            return $"Error: You do not have permission to set fields on '{entityType}'.";

        var fields = args["fields"] as JObject;
        if (fields == null || !fields.HasValues)
            return "Error: fields object is required and must not be empty.";

        var setCount = 0;

        // Handle title if provided
        var title = args.Value<string>("title");
        if (!string.IsNullOrEmpty(title))
        {
            var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
            actions.Add(new ProposedAction
            {
                ActionId = actionId,
                ActionType = "set_field",
                EntityType = entityType,
                Payload = new JObject
                {
                    ["field_name"] = "__title__",
                    ["suggested_value"] = title
                },
                Description = "Set title",
                RequiresApproval = false
            });
            setCount++;
        }

        foreach (var prop in fields.Properties())
        {
            var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
            actions.Add(new ProposedAction
            {
                ActionId = actionId,
                ActionType = "set_field",
                EntityType = entityType,
                Payload = new JObject
                {
                    ["field_name"] = prop.Name,
                    ["suggested_value"] = prop.Value.DeepClone()
                },
                Description = $"Set {prop.Name}",
                RequiresApproval = false
            });
            setCount++;
        }

        return $"Proposed setting {setCount} field(s). The values will be applied to the form.";
    }

    // --- Navigate tool (proposes a UI navigation) ---

    private static string ExecuteNavigate(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> actions)
    {
        var page = args.Value<string>("page");
        if (string.IsNullOrEmpty(page))
            return "Error: 'page' is required.";

        // Dashboard navigation doesn't need an entity type
        if (page == "dashboard")
        {
            var navActionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
            actions.Add(new ProposedAction
            {
                ActionId = navActionId,
                ActionType = "navigate",
                Payload = new JObject { ["page"] = "dashboard" },
                Description = "Navigate to dashboard",
                RequiresApproval = false
            });
            return $"Navigation action '{navActionId}' queued: Navigate to dashboard.";
        }

        var entityType = args.Value<string>("entity_type");
        if (string.IsNullOrEmpty(entityType))
            return "Error: 'entity_type' is required for entity pages.";

        if (!RfConfiguration.EntityNameToConfiguration.ContainsKey(entityType))
            return $"Error: Unknown entity type '{entityType}'.";
        if (!user.Fields.CanUserDo("PEEK_ALL", entityType))
            return $"Error: You do not have access to '{entityType}'.";

        var entityId = args.Value<int?>("entity_id");

        // Validate required parameters for specific pages
        if ((page == "entity-edit" || page == "revision-diff") && !entityId.HasValue)
            return $"Error: 'entity_id' is required for page '{page}'.";

        // Don't navigate to create page when there's already a create proposal — the AI creates via backend, not the form
        if (page == "entity-create" && actions.Any(a => a.ActionType == "create_entity" && a.EntityType == entityType))
            return $"Skipped navigation to create page — a create proposal for {entityType} is already pending approval.";

        var actionId = $"action-{Interlocked.Increment(ref _actionIdCounter)}";
        var description = page switch
        {
            "entity-list" => $"Navigate to {entityType} list",
            "entity-edit" => $"Navigate to edit {entityType} #{entityId}",
            "entity-create" => $"Navigate to create new {entityType}",
            "revision-diff" => $"Navigate to revisions of {entityType} #{entityId}",
            _ => $"Navigate to {page} for {entityType}"
        };

        actions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "navigate",
            EntityType = entityType,
            EntityId = entityId,
            Payload = new JObject { ["page"] = page },
            Description = description,
            RequiresApproval = false
        });

        return $"Navigation action '{actionId}' queued: {description}.";
    }

    // --- List entities tool ---

    private static async Task<string> ExecuteListEntities(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var entityType = args.Value<string>("entity_type");
        if (string.IsNullOrEmpty(entityType))
            return "Error: 'entity_type' is required.";

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var configBase))
            return $"Error: Entity type '{entityType}' not found.";

        if (!user.Fields.CanUserDo("PEEK_ALL", entityType))
            return $"Error: You do not have access to '{entityType}'.";

        var limit = Math.Clamp(args.Value<int?>( "limit") ?? 20, 1, 50);

        var scanResult = await AiConfiguration.DatabaseService.ScanTableAsync(
            EntityRepositoryService.GetEntityTableName(entityType), ct);

        if (!scanResult.IsSuccessful)
            return "Error: Could not retrieve entities.";

        var items = scanResult.Data.Items;
        var results = new JArray();
        var accessibleCount = 0;

        foreach (var item in items)
        {
            // Per-entity sharing check
            if (configBase.EntityConfiguration.HasIndividualSharing)
            {
                var accessLevel = GetEntitySharingAccessLevel(entityType, item, user);
                if (accessLevel == SharingAccessLevel.None)
                    continue;
            }

            accessibleCount++;

            if (results.Count >= limit)
                continue; // keep counting accessible items for total_count

            var entry = new JObject { ["entity_type"] = entityType };

            if (item.TryGetValue(EntityModelAttributes.Id, out var idToken))
                entry["entity_id"] = idToken.Value<int>();

            if (item.TryGetValue(EntityModelAttributes.Title, out var titleToken) &&
                titleToken is JObject titleObj &&
                titleObj.TryGetValue(EntityModelAttributes.TitleRendered, out var rendered))
                entry["title"] = rendered.Value<string>();

            if (item.TryGetValue(EntityModelAttributes.ModifiedGmt, out var modifiedToken))
                entry["modified_gmt"] = modifiedToken.ToString();

            results.Add(entry);
        }

        var response = new JObject
        {
            ["entity_type"] = entityType,
            ["total_count"] = accessibleCount,
            ["returned_count"] = results.Count,
            ["entities"] = results
        };

        return TruncateToolResult(response.ToString(Newtonsoft.Json.Formatting.None));
    }

    // --- Confirmation processing (execute approved actions) ---

    private static Task<string> ProcessConfirmedActionsAsync(
        List<ActionConfirmation> confirmations, List<ActionExecutionResult>? executionResults,
        EntityModel<UserEntityFieldsModel> user,
        CancellationToken ct)
    {
        var executionMap = new Dictionary<string, ActionExecutionResult>();
        if (executionResults != null)
        {
            foreach (var r in executionResults)
                executionMap[r.ActionId] = r;
        }

        var results = new List<string>();

        foreach (var confirmation in confirmations)
        {
            if (!confirmation.Approved)
            {
                results.Add($"Action '{confirmation.ActionId}' was rejected by the user. Do NOT re-propose it.");
                continue;
            }

            if (executionMap.TryGetValue(confirmation.ActionId, out var execResult))
            {
                if (execResult.Success)
                    results.Add($"Action '{confirmation.ActionId}' was approved and SUCCESSFULLY EXECUTED: {execResult.Message}. Do NOT propose this action again — it is already saved.");
                else
                    results.Add($"Action '{confirmation.ActionId}' was approved but EXECUTION FAILED: {execResult.Message}. Inform the user about the error. Do NOT re-propose or regenerate the entity — just explain the error clearly.");
            }
            else
            {
                results.Add($"Action '{confirmation.ActionId}' was approved and executed.");
            }
        }

        return Task.FromResult(string.Join(" ", results));
    }

    internal static async Task<OperationResult<JObject>> ExecuteApprovedCreateAsync(
        string entityType, JObject payload, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var config))
            return OperationResult<JObject>.Failure($"Unknown entity type '{entityType}'.", HttpStatusCode.BadRequest);
        if (!user.Fields.CanUserDo("CREATE", entityType))
            return OperationResult<JObject>.Failure("Permission denied.", HttpStatusCode.Forbidden);

        var body = new JObject();
        var title = payload.Value<string>("title");
        if (!string.IsNullOrEmpty(title))
            body[EntityModelAttributes.Title] = new JObject { [EntityModelAttributes.TitleRendered] = title };
        if (payload["fields"] is JObject fields)
        {
            RemoveUnknownFields(entityType, fields);
            NormalizeRepeaterItems(entityType, fields);
            NormalizeDateFields(entityType, fields);
            InjectUserRelationFields(entityType, fields, user.Id);
            body["fields"] = fields;
        }

        if (config.EntityConfiguration.HasAuthor)
            body[EntityModelAttributes.Author] = user.Id;

        try
        {
            var t = (Task<OperationResult<JObject>>)config.CrudMethodInfo.PutOneAsyncMethodInfo
                .Invoke(RfConfiguration.RepositoryService, [entityType, body, ct])!;
            return await t;
        }
        catch (Exception ex)
        {
            var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            RfConfiguration.LogError($"ExecuteApprovedCreateAsync [{entityType}]: {inner.Message}");
            return OperationResult<JObject>.Failure($"Create failed: {inner.Message}", HttpStatusCode.InternalServerError);
        }
    }

    internal static async Task<OperationResult<JObject>> ExecuteApprovedUpdateAsync(
        string entityType, int entityId, JObject payload, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var config))
            return OperationResult<JObject>.Failure($"Unknown entity type '{entityType}'.", HttpStatusCode.BadRequest);
        if (!user.Fields.CanUserDo("UPDATE", entityType))
            return OperationResult<JObject>.Failure("Permission denied.", HttpStatusCode.Forbidden);

        // Per-entity sharing check — require at least Edit access
        if (config.EntityConfiguration.HasIndividualSharing)
        {
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                EntityRepositoryService.GetEntityTableName(entityType),
                new DbKey(EntityModelAttributes.Id, entityId), null, ct);
            if (!entityResult.IsSuccessful || entityResult.Data == null)
                return OperationResult<JObject>.Failure($"Entity not found.", HttpStatusCode.NotFound);
            var accessLevel = GetEntitySharingAccessLevel(entityType, entityResult.Data, user);
            if (accessLevel is SharingAccessLevel.None or SharingAccessLevel.View)
                return OperationResult<JObject>.Failure("Permission denied.", HttpStatusCode.Forbidden);
        }

        var body = new JObject { ["id"] = entityId };
        if (payload["fields"] is JObject fields)
        {
            RemoveUnknownFields(entityType, fields);
            NormalizeRepeaterItems(entityType, fields);
            NormalizeDateFields(entityType, fields);
            InjectUserRelationFields(entityType, fields, user.Id);
            body["fields"] = fields;
        }

        try
        {
            var uid = EntityUpdaterIdentity.NormalUpdate(user.Id, user.Fields.EmailAddress);
            var t = (Task<OperationResult<JObject>>)config.CrudMethodInfo.UpdateOneAsyncMethodInfo
                .Invoke(RfConfiguration.RepositoryService, [entityType, entityId, body, uid, ct])!;
            return await t;
        }
        catch (Exception ex)
        {
            var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            RfConfiguration.LogError($"ExecuteApprovedUpdateAsync [{entityType}#{entityId}]: {inner.Message}");
            return OperationResult<JObject>.Failure($"Update failed: {inner.Message}", HttpStatusCode.InternalServerError);
        }
    }

    internal static async Task<OperationResult<JObject>> ExecuteApprovedDeleteAsync(
        string entityType, int entityId, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityType, out var config))
            return OperationResult<JObject>.Failure($"Unknown entity type '{entityType}'.", HttpStatusCode.BadRequest);
        if (!user.Fields.CanUserDo("DELETE", entityType))
            return OperationResult<JObject>.Failure("Permission denied.", HttpStatusCode.Forbidden);

        // Per-entity sharing check — require Owner access to delete
        if (config.EntityConfiguration.HasIndividualSharing)
        {
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                EntityRepositoryService.GetEntityTableName(entityType),
                new DbKey(EntityModelAttributes.Id, entityId), null, ct);
            if (!entityResult.IsSuccessful || entityResult.Data == null)
                return OperationResult<JObject>.Failure($"Entity not found.", HttpStatusCode.NotFound);
            var accessLevel = GetEntitySharingAccessLevel(entityType, entityResult.Data, user);
            if (accessLevel != SharingAccessLevel.Owner)
                return OperationResult<JObject>.Failure("Permission denied.", HttpStatusCode.Forbidden);
        }

        try
        {
            var t = (Task<OperationResult<JObject>>)config.CrudMethodInfo.DeleteOneAsyncMethodInfo
                .Invoke(RfConfiguration.RepositoryService, [entityType, entityId, user.Id, ct])!;
            return await t;
        }
        catch (Exception ex)
        {
            var inner = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            RfConfiguration.LogError($"ExecuteApprovedDeleteAsync [{entityType}#{entityId}]: {inner.Message}");
            return OperationResult<JObject>.Failure($"Delete failed: {inner.Message}", HttpStatusCode.InternalServerError);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Sheet Tools — Implementation
    // ══════════════════════════════════════════════════════════════════════════

    private const string SheetSystemPromptExtension =
        "\n\nSHEET CONTEXT:\n" +
        "The user is currently on an RF Sheet page. RF Sheets are spreadsheets that reference entity data via custom formulas.\n" +
        "You have access to sheet-specific tools: list_sheets, get_sheet_summary, get_sheet_cell_value, suggest_formula, propose_sheet_formulas, propose_add_sheet_source.\n\n" +
        "RF FORMULA REFERENCE (all formulas start with =, ALL string arguments MUST use double quotes — NEVER single quotes):\n" +
        "- RF.FIELD(\"entity\", id, \"fieldName\") — Get a single field value. Supports dot-path for nested fields (e.g. \"venue.address.city\").\n" +
        "- RF.TITLE(\"entity\", id) — Get the title of an entity by ID.\n" +
        "- RF.COUNT(\"entity\") — Count total rows for an entity type.\n" +
        "- RF.SUM(\"entity\", \"field\") — Sum a numeric field across all rows.\n" +
        "- RF.AVG(\"entity\", \"field\") — Average a numeric field across all rows.\n" +
        "- RF.IDS(\"entity\") — Spill array of all entity IDs (fills cells downward).\n" +
        "- RF.LIST(\"entity\", \"field\") — Spill array of a field's values across all rows.\n" +
        "- RF.FILTER(\"entity\", \"field\", \"operator\", \"value\") — Spill array of IDs where condition is met. Operators: =, !=, >, <, >=, <=, contains.\n" +
        "- RF.MATCHLIST(\"entity\", \"matchField\", \"operator\", \"value\", \"returnField\") — Like FILTER but returns a different field's values.\n" +
        "- RF.LOOKUP(\"entity\", \"matchField\", \"matchValue\", \"returnField\") — Find first row where matchField equals matchValue, return returnField.\n" +
        "- RF.MATCH(\"entity\", \"matchField\", \"matchValue\") — Find first row where matchField equals matchValue, return its ID.\n" +
        "- RF.REPEAT(\"entity\", id, \"repeaterPath\", \"subField\") — Spill array of sub-field values from a repeater field.\n" +
        "- RF.REPEATCOUNT(\"entity\", id, \"repeaterPath\") — Count items in a repeater field.\n" +
        "- RF.REPEATFIELD(\"entity\", id, \"repeaterPath\", index, \"subField\") — Get a specific repeater item's sub-field by index.\n" +
        "IMPORTANT: String arguments in RF formulas MUST always be wrapped in double quotes (\\\"). Single quotes (') will cause errors. Example: =RF.IDS(\"objective\") is correct, =RF.IDS('objective') is WRONG.\n\n" +
        "SPILL BEHAVIOR: Functions returning arrays (RF.IDS, RF.LIST, RF.FILTER, RF.MATCHLIST, RF.REPEAT) fill cells downward from their anchor cell. Do NOT place other content below a spill formula.\n\n" +
        "SHEET TOOL RULES:\n" +
        "- Use list_sheets to discover available sheets.\n" +
        "- Use get_sheet_summary to understand a sheet's structure and formulas.\n" +
        "- Use get_sheet_cell_value to read current cell values.\n" +
        "- Use suggest_formula to help the user construct a formula (returns text only, no modification).\n" +
        "- Use propose_sheet_formulas to propose writing cells (headers, formulas, labels). Requires user approval.\n" +
        "- Use propose_add_sheet_source to propose adding a new entity data source to the sheet. Requires user approval.\n" +
        "- When building a table: place headers in row 0, then use spill formulas (RF.LIST) in row 1. Leave space below for spill expansion.\n" +
        "- Cell operations use 0-based row/col indices.\n\n" +
        "ADVANCED USAGE:\n" +
        "- When the user says 'on the cell I clicked' or 'right here', use the Selected cell from their context (format R{row}C{col}, 0-based).\n" +
        "- For 'calculate total of [entity]'s [field]': use RF.SUM(entity, field) placed at the selected cell. Add a label in an adjacent cell if appropriate.\n" +
        "- For overview/dashboard requests: build a structured layout with propose_sheet_formulas — headers in one row, spill formulas (RF.IDS, RF.LIST) in the row below. Use multiple columns for different fields.\n" +
        "- For date-filtered views (e.g. '2020 to 2024'): use RF.FILTER or RF.MATCHLIST with date field conditions, or combine RF.LIST with labels explaining the filter.\n" +
        "- If the sheet doesn't have the needed entity as a data source yet, first propose_add_sheet_source, then propose the formulas.\n" +
        "- When placing aggregations (SUM, AVG, COUNT), put a descriptive label (e.g. 'Total Revenue') in the cell to the left or above the formula cell.\n" +
        "- For multi-entity dashboards, organize sections vertically with blank rows between them. Each section: title row → header row → spill data row.\n" +
        "- You can use the currently connected Sheet data sources from the context to know what entities are available without calling list_sheets.";

    private static async Task<string> ExecuteListSheets(
        EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        const string entityName = RfReservedEntities.SheetsEntityName;

        if (!user.Fields.CanUserDo("PEEK_ALL", entityName))
            return "Error: You do not have access to sheets.";

        var scanResult = await AiConfiguration.DatabaseService.ScanTableAsync(
            EntityRepositoryService.GetEntityTableName(entityName), ct);

        if (!scanResult.IsSuccessful)
            return "Error: Could not retrieve sheets.";

        var results = new JArray();
        foreach (var item in scanResult.Data.Items)
        {
            // Per-entity sharing check
            var accessLevel = GetEntitySharingAccessLevel(entityName, item, user);
            if (accessLevel == SharingAccessLevel.None)
                continue;

            var entry = new JObject();

            if (item.TryGetValue(EntityModelAttributes.Id, out var idToken))
                entry["id"] = idToken.Value<int>();

            if (item.TryGetValue(EntityModelAttributes.Title, out var titleToken) &&
                titleToken is JObject titleObj &&
                titleObj.TryGetValue(EntityModelAttributes.TitleRendered, out var rendered))
                entry["title"] = rendered.Value<string>();

            if (item[EntityModelAttributes.Fields] is JObject fields)
            {
                var sources = fields.Value<string>("sources") ?? "[]";
                try
                {
                    var sourcesArray = JArray.Parse(sources);
                    entry["sources"] = new JArray(sourcesArray.Select(s => s.Value<string>("entity")).Where(e => e != null));
                }
                catch { entry["sources"] = new JArray(); }
            }

            if (item.TryGetValue(EntityModelAttributes.Author, out var authorToken))
                entry["author_id"] = authorToken;

            results.Add(entry);
        }

        if (results.Count == 0)
            return "No sheets found.";

        return new JObject { ["sheets"] = results, ["total_count"] = results.Count }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static async Task<string> ExecuteGetSheetSummary(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var sheetId = args.Value<int?>("sheet_id");
        if (sheetId == null)
            return "Error: sheet_id is required.";

        var (sheet, accessLevel, error) = await GetSheetWithAccessCheck(sheetId.Value, user, SharingAccessLevel.View, ct);
        if (error != null) return error;

        var fields = sheet![EntityModelAttributes.Fields] as JObject;
        var title = "";
        if (sheet.TryGetValue(EntityModelAttributes.Title, out var titleToken) &&
            titleToken is JObject titleObj &&
            titleObj.TryGetValue(EntityModelAttributes.TitleRendered, out var renderedTitle))
            title = renderedTitle.Value<string>() ?? "";

        var sources = fields?.Value<string>("sources") ?? "[]";
        var workbookData = fields?.Value<string>("workbook_data") ?? "{}";
        var refreshInterval = fields?.Value<int?>("refresh_interval_seconds") ?? 30;

        // Parse sources
        JArray sourcesList;
        try { sourcesList = JArray.Parse(sources); }
        catch { sourcesList = new JArray(); }

        // Parse workbook and extract formula inventory
        var formulaCounts = new Dictionary<string, int>();
        var cellCount = 0;
        try
        {
            var workbook = JObject.Parse(workbookData);
            if (workbook["sheets"] is JObject sheets)
            {
                foreach (var sheetEntry in sheets.Properties())
                {
                    if (sheetEntry.Value is JObject sheetData && sheetData["cellData"] is JObject cellData)
                    {
                        foreach (var row in cellData.Properties())
                        {
                            if (row.Value is JObject rowCells)
                            {
                                foreach (var cell in rowCells.Properties())
                                {
                                    cellCount++;
                                    if (cell.Value is JObject cellObj && cellObj.TryGetValue("f", out var fToken))
                                    {
                                        var formula = fToken.Value<string>() ?? "";
                                        // Extract RF function names
                                        foreach (System.Text.RegularExpressions.Match m in
                                            System.Text.RegularExpressions.Regex.Matches(formula, @"RF\.(\w+)\("))
                                        {
                                            var funcName = $"RF.{m.Groups[1].Value}";
                                            formulaCounts[funcName] = formulaCounts.GetValueOrDefault(funcName) + 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Corrupt workbook — return partial summary
        }

        var summary = new JObject
        {
            ["title"] = title,
            ["sheet_id"] = sheetId.Value,
            ["sources"] = sourcesList,
            ["cell_count"] = cellCount,
            ["refresh_interval_seconds"] = refreshInterval,
            ["access_level"] = accessLevel.ToString().ToLowerInvariant()
        };

        if (formulaCounts.Count > 0)
        {
            var formulaObj = new JObject();
            foreach (var (name, count) in formulaCounts.OrderByDescending(kv => kv.Value))
                formulaObj[name] = count;
            summary["formula_inventory"] = formulaObj;
        }

        return summary.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static async Task<string> ExecuteGetSheetCellValue(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var sheetId = args.Value<int?>("sheet_id");
        if (sheetId == null)
            return "Error: sheet_id is required.";

        var range = args.Value<string>("range");
        if (string.IsNullOrWhiteSpace(range))
            return "Error: range is required (e.g. 'A1' or 'A1:C5').";

        var (sheet, _, error) = await GetSheetWithAccessCheck(sheetId.Value, user, SharingAccessLevel.View, ct);
        if (error != null) return error;

        // Parse range
        if (!TryParseCellRange(range, out var startRow, out var startCol, out var endRow, out var endCol))
            return $"Error: Invalid range format '{range}'. Use formats like 'A1', 'B3', or 'A1:C5'.";

        var fields = sheet![EntityModelAttributes.Fields] as JObject;
        var workbookData = fields?.Value<string>("workbook_data") ?? "{}";

        JObject? cellData = null;
        try
        {
            var workbook = JObject.Parse(workbookData);
            if (workbook["sheets"] is JObject sheets)
            {
                // Get first sheet's cellData
                var firstSheet = sheets.Properties().FirstOrDefault();
                if (firstSheet?.Value is JObject sheetData)
                    cellData = sheetData["cellData"] as JObject;
            }
        }
        catch { return "Error: Could not parse workbook data."; }

        var results = new JArray();
        for (var row = startRow; row <= endRow; row++)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                var cellRef = $"{GetColumnLetter(col)}{row + 1}";
                var cellEntry = new JObject { ["cell"] = cellRef };

                var cellObj = cellData?[row.ToString()]?[col.ToString()] as JObject;
                if (cellObj != null)
                {
                    if (cellObj.TryGetValue("v", out var vToken))
                        cellEntry["value"] = vToken;
                    if (cellObj.TryGetValue("f", out var fToken))
                        cellEntry["formula"] = fToken.Value<string>();
                }
                else
                {
                    cellEntry["value"] = null;
                }

                results.Add(cellEntry);
            }
        }

        return new JObject { ["cells"] = results }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static async Task<string> ExecuteSuggestFormula(
        JObject args, EntityModel<UserEntityFieldsModel> user, CancellationToken ct)
    {
        var description = args.Value<string>("description");
        if (string.IsNullOrWhiteSpace(description))
            return "Error: description is required.";

        var entityType = args.Value<string>("entity_type");
        var fieldName = args.Value<string>("field_name");

        // Validate entity_type if provided
        if (!string.IsNullOrEmpty(entityType) && !RfConfiguration.EntityNameToConfiguration.ContainsKey(entityType))
            return $"Error: Entity type '{entityType}' not found.";

        if (!string.IsNullOrEmpty(entityType) && !user.Fields.CanUserDo("PEEK_ALL", entityType))
            return $"Error: You do not have access to entity type '{entityType}'.";

        // Validate field_name if provided along with entity_type
        if (!string.IsNullOrEmpty(entityType) && !string.IsNullOrEmpty(fieldName))
        {
            var schema = EntitySchemaGenerator.GenerateSchema(entityType);
            if (schema.IsSuccessful && schema.Data?.Fields != null && !schema.Data.Fields.Any(f => f.Name == fieldName))
                return $"Error: Field '{fieldName}' does not exist on entity type '{entityType}'.";
        }

        // Use Light LLM to generate formula suggestion
        var formulaPrompt =
            "You are a formula assistant for RF Sheets. Given a description, return ONLY the formula string (starting with =). " +
            "Do NOT include any explanation.\n\n" +
            "Available RF formulas:\n" +
            "- =RF.FIELD(\"entity\",id,\"field\") — single value\n" +
            "- =RF.TITLE(\"entity\",id) — entity title\n" +
            "- =RF.COUNT(\"entity\") — row count\n" +
            "- =RF.SUM(\"entity\",\"field\") — sum numeric field\n" +
            "- =RF.AVG(\"entity\",\"field\") — average numeric field\n" +
            "- =RF.IDS(\"entity\") — all IDs (spill)\n" +
            "- =RF.LIST(\"entity\",\"field\") — all values of a field (spill)\n" +
            "- =RF.FILTER(\"entity\",\"field\",\"op\",\"value\") — filtered IDs (spill)\n" +
            "- =RF.MATCHLIST(\"entity\",\"matchField\",\"op\",\"value\",\"returnField\") — filtered field values (spill)\n" +
            "- =RF.LOOKUP(\"entity\",\"matchField\",\"matchValue\",\"returnField\") — first matching row's field\n" +
            "- =RF.MATCH(\"entity\",\"matchField\",\"matchValue\") — first matching row's ID\n" +
            "- =RF.REPEAT(\"entity\",id,\"repeaterPath\",\"subField\") — repeater sub-field values (spill)\n" +
            "- =RF.REPEATCOUNT(\"entity\",id,\"repeaterPath\") — repeater item count\n" +
            "- =RF.REPEATFIELD(\"entity\",id,\"repeaterPath\",index,\"subField\") — specific repeater item\n\n" +
            $"Description: {description}\n" +
            (entityType != null ? $"Entity type: {entityType}\n" : "") +
            (fieldName != null ? $"Field: {fieldName}\n" : "") +
            "Formula:";

        var request = new LLMRequest
        {
            Messages = [new LLMMessage { Role = LLMRole.User, Content = formulaPrompt }],
            MaxTokens = 200,
            Temperature = 0.1
        };

        var result = await AiConfiguration.LightLlmService.CompleteAsync(request, ct);
        if (!result.IsSuccessful)
            return "Error: Could not generate formula suggestion.";

        var formula = result.Data.Content?.Trim() ?? "";
        // Clean up — ensure it starts with =
        if (!formula.StartsWith('='))
            formula = "=" + formula;
        // Strip any trailing explanation
        var newlineIdx = formula.IndexOf('\n');
        if (newlineIdx > 0) formula = formula[..newlineIdx].Trim();
        // Sanitize single quotes → double quotes in RF function string arguments
        formula = SanitizeFormulaQuotes(formula);

        return new JObject { ["formula"] = formula, ["description"] = description }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string ExecuteProposeSheetFormulas(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> proposedActions)
    {
        var sheetId = args.Value<int?>("sheet_id");
        if (sheetId == null)
            return "Error: sheet_id is required.";

        var operations = args["operations"] as JArray;
        if (operations == null || operations.Count == 0)
            return "Error: operations array is required and must not be empty.";

        if (operations.Count > 500)
            return "Error: Maximum 500 operations per proposal.";

        // Validate user has PEEK_ALL and UPDATE on sheets
        if (!user.Fields.CanUserDo("PEEK_ALL", RfReservedEntities.SheetsEntityName))
            return "Error: You do not have access to sheets.";
        if (!user.Fields.CanUserDo("UPDATE", RfReservedEntities.SheetsEntityName))
            return "Error: You do not have edit permission for sheets.";

        // Validate operations structure
        foreach (var op in operations)
        {
            if (op is not JObject opObj)
                return "Error: Each operation must be an object with 'row' and 'col' properties.";
            if (!opObj.ContainsKey("row") || !opObj.ContainsKey("col"))
                return "Error: Each operation must have 'row' and 'col' properties.";
            if (!opObj.ContainsKey("value") && !opObj.ContainsKey("formula"))
                return "Error: Each operation must have either 'value' or 'formula'.";
        }

        // Validate entity references in formulas (existence + user access)
        foreach (var op in operations.OfType<JObject>())
        {
            var formula = op.Value<string>("formula");
            if (string.IsNullOrEmpty(formula)) continue;

            // Sanitize: replace single quotes with double quotes in RF function arguments.
            // LLMs sometimes produce =RF.IDS('entity') instead of =RF.IDS("entity").
            formula = SanitizeFormulaQuotes(formula);
            op["formula"] = formula;

            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(formula, @"RF\.\w+\(\s*""([^""]+)"""))
            {
                var referencedEntity = m.Groups[1].Value;
                if (!RfConfiguration.EntityNameToConfiguration.ContainsKey(referencedEntity))
                    return $"Error: Formula references unknown entity type '{referencedEntity}'.";
                if (!user.Fields.CanUserDo("PEEK_ALL", referencedEntity))
                    return $"Error: You do not have access to entity type '{referencedEntity}' referenced in formula.";
            }
        }

        var actionId = $"sheet_edit_{Interlocked.Increment(ref _actionIdCounter)}";
        proposedActions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "sheet_edit",
            EntityType = RfReservedEntities.SheetsEntityName,
            EntityId = sheetId.Value,
            Payload = new JObject { ["sheet_id"] = sheetId.Value, ["operations"] = operations },
            Description = $"Write {operations.Count} cell(s) to sheet #{sheetId.Value}",
            RequiresApproval = true
        });

        return $"Proposed: Write {operations.Count} cell(s) to sheet #{sheetId.Value}. Waiting for user approval.";
    }

    /// <summary>
    /// Replaces single-quoted string arguments inside RF.* function calls with double quotes.
    /// E.g. =RF.IDS('objective') → =RF.IDS("objective")
    /// </summary>
    private static string SanitizeFormulaQuotes(string formula)
    {
        // Match RF.<FuncName>(... 'value' ...) and replace single quotes with double quotes
        return System.Text.RegularExpressions.Regex.Replace(
            formula,
            @"(?<=RF\.\w+\([^)]*)'([^']*)'",
            "\"$1\"");
    }

    private static string ExecuteProposeAddSheetSource(
        JObject args, EntityModel<UserEntityFieldsModel> user, List<ProposedAction> proposedActions)
    {
        var sheetId = args.Value<int?>("sheet_id");
        if (sheetId == null)
            return "Error: sheet_id is required.";

        var entity = args.Value<string>("entity");
        if (string.IsNullOrWhiteSpace(entity))
            return "Error: entity is required.";

        // Validate entity type exists
        if (!RfConfiguration.EntityNameToConfiguration.ContainsKey(entity))
            return $"Error: Entity type '{entity}' not found.";

        // Validate user has PEEK_ALL on the entity type
        if (!user.Fields.CanUserDo("PEEK_ALL", entity))
            return $"Error: You do not have access to entity type '{entity}'.";

        // Validate user has access and edit permission for sheets
        if (!user.Fields.CanUserDo("PEEK_ALL", RfReservedEntities.SheetsEntityName))
            return "Error: You do not have access to sheets.";
        if (!user.Fields.CanUserDo("UPDATE", RfReservedEntities.SheetsEntityName))
            return "Error: You do not have edit permission for sheets.";

        var fieldsArray = args["fields"] as JArray;
        var payload = new JObject
        {
            ["sheet_id"] = sheetId.Value,
            ["entity"] = entity
        };
        if (fieldsArray != null)
            payload["fields"] = fieldsArray;

        var actionId = $"sheet_add_source_{Interlocked.Increment(ref _actionIdCounter)}";
        proposedActions.Add(new ProposedAction
        {
            ActionId = actionId,
            ActionType = "sheet_add_source",
            EntityType = RfReservedEntities.SheetsEntityName,
            EntityId = sheetId.Value,
            Payload = payload,
            Description = $"Add entity source '{entity}' to sheet #{sheetId.Value}",
            RequiresApproval = true
        });

        return $"Proposed: Add '{entity}' as a data source to sheet #{sheetId.Value}. Waiting for user approval.";
    }

    // ── Sheet helpers ────────────────────────────────────────────────────────

    private static async Task<(JObject? Sheet, SharingAccessLevel AccessLevel, string? Error)> GetSheetWithAccessCheck(
        int sheetId, EntityModel<UserEntityFieldsModel> user, SharingAccessLevel requiredLevel, CancellationToken ct)
    {
        const string entityName = RfReservedEntities.SheetsEntityName;

        if (!user.Fields.CanUserDo("PEEK_ALL", entityName))
            return (null, SharingAccessLevel.None, "Error: You do not have access to sheets.");

        var result = await AiConfiguration.DatabaseService.GetItemAsync(
            EntityRepositoryService.GetEntityTableName(entityName),
            new DbKey(EntityModelAttributes.Id, new Primitive(sheetId)), null, ct);

        if (!result.IsSuccessful || result.Data == null)
            return (null, SharingAccessLevel.None, $"Error: Sheet #{sheetId} not found.");

        var accessLevel = GetEntitySharingAccessLevel(entityName, result.Data, user);
        if (accessLevel < requiredLevel)
            return (null, accessLevel, "Error: You do not have sufficient access to this sheet.");

        return (result.Data, accessLevel, null);
    }

    private static bool TryParseCellRange(string range, out int startRow, out int startCol, out int endRow, out int endCol)
    {
        startRow = startCol = endRow = endCol = 0;
        range = range.Trim().ToUpperInvariant();

        var parts = range.Split(':');
        if (parts.Length == 1)
        {
            if (!TryParseCellRef(parts[0], out startRow, out startCol))
                return false;
            endRow = startRow;
            endCol = startCol;
            return true;
        }

        if (parts.Length == 2)
            return TryParseCellRef(parts[0], out startRow, out startCol) &&
                   TryParseCellRef(parts[1], out endRow, out endCol);

        return false;
    }

    private static bool TryParseCellRef(string cellRef, out int row, out int col)
    {
        row = col = 0;
        var i = 0;
        // Parse column letters
        while (i < cellRef.Length && char.IsLetter(cellRef[i]))
            i++;
        if (i == 0) return false;
        var colStr = cellRef[..i];
        var rowStr = cellRef[i..];
        if (!int.TryParse(rowStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNum) || rowNum < 1)
            return false;
        row = rowNum - 1; // Convert to 0-based
        col = 0;
        foreach (var c in colStr)
            col = col * 26 + (c - 'A');
        return true;
    }

    private static string GetColumnLetter(int col)
    {
        var result = "";
        do
        {
            result = (char)('A' + col % 26) + result;
            col = col / 26 - 1;
        } while (col >= 0);
        return result;
    }
}
