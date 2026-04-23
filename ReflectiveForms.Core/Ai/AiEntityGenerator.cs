// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Generates a partial entity JObject from a natural language prompt.
/// Strategy: generates one field at a time using isolated prompts (system + context + question).
/// Each field gets a minimal prompt rather than an accumulated conversation to keep token usage
/// predictable and avoid cross-field contamination. The full Q&amp;A log is kept for the debug chat box.
/// Groups and repeaters are handled by recursion (ask "how many items?", then generate each).
/// The result is a draft — NOT saved to the database. The user reviews and saves normally.
/// </summary>
internal static class AiEntityGenerator
{
    internal static async Task<(JObject? Fields, List<LLMMessage> Conversation)> GenerateAsync(
        string entityName, string prompt, CancellationToken cancellationToken)
    {
        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityName);
        if (!schemaResult.IsSuccessful)
            return (null, []);

        var config = RfConfiguration.EntityNameToConfiguration[entityName];
        var readableName = config.EntityConfiguration.EntityReadableNameSingular;
        var entityDescription = config.EntityConfiguration.EntityDescription;

        var strategy = ResolveStrategy();

        return strategy switch
        {
            AiGenerationStrategy.Agentic => await AiEntityGeneratorAgentic.GenerateAsync(
                entityName, readableName, entityDescription, prompt, schemaResult.Data.Fields, cancellationToken),
            AiGenerationStrategy.FieldByField => await GenerateFieldByFieldLegacyAsync(
                readableName, entityDescription, prompt, schemaResult.Data.Fields, cancellationToken),
            _ => await GenerateFieldByFieldAsync(
                entityName, readableName, entityDescription, prompt, schemaResult.Data.Fields, cancellationToken)
        };
    }

    /// <summary>
    /// Resolves the effective generation strategy.
    /// Auto selects BatchJson (the plan-based approach is the new default).
    /// </summary>
    private static AiGenerationStrategy ResolveStrategy()
    {
        var configured = RfConfiguration.AiServiceConfiguration!.GenerationStrategy;
        if (configured != AiGenerationStrategy.Auto)
            return configured;

        // Auto: use BatchJson (plan-based) as the default — it works well with all models.
        // Agentic requires explicit opt-in since it needs tool-calling capable models.
        return AiGenerationStrategy.BatchJson;
    }

    /// <summary>
    /// Legacy field-by-field generation. Generates one field at a time with isolated prompts.
    /// Preserved for backward compatibility when <see cref="AiGenerationStrategy.FieldByField"/> is selected.
    /// </summary>
    private static async Task<(JObject? Fields, List<LLMMessage> Conversation)> GenerateFieldByFieldLegacyAsync(
        string readableName, string? entityDescription, string userPrompt,
        List<FieldSchema> fields, CancellationToken cancellationToken)
    {
        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var result = new JObject();

        var descriptionClause = !string.IsNullOrWhiteSpace(entityDescription)
            ? $" A {readableName} is: {entityDescription}."
            : "";
        var systemPrompt = aiConfig.SystemPromptPrefix +
            $"\nYou are generating field values for a \"{readableName}\" about: {userPrompt}.{descriptionClause}" +
            "\nReply with ONLY the raw value — no field names, labels, or explanations." +
            "\nText/content: write substantial, realistic content. Select: use exact choice values. Dates: use the requested format. Booleans: true or false." +
            "\nProduce values in English unless the user's prompt is in another language.";

        var conversationLog = new List<LLMMessage>
        {
            new() { Role = LLMRole.User, Content = $"Create a {readableName}: {userPrompt}" },
            new() { Role = LLMRole.Assistant, Content = $"Generating fields for this {readableName}..." }
        };

        var genCtx = new GenerationContext(systemPrompt, userPrompt, readableName);

        // Generate title
        var titleQuestion = $"Write a short title about: {userPrompt}";
        conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = $"title ({titleQuestion})" });
        var titlePrompt = genCtx.BuildPrompt(titleQuestion);
        var titleResponse = await CompleteShortAsync(titlePrompt, cancellationToken);
        if (!string.IsNullOrWhiteSpace(titleResponse)
            && !string.Equals(titleResponse, "SKIP", StringComparison.OrdinalIgnoreCase)
            && !titleResponse.StartsWith("Ready", StringComparison.OrdinalIgnoreCase)
            && !IsEchoOfQuestion(titleResponse, titleQuestion))
        {
            var cleanTitle = CleanValue(titleResponse).Trim('"', '\'');
            result["title"] = cleanTitle;
            genCtx.Title = cleanTitle;
            conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = cleanTitle });
        }
        else
        {
            var fallbackTitle = userPrompt.Length > 80 ? userPrompt[..80] + "..." : userPrompt;
            result["title"] = fallbackTitle;
            genCtx.Title = fallbackTitle;
        }

        // Generate all fields linearly (original behavior)
        await GenerateFieldsIntoAsync(result, genCtx, conversationLog, fields, "", cancellationToken);

        // Fallback content retry (same as original)
        foreach (var f in fields)
        {
            if (f.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea
                && !result.ContainsKey(f.Name))
            {
                var retryQuestion = $"Write {f.Label} about: {userPrompt}, a few paragraphs.";
                var retryPrompt = genCtx.BuildPrompt(retryQuestion);
                var retryResponse = await CompleteLongAsync(retryPrompt, cancellationToken);
                if (!string.IsNullOrWhiteSpace(retryResponse) && retryResponse.Length > 50)
                    result[f.Name] = CleanValue(retryResponse);
            }
        }

        PostProcessFields(result, fields);

        return result.Count > 0 ? (result, conversationLog) : (null, conversationLog);
    }

    /// <summary>
    /// Generates entity fields using a planned, multi-phase approach:
    /// 1. Title generation
    /// 2. Batch structured fields (Critical + Structural — one LLM call for all)
    /// 3. Evaluate display conditions, batch conditional fields
    /// 4. Content fields (WYSIWYG/TextArea — individual calls with full context)
    /// 5. Groups and repeaters (recursive generation)
    /// 6. Post-processing (derive slug, excerpt, reading_time, SEO)
    /// 7. Validation + auto-fix + targeted retry
    /// Falls back to field-by-field for any batch parse failures.
    /// </summary>
    private static async Task<(JObject? Fields, List<LLMMessage> Conversation)> GenerateFieldByFieldAsync(
        string entityName, string readableName, string? entityDescription, string userPrompt,
        List<FieldSchema> fields, CancellationToken cancellationToken)
    {
        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var result = new JObject();

        var descriptionClause = !string.IsNullOrWhiteSpace(entityDescription)
            ? $" A {readableName} is: {entityDescription}."
            : "";
        var systemPrompt = aiConfig.SystemPromptPrefix +
            $"\nYou are generating field values for a \"{readableName}\" about: {userPrompt}.{descriptionClause}" +
            "\nReply with ONLY the raw value — no field names, labels, or explanations." +
            "\nText/content: write substantial, realistic content. Select: use exact choice values. Dates: use the requested format. Booleans: true or false." +
            "\nProduce values in English unless the user's prompt is in another language.";

        // Conversation log — for debug chat box only, NOT sent to LLM
        var conversationLog = new List<LLMMessage>
        {
            new() { Role = LLMRole.User, Content = $"Create a {readableName}: {userPrompt}" },
            new() { Role = LLMRole.Assistant, Content = $"Generating fields for this {readableName}..." }
        };

        // Generation context — shared across fields so each prompt can include title + already-generated content
        var genCtx = new GenerationContext(systemPrompt, userPrompt, readableName);

        // ── Step A: Generate title ──────────────────────────────────
        var titleQuestion = $"Write a short title about: {userPrompt}";
        conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = $"title ({titleQuestion})" });
        var titlePrompt = genCtx.BuildPrompt(titleQuestion);
        var titleResponse = await CompleteShortAsync(titlePrompt, cancellationToken);
        if (!string.IsNullOrWhiteSpace(titleResponse)
            && !string.Equals(titleResponse, "SKIP", StringComparison.OrdinalIgnoreCase)
            && !titleResponse.StartsWith("Ready", StringComparison.OrdinalIgnoreCase)
            && !IsEchoOfQuestion(titleResponse, titleQuestion))
        {
            var cleanTitle = CleanValue(titleResponse);
            if (cleanTitle.Length <= 80 && !cleanTitle.Contains("short title", StringComparison.OrdinalIgnoreCase))
            {
                result["title"] = cleanTitle;
                genCtx.Title = cleanTitle;
                conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = cleanTitle });
            }
            else
            {
                conversationLog.RemoveAt(conversationLog.Count - 1);
            }
        }
        else
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
        }

        // Fallback: if LLM didn't produce a title, capitalize the user prompt
        if (result["title"] == null)
        {
            var fallbackTitle = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(userPrompt.ToLowerInvariant());
            if (fallbackTitle.Length > 60)
                fallbackTitle = fallbackTitle[..60].TrimEnd() + "...";
            result["title"] = fallbackTitle;
            genCtx.Title = fallbackTitle;
        }

        // ── Build generation plan ──────────────────────────────────
        var plan = GenerationPlanner.BuildPlan(fields);

        // ── Resolve dynamic runtime choices (Phase 6) ──────────────
        var resolvedDynamicChoices = new Dictionary<string, List<SelectChoice>>();
        foreach (var f in fields)
        {
            if (f.HasDynamicChoicesRuntime && f.Type == FieldSchemaType.Select)
            {
                var dynChoices = AiEntityGeneratorValidator.ResolveDynamicRuntimeChoices(f, result);
                if (dynChoices != null)
                    resolvedDynamicChoices[f.Name] = dynChoices;
            }
        }

        // ── Fetch few-shot examples (best-effort, non-blocking) ────
        var exampleJson = await FetchExampleEntityJsonAsync(entityName, userPrompt, cancellationToken);

        // ── Step B: Batch generate Critical + Structural fields ────
        var batchFields = plan.GetByPriority(FieldPriority.Critical)
            .Concat(plan.GetByPriority(FieldPriority.Structural))
            .Where(e => e.Schema.Type is not (FieldSchemaType.Group or FieldSchemaType.Repeater))
            .ToList();

        if (batchFields.Count > 0)
        {
            _batchResults.Value = new Dictionary<string, JToken>();
            try
            {
                var batchGenerated = await GenerateStructuredFieldsBatchAsync(
                    genCtx, conversationLog, batchFields, exampleJson, resolvedDynamicChoices, cancellationToken);

                if (batchGenerated && _batchResults.Value!.Count > 0)
                {
                    foreach (var (key, value) in _batchResults.Value)
                        result[key] = value;
                }

                if (!batchGenerated)
                {
                    // Fallback: generate these fields one-by-one
                    foreach (var entry in batchFields)
                    {
                        await GenerateSingleFieldAsync(result, genCtx, conversationLog, entry.Schema, "", cancellationToken);
                    }
                }
            }
            finally
            {
                _batchResults.Value = null;
            }
        }

        // ── Re-resolve dynamic choices after structural fields are set ──
        foreach (var f in fields)
        {
            if (f.HasDynamicChoicesRuntime && f.Type == FieldSchemaType.Select)
            {
                var dynChoices = AiEntityGeneratorValidator.ResolveDynamicRuntimeChoices(f, result);
                if (dynChoices != null)
                    resolvedDynamicChoices[f.Name] = dynChoices;
            }
        }

        // ── Step C: Evaluate display conditions → batch conditional fields ──
        var resolvedConditionals = plan.GetResolvedConditionals(result)
            .Where(e => result[e.FieldName] == null) // Only generate if not already set
            .Where(e => e.Schema.Type is not (FieldSchemaType.Group or FieldSchemaType.Repeater))
            .ToList();

        if (resolvedConditionals.Count > 0)
        {
            _batchResults.Value = new Dictionary<string, JToken>();
            try
            {
                var condBatchGenerated = await GenerateStructuredFieldsBatchAsync(
                    genCtx, conversationLog, resolvedConditionals, exampleJson, resolvedDynamicChoices, cancellationToken);

                if (condBatchGenerated && _batchResults.Value!.Count > 0)
                {
                    foreach (var (key, value) in _batchResults.Value)
                        result[key] = value;
                }

                if (!condBatchGenerated)
                {
                    foreach (var entry in resolvedConditionals)
                    {
                        await GenerateSingleFieldAsync(result, genCtx, conversationLog, entry.Schema, "", cancellationToken);
                    }
                }
            }
            finally
            {
                _batchResults.Value = null;
            }
        }

        // ── Step D: Content fields (individual calls with full context) ──
        var contentFields = plan.GetByPriority(FieldPriority.Content)
            .Where(e => result[e.FieldName] == null)
            .ToList();

        foreach (var entry in contentFields)
        {
            await GenerateContentFieldAsync(result, genCtx, conversationLog, entry.Schema, cancellationToken);
        }

        // Also generate resolved conditional content fields
        var conditionalContent = plan.GetResolvedConditionals(result)
            .Where(e => result[e.FieldName] == null)
            .Where(e => e.Schema.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea)
            .ToList();

        foreach (var entry in conditionalContent)
        {
            await GenerateContentFieldAsync(result, genCtx, conversationLog, entry.Schema, cancellationToken);
        }

        // ── Step E: Groups and repeaters (recursive, field-by-field) ──
        foreach (var entry in plan.Entries.Where(e =>
            e.Schema.Type is FieldSchemaType.Group or FieldSchemaType.Repeater
            && e.Priority != FieldPriority.Derived))
        {
            // Skip if display condition not met
            if (!string.IsNullOrEmpty(entry.Schema.DisplayCondition)
                && !IsDisplayConditionMet(entry.Schema.DisplayCondition, result))
                continue;

            if (entry.Schema.Type == FieldSchemaType.Group && entry.Schema.GroupOptions?.ChildSchema != null)
            {
                var groupObj = new JObject();
                await GenerateFieldsIntoAsync(groupObj, genCtx, conversationLog,
                    entry.Schema.GroupOptions.ChildSchema, entry.Schema.Label, cancellationToken);
                if (groupObj.Count > 0)
                    result[entry.FieldName] = groupObj;
            }
            else if (entry.Schema.Type == FieldSchemaType.Repeater && entry.Schema.RepeaterOptions?.ItemSchema != null)
            {
                var repeaterArray = await GenerateRepeaterAsync(
                    genCtx, conversationLog, entry.Schema, entry.Schema.Label, cancellationToken);
                if (repeaterArray is { Count: > 0 })
                    result[entry.FieldName] = repeaterArray;
            }
        }

        // ── Step F: Fallback content retry ──────────────────────────
        foreach (var f in fields)
        {
            if (f.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea
                && f.Name != "excerpt" && result[f.Name] == null)
            {
                var retryPrompt = genCtx.BuildPrompt($"Write a short article about: {userPrompt}");
                var retryContent = await CompleteLongAsync(retryPrompt, cancellationToken);
                if (!string.IsNullOrWhiteSpace(retryContent))
                {
                    var cleaned = CleanValue(retryContent);
                    if (cleaned.Length >= 50 && !IsEchoOfTitle(cleaned, genCtx.Title ?? ""))
                    {
                        result[f.Name] = cleaned;
                        genCtx.ContentSummary = cleaned.Length > 100 ? cleaned[..100] + "..." : cleaned;
                    }
                }
                break;
            }
        }

        // ── Step G: Post-process derived fields ─────────────────────
        PostProcessFields(result, fields);

        // ── Step G.5: Generate any derived fields that PostProcessFields couldn't compute ──
        var missingDerived = plan.GetByPriority(FieldPriority.Derived)
            .Where(e => result[e.FieldName] == null && e.Schema.Type is not (FieldSchemaType.Group or FieldSchemaType.Repeater))
            .ToList();
        foreach (var entry in missingDerived)
        {
            await GenerateSingleFieldAsync(result, genCtx, conversationLog, entry.Schema, "", cancellationToken);
        }

        // ── Step H: Validate + auto-fix + targeted retry ────────────
        var validationErrors = AiEntityGeneratorValidator.ValidateDraft(result, fields);
        var unfixableErrors = AiEntityGeneratorValidator.ApplyAutoFixes(result, fields, validationErrors);

        if (unfixableErrors.Count > 0)
        {
            await RetryUnfixableFieldsAsync(result, genCtx, conversationLog, fields, unfixableErrors, cancellationToken);
        }

        return (result.Count > 0 ? result : null, conversationLog);
    }

    /// <summary>
    /// Holds the shared context for field generation — title, content summary, and prompt base.
    /// This is NOT the conversation messages sent to the LLM — each field builds its own minimal prompt.
    /// </summary>
    private sealed class GenerationContext(string systemPrompt, string userPrompt, string readableName)
    {
        public string SystemPrompt { get; } = systemPrompt;
        public string UserPrompt { get; } = userPrompt;
        public string ReadableName { get; } = readableName;
        public string? Title { get; set; }
        public string? ContentSummary { get; set; }

        /// <summary>
        /// Builds a minimal per-field prompt: system + one question.
        /// The topic is already embedded in the question by the caller.
        /// </summary>
        public LLMMessage[] BuildPrompt(string fieldQuestion)
        {
            return
            [
                new LLMMessage { Role = LLMRole.System, Content = SystemPrompt },
                new LLMMessage { Role = LLMRole.User, Content = fieldQuestion }
            ];
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Few-shot example retrieval (Phase 4)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches one existing entity that is semantically similar to the user prompt,
    /// returning a trimmed JSON string suitable for inclusion in generation prompts.
    /// Returns null if no example is available (vector service not configured, no indexed entities, etc.).
    /// </summary>
    internal static async Task<string?> FetchExampleEntityJsonAsync(
        string entityName, string userPrompt, CancellationToken cancellationToken)
    {
        try
        {
            if (AiConfiguration.VectorService == null || AiConfiguration.LightLlmService == null)
                return null;

            var collectionName = AiVectorIndexer.GetCollectionName(entityName);

            var searchResult = await AiConfiguration.VectorService.SemanticSearchAsync(
                AiConfiguration.LightLlmService,
                collectionName,
                userPrompt,
                topK: 1,
                filter: null,
                includeMetadata: true,
                cancellationToken);

            if (!searchResult.IsSuccessful || searchResult.Data == null || searchResult.Data.Count == 0)
                return null;

            var bestHit = searchResult.Data[0];
            if (!int.TryParse(bestHit.Id, out var entityId))
                return null;

            // Fetch the actual entity data from the database
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                entityName,
                new DbKey(EntityModelAttributes.Id, entityId),
                null, cancellationToken);

            if (!entityResult.IsSuccessful || entityResult.Data == null)
                return null;

            // Strip system-managed fields to keep the example focused on content fields
            var example = new JObject(entityResult.Data);
            example.Remove(EntityModelAttributes.Id);
            example.Remove(EntityModelAttributes.DateGmt);
            example.Remove(EntityModelAttributes.ModifiedGmt);
            example.Remove(EntityModelAttributes.Author);

            var json = example.ToString(Newtonsoft.Json.Formatting.Indented);
            // Trim to avoid blowing up the prompt
            return json.Length > 2000 ? json[..2000] + "\n  ... (truncated)" : json;
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError(ex);
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Batch and content generation methods (Phase 3)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates multiple structured fields in a single LLM call by asking for a JSON response.
    /// Returns true if batch generation succeeded, false if caller should fall back to field-by-field.
    /// On success, parsed values are applied directly to <see cref="GenerationContext"/> result object
    /// referenced via the conversationLog side-channel.
    /// </summary>
    private static async Task<bool> GenerateStructuredFieldsBatchAsync(
        GenerationContext genCtx, List<LLMMessage> conversationLog,
        List<PlanEntry> entries, string? exampleJson,
        Dictionary<string, List<SelectChoice>>? resolvedDynamicChoices,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return true;

        // Build the batch schema prompt (with dynamic choices if available)
        var schemaDesc = GenerationPlanner.BuildBatchSchemaPrompt(entries, resolvedDynamicChoices);

        var exampleClause = !string.IsNullOrEmpty(exampleJson)
            ? $"\n\nHere is an existing entity for reference (match its style and patterns):\n{exampleJson}\n"
            : "";

        var batchQuestion =
            $"Topic: {genCtx.UserPrompt}" +
            exampleClause +
            "\n\nFill in ALL of the following fields as a JSON object. " +
            "Use the exact field names as keys. " +
            "For select fields, use the exact choice value (not the label). " +
            "For dates, use the specified format. " +
            "Respond with ONLY valid JSON, no explanation.\n\n" +
            schemaDesc;

        // Include title context in system prompt, not user message
        var batchSystemPrompt = genCtx.SystemPrompt +
            (genCtx.Title != null ? $"\nThe title is: {genCtx.Title}" : "");

        conversationLog.Add(new LLMMessage
        {
            Role = LLMRole.User,
            Content = $"[batch] {entries.Count} structured fields"
        });

        var prompt = new LLMMessage[]
        {
            new() { Role = LLMRole.System, Content = batchSystemPrompt },
            new() { Role = LLMRole.User, Content = batchQuestion }
        };

        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var request = new LLMRequest
        {
            Messages = prompt,
            MaxTokens = Math.Min(aiConfig.MaxCompletionTokens, 512),
            Temperature = aiConfig.Temperature
        };

        var llmResult = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
        if (!llmResult.IsSuccessful || string.IsNullOrWhiteSpace(llmResult.Data.Content))
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return false;
        }

        // Try to extract JSON from the response (may have markdown fences)
        var rawContent = llmResult.Data.Content.Trim();
        var jsonStr = ExtractJsonObject(rawContent);
        if (jsonStr == null)
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return false;
        }

        JObject batchResult;
        try
        {
            batchResult = JObject.Parse(jsonStr);
        }
        catch
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return false;
        }

        // Apply each parsed value through the existing ParseFieldValue pipeline
        var appliedFields = new List<string>();
        foreach (var entry in entries)
        {
            var token = batchResult[entry.FieldName];
            if (token == null || token.Type == JTokenType.Null)
                continue;

            var stringValue = token.Type == JTokenType.String
                ? token.Value<string>()!
                : token.ToString();

            var parsed = ParseFieldValue(entry.Schema, stringValue);
            if (parsed != null)
            {
                _batchResults.Value![entry.FieldName] = parsed;
                appliedFields.Add(entry.FieldName);
            }
            else
            {
                // For non-string types (booleans, numbers), try using the token directly
                if (token.Type == JTokenType.Boolean)
                {
                    _batchResults.Value![entry.FieldName] = token;
                    appliedFields.Add(entry.FieldName);
                }
                else if (token.Type is JTokenType.Integer or JTokenType.Float)
                {
                    _batchResults.Value![entry.FieldName] = token;
                    appliedFields.Add(entry.FieldName);
                }
            }
        }

        if (appliedFields.Count > 0)
        {
            conversationLog.Add(new LLMMessage
            {
                Role = LLMRole.Assistant,
                Content = $"[batch] Set {appliedFields.Count} fields: {string.Join(", ", appliedFields)}"
            });
        }
        else
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
        }

        return appliedFields.Count > 0;
    }

    // Async-local storage for batch results — flows across await boundaries, cleared per generation
    private static readonly AsyncLocal<Dictionary<string, JToken>?> _batchResults = new();

    /// <summary>
    /// Generates a single content field (WYSIWYG/TextArea) with full draft context.
    /// </summary>
    private static async Task GenerateContentFieldAsync(
        JObject target, GenerationContext genCtx, List<LLMMessage> conversationLog,
        FieldSchema field, CancellationToken cancellationToken)
    {
        var question = $"Write {field.Label} about: {genCtx.UserPrompt}";

        if (field.Type == FieldSchemaType.WysiwygEditor)
            question += ", HTML content with paragraphs";
        else
            question += ", a few paragraphs of plain text";

        // Include title and existing content as system-level context
        // (not in the user question, to keep the user message focused on the actual request)
        var contextParts = new List<string> { genCtx.SystemPrompt };
        if (genCtx.Title != null) contextParts.Add($"The title is: {genCtx.Title}");
        if (genCtx.ContentSummary != null) contextParts.Add($"Content written so far: {genCtx.ContentSummary}");
        var systemPrompt = string.Join("\n", contextParts);

        conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = $"{field.Name} ({field.Label})" });

        var prompt = new LLMMessage[]
        {
            new() { Role = LLMRole.System, Content = systemPrompt },
            new() { Role = LLMRole.User, Content = question }
        };
        var response = await CompleteLongAsync(prompt, cancellationToken);

        if (string.IsNullOrWhiteSpace(response) || IsEchoOfQuestion(response, question))
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return;
        }

        var cleaned = CleanValue(response);

        // Content quality checks
        if (cleaned.Length < 50 || IsEchoOfTitle(cleaned, genCtx.Title ?? ""))
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return;
        }

        // Reject JSON blobs that survived CleanValue — content fields should never contain raw JSON
        if (LooksLikeJsonStructure(cleaned))
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return;
        }

        // Skip repetitive garbage
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Select(w => w.ToLowerInvariant()).Distinct().Count();
        if (words.Length > 10 && uniqueWords < words.Length / 3)
        {
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return;
        }

        target[field.Name] = cleaned;
        conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = cleaned.Length > 100 ? cleaned[..100] + "..." : cleaned });

        // Update content summary
        if (genCtx.ContentSummary == null && cleaned.Length > 30)
        {
            var plain = StripHtmlTags(cleaned);
            genCtx.ContentSummary = plain.Length > 100 ? plain[..100] + "..." : plain;
        }
    }

    /// <summary>
    /// Generates a single structured field using the existing field-by-field approach (fallback).
    /// </summary>
    private static async Task GenerateSingleFieldAsync(
        JObject target, GenerationContext genCtx, List<LLMMessage> conversationLog,
        FieldSchema field, string breadcrumb, CancellationToken cancellationToken)
    {
        // Skip already-generated fields
        if (target[field.Name] != null) return;

        // Skip Select fields with dynamic runtime choices
        if (field.Type == FieldSchemaType.Select && field.HasDynamicChoicesRuntime && GetEnumValues(field) == null)
            return;

        var description = string.IsNullOrEmpty(breadcrumb)
            ? BuildFieldDescription(field)
            : $"{breadcrumb} > {field.Label}" + (field.Instructions != null ? $" — {StripHtmlTags(field.Instructions)}" : "");
        var fieldType = GetSimpleTypeName(field);
        var enumValues = GetEnumValues(field);

        var value = await AskForFieldAsync(genCtx, conversationLog, field.Name, description, fieldType, enumValues, cancellationToken);
        if (string.IsNullOrWhiteSpace(value) || value == "SKIP")
        {
            // Apply fallbacks for known types
            if (field.Type == FieldSchemaType.Select && field.SelectOptions?.Choices is { Count: > 0 })
            {
                var defaultStr = field.DefaultValue?.ToString();
                var fallback = !string.IsNullOrEmpty(defaultStr) ? defaultStr : field.SelectOptions.Choices[0].Value;
                if (!string.IsNullOrEmpty(fallback))
                {
                    target[field.Name] = fallback;
                    conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = fallback });
                }
            }
            else if (field.Type == FieldSchemaType.DatePicker)
            {
                target[field.Name] = FormatTodayForDateField(field);
            }
            else if (field.Type == FieldSchemaType.Checkbox)
            {
                target[field.Name] = field.DefaultValue is bool dv ? dv : false;
            }
            return;
        }

        var cleaned = CleanValue(value);

        // Select field parsing
        if (field.Type == FieldSchemaType.Select)
        {
            var parsedRaw = ParseFieldValue(field, value);
            if (parsedRaw != null) { target[field.Name] = parsedRaw; return; }
            var parsedCleaned = ParseFieldValue(field, cleaned);
            if (parsedCleaned != null) { target[field.Name] = parsedCleaned; return; }
            if (field.SelectOptions?.Choices is { Count: > 0 })
            {
                var defaultStr = field.DefaultValue?.ToString();
                var fb = !string.IsNullOrEmpty(defaultStr) ? defaultStr : field.SelectOptions.Choices[0].Value;
                if (!string.IsNullOrEmpty(fb)) target[field.Name] = fb;
            }
            return;
        }

        var parsed = ParseFieldValue(field, cleaned);
        if (parsed != null)
        {
            target[field.Name] = parsed;
        }
        else if (field.Type == FieldSchemaType.Checkbox)
        {
            target[field.Name] = field.DefaultValue is bool dv ? dv : false;
        }
        else if (field.Type == FieldSchemaType.DatePicker)
        {
            target[field.Name] = FormatTodayForDateField(field);
        }
    }

    /// <summary>
    /// Extracts a JSON object from LLM output, handling markdown code fences.
    /// </summary>
    private static string? ExtractJsonObject(string text)
    {
        // Try direct parse first
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            return trimmed;

        // Strip markdown code fences
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                var withoutFence = trimmed[(firstNewline + 1)..];
                var lastFence = withoutFence.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence >= 0)
                    withoutFence = withoutFence[..lastFence];
                withoutFence = withoutFence.Trim();
                if (withoutFence.StartsWith('{') && withoutFence.EndsWith('}'))
                    return withoutFence;
            }
        }

        // Try finding first { to last }
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return null;
    }

    /// <summary>
    /// Recursively generates fields from a schema list into a target JObject.
    /// Handles flat fields, groups (sub-objects), and repeaters (arrays of sub-objects),
    /// with arbitrary nesting depth (groups inside repeaters inside groups, etc.).
    /// </summary>
    private static async Task GenerateFieldsIntoAsync(
        JObject target, GenerationContext genCtx, List<LLMMessage> conversationLog,
        List<FieldSchema> fields, string breadcrumb, CancellationToken cancellationToken)
    {
        foreach (var field in fields)
        {
            if (field.Type is FieldSchemaType.Relation or FieldSchemaType.MediaSourceBase64)
                continue;

            // Skip title — already generated explicitly before the recursive pass
            if (field.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip fields whose display condition is not met by already-generated values
            if (!string.IsNullOrEmpty(field.DisplayCondition) && !IsDisplayConditionMet(field.DisplayCondition, target))
                continue;

            var fieldPath = string.IsNullOrEmpty(breadcrumb) ? field.Label : $"{breadcrumb} > {field.Label}";

            // Group → recurse into child schema, producing a sub-object
            if (field.Type == FieldSchemaType.Group && field.GroupOptions?.ChildSchema != null)
            {
                var groupObj = new JObject();
                await GenerateFieldsIntoAsync(groupObj, genCtx, conversationLog, field.GroupOptions.ChildSchema, fieldPath, cancellationToken);
                if (groupObj.Count > 0)
                    target[field.Name] = groupObj;
                continue;
            }

            // Repeater → ask how many items, then recurse for each item's sub-schema
            if (field.Type == FieldSchemaType.Repeater && field.RepeaterOptions?.ItemSchema != null)
            {
                var repeaterArray = await GenerateRepeaterAsync(genCtx, conversationLog, field, fieldPath, cancellationToken);
                if (repeaterArray is { Count: > 0 })
                    target[field.Name] = repeaterArray;
                continue;
            }

            // Leaf field → ask the LLM directly
            var description = string.IsNullOrEmpty(breadcrumb)
                ? BuildFieldDescription(field)
                : $"{fieldPath}" + (field.Instructions != null ? $" — {StripHtmlTags(field.Instructions)}" : "");
            var fieldType = GetSimpleTypeName(field);
            var enumValues = GetEnumValues(field);

            // Skip Select fields that use dynamic runtime choices — we don't have the choices
            if (field.Type == FieldSchemaType.Select && field.HasDynamicChoicesRuntime && enumValues == null)
                continue;

            var value = await AskForFieldAsync(genCtx, conversationLog, field.Name, description, fieldType, enumValues, cancellationToken);
            if (string.IsNullOrWhiteSpace(value) || value == "SKIP")
            {
                // Fallback for Select fields: use defaultValue or first choice
                if (field.Type == FieldSchemaType.Select && field.SelectOptions?.Choices is { Count: > 0 })
                {
                    var defaultStr = field.DefaultValue?.ToString();
                    var fallback = !string.IsNullOrEmpty(defaultStr) ? defaultStr
                        : field.SelectOptions.Choices[0].Value;
                    if (!string.IsNullOrEmpty(fallback))
                    {
                        target[field.Name] = fallback;
                        conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = fallback });
                    }
                }
                // Fallback for DatePicker: use today's date in the field's configured format
                else if (field.Type == FieldSchemaType.DatePicker)
                {
                    target[field.Name] = FormatTodayForDateField(field);
                }
                // Fallback for Checkbox: use field's default value
                else if (field.Type == FieldSchemaType.Checkbox)
                {
                    target[field.Name] = field.DefaultValue is bool dv ? dv : false;
                }
                continue;
            }

            var cleaned = CleanValue(value);

            // For Select fields, try parsing the raw value first (CleanValue may strip the choice keyword)
            if (field.Type == FieldSchemaType.Select)
            {
                var parsedRaw = ParseFieldValue(field, value);
                if (parsedRaw != null)
                {
                    target[field.Name] = parsedRaw;
                    continue;
                }
                // Also try the cleaned value
                var parsedCleaned = ParseFieldValue(field, cleaned);
                if (parsedCleaned != null)
                {
                    target[field.Name] = parsedCleaned;
                    continue;
                }
                // LLM returned something that doesn't match any choice → use fallback
                if (field.SelectOptions?.Choices is { Count: > 0 })
                {
                    var defaultStr = field.DefaultValue?.ToString();
                    var fb = !string.IsNullOrEmpty(defaultStr) ? defaultStr
                        : field.SelectOptions.Choices[0].Value;
                    if (!string.IsNullOrEmpty(fb))
                        target[field.Name] = fb;
                }
                continue;
            }
            // Skip trivially short or title-echo content for WYSIWYG/TextArea
            if (field.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea)
            {
                if (cleaned.Length < 50 || IsEchoOfTitle(cleaned, genCtx.Title ?? ""))
                    continue;
                // Reject JSON blobs — content fields should never contain raw JSON
                if (LooksLikeJsonStructure(cleaned))
                    continue;
                // Skip repetitive garbage (e.g. "Title:\nTitle:\nTitle:...")
                var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var uniqueWords = words.Select(w => w.ToLowerInvariant()).Distinct().Count();
                if (words.Length > 10 && uniqueWords < words.Length / 3)
                    continue;
            }
            var parsed = ParseFieldValue(field, cleaned);
            if (parsed != null)
            {
                target[field.Name] = parsed;

                // Update content summary if this is a substantial text field
                if (field.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea
                    && cleaned.Length > 30 && genCtx.ContentSummary == null)
                {
                    var plain = StripHtmlTags(cleaned);
                    genCtx.ContentSummary = plain.Length > 100 ? plain[..100] + "..." : plain;
                }
            }
            else if (field.Type == FieldSchemaType.Checkbox)
            {
                // LLM produced an unrecognizable response for checkbox — use default value
                target[field.Name] = field.DefaultValue is bool dv ? dv : false;
            }
            else if (field.Type == FieldSchemaType.DatePicker)
            {
                // LLM produced an unrecognizable response for date — use today
                target[field.Name] = FormatTodayForDateField(field);
            }
        }
    }

    /// <summary>
    /// Generates a repeater: asks how many items, then recursively generates each item's fields.
    /// </summary>
    private static async Task<JArray?> GenerateRepeaterAsync(
        GenerationContext genCtx, List<LLMMessage> conversationLog,
        FieldSchema repeaterField, string breadcrumb,
        CancellationToken cancellationToken)
    {
        var opts = repeaterField.RepeaterOptions!;
        var maxItems = opts.MaxItems ?? 5;
        var minItems = opts.MinItems ?? 0;
        // Default to a small number to keep generation fast and predictable.
        // Use minItems if set, otherwise 1 item (or 0 for optional repeaters).
        // Skip repeaters that contain URL fields — LLMs tend to fabricate URLs.
        var hasUrlField = opts.ItemSchema.Any(f => f.Type == FieldSchemaType.Url);
        var count = hasUrlField ? 0 : (minItems > 0 ? minItems : Math.Min(1, maxItems));

        // Log the decision for the debug chat box
        conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = $"How many \"{repeaterField.Label}\" items?" });
        conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = count.ToString() });

        if (count == 0)
            return null;

        var items = new JArray();

        for (var i = 0; i < count; i++)
        {
            var itemObj = new JObject();
            var itemBreadcrumb = $"{breadcrumb} (item {i + 1} of {count})";
            await GenerateFieldsIntoAsync(itemObj, genCtx, conversationLog, opts.ItemSchema, itemBreadcrumb, cancellationToken);
            if (itemObj.Count > 0)
                items.Add(itemObj);
        }

        return items.Count > 0 ? items : null;
    }

    /// <summary>
    /// Retries LLM generation for fields that failed validation and cannot be auto-fixed.
    /// Each field gets one targeted retry with full draft context.
    /// </summary>
    private static async Task RetryUnfixableFieldsAsync(
        JObject result, GenerationContext genCtx, List<LLMMessage> conversationLog,
        List<FieldSchema> allFields, List<FieldValidationError> errors,
        CancellationToken cancellationToken)
    {
        var fieldMap = BuildFieldMap(allFields);

        foreach (var error in errors)
        {
            // Only retry content-too-short and missing-conditional errors
            if (error.Type is not (FieldValidationErrorType.ContentTooShort
                or FieldValidationErrorType.MissingConditional
                or FieldValidationErrorType.MissingRequired))
                continue;

            // Find the field schema by name (use the simple name, not the nested path)
            if (!fieldMap.TryGetValue(error.FieldName, out var field))
                continue;

            // Build a context-rich retry prompt
            var draftSummary = BuildDraftSummary(result);
            var fieldType = GetSimpleTypeName(field);
            var enumValues = GetEnumValues(field);
            var constraintHint = BuildConstraintHint(fieldType, enumValues);

            string question;
            if (fieldType is "html" or "text_long")
                question = $"Context: {draftSummary}\n\nWrite {field.Label} about: {genCtx.UserPrompt}{constraintHint}";
            else if (fieldType == "enum" && enumValues is { Count: > 0 })
                question = $"Context: {draftSummary}\n\nPick one for {field.Label}: {string.Join(", ", enumValues)}";
            else
                question = $"Context: {draftSummary}\n\n{field.Label}{constraintHint}";

            conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = $"[retry] {error.FieldName} ({error.Message})" });

            var prompt = genCtx.BuildPrompt(question);

            string? response;
            if (fieldType is "html" or "text_long")
                response = await CompleteLongAsync(prompt, cancellationToken);
            else
                response = await CompleteShortAsync(prompt, cancellationToken);

            if (string.IsNullOrWhiteSpace(response))
            {
                conversationLog.RemoveAt(conversationLog.Count - 1);
                continue;
            }

            var cleaned = CleanValue(response);
            var parsed = ParseFieldValue(field, cleaned);
            if (parsed != null)
            {
                SetFieldAtPath(result, error.FieldPath, parsed);
                conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = cleaned });
            }
            else
            {
                conversationLog.RemoveAt(conversationLog.Count - 1);
            }
        }
    }

    /// <summary>
    /// Builds a compact summary of the current draft for retry context.
    /// </summary>
    private static string BuildDraftSummary(JObject draft)
    {
        var parts = new List<string>();
        foreach (var prop in draft.Properties())
        {
            if (prop.Value.Type == JTokenType.String)
            {
                var val = prop.Value.Value<string>() ?? "";
                if (val.Length > 80) val = val[..80] + "...";
                parts.Add($"{prop.Name}: {val}");
            }
            else if (prop.Value.Type is JTokenType.Integer or JTokenType.Float or JTokenType.Boolean)
            {
                parts.Add($"{prop.Name}: {prop.Value}");
            }
        }
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Flattens all fields (including group/repeater children) into a name→schema map.
    /// </summary>
    private static Dictionary<string, FieldSchema> BuildFieldMap(List<FieldSchema> fields)
    {
        var map = new Dictionary<string, FieldSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            map[field.Name] = field;
            if (field.GroupOptions?.ChildSchema != null)
                foreach (var child in field.GroupOptions.ChildSchema)
                    map[child.Name] = child;
            if (field.RepeaterOptions?.ItemSchema != null)
                foreach (var child in field.RepeaterOptions.ItemSchema)
                    map[child.Name] = child;
        }
        return map;
    }

    /// <summary>
    /// Sets a value at a potentially nested path (e.g. "seo_metadata.meta_title").
    /// </summary>
    private static void SetFieldAtPath(JObject root, string path, JToken value)
    {
        if (!path.Contains('.') && !path.Contains('['))
        {
            root[path] = value;
            return;
        }

        var segments = path.Split('.');
        JToken current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            var bracketIdx = seg.IndexOf('[');
            if (bracketIdx >= 0)
            {
                var propName = seg[..bracketIdx];
                var indexStr = seg[(bracketIdx + 1)..seg.IndexOf(']')];
                if (int.TryParse(indexStr, out var idx) && current[propName] is JArray arr && idx < arr.Count)
                    current = arr[idx];
                else return;
            }
            else
            {
                var next = current[seg];
                if (next == null) return;
                current = next;
            }
        }

        if (current is JObject lastObj)
            lastObj[segments[^1]] = value;
    }

    /// <summary>
    /// Asks the LLM for a single field value using an isolated per-field prompt.
    /// Logs both the question and the answer to conversationLog for the debug chat box.
    /// </summary>
    private static async Task<string?> AskForFieldAsync(
        GenerationContext genCtx, List<LLMMessage> conversationLog,
        string fieldName, string fieldDescription,
        string fieldType, List<string>? enumValues, CancellationToken cancellationToken)
    {
        var constraintHint = BuildConstraintHint(fieldType, enumValues);

        // Build the actual LLM question — strategy varies by field type:
        // - Content fields: embed the topic so SmolLM2 writes about it
        // - Select fields: just list choices (description gets echoed if included)
        // - Other fields: use description + constraint
        string question;
        if (fieldType is "html" or "text_long")
            question = $"Write about: {genCtx.UserPrompt}{constraintHint}";
        else if (fieldType == "enum" && enumValues is { Count: > 0 })
            question = $"Pick one: {string.Join(", ", enumValues)}";
        else if (fieldType == "date")
            question = $"A date (YYYY-MM-DD) for: {genCtx.UserPrompt}";  // LLM output will be reformatted to field's configured format
        else if (fieldType == "boolean")
        {
            var labelOnly = fieldDescription.Contains(" — ")
                ? fieldDescription[..fieldDescription.IndexOf(" — ", StringComparison.Ordinal)].TrimEnd('?', '.')
                : fieldDescription.TrimEnd('?', '.');
            question = $"{labelOnly}? yes or no";
        }
        else
            question = $"{fieldDescription}{constraintHint}";

        // For the conversation log (debug UI), include the field name for readability
        conversationLog.Add(new LLMMessage { Role = LLMRole.User, Content = $"{fieldName} ({fieldDescription}{constraintHint})" });

        // Build isolated prompt for this field — NOT the full conversation
        var prompt = genCtx.BuildPrompt(question);

        string? response;
        if (fieldType is "html" or "text_long")
            response = await CompleteLongAsync(prompt, cancellationToken);
        else
            response = await CompleteShortAsync(prompt, cancellationToken);

        // Filter non-answers: empty, "Ready..." patterns, context echoes, or question echoes
        if (string.IsNullOrWhiteSpace(response) ||
            response.StartsWith("Ready", StringComparison.OrdinalIgnoreCase) ||
            response.StartsWith("The user wants", StringComparison.OrdinalIgnoreCase) ||
            response.StartsWith("Topic:", StringComparison.OrdinalIgnoreCase) ||
            IsEchoOfQuestion(response, question))
        {
            // Remove the unanswered question to keep conversation log clean
            conversationLog.RemoveAt(conversationLog.Count - 1);
            return null;
        }

        // Clean the value before adding to conversation log
        var cleaned = CleanValue(response);
        conversationLog.Add(new LLMMessage { Role = LLMRole.Assistant, Content = cleaned });
        return response;
    }

    private static string BuildConstraintHint(string fieldType, List<string>? enumValues)
    {
        if (enumValues is { Count: > 0 })
            return $", choose one: {string.Join(", ", enumValues)}";

        return fieldType switch
        {
            "boolean" => ", true or false",
            "number" => ", a number",
            "date" => ", YYYY-MM-DD format",
            "short_text" => ", short text, max 10 words",
            "slug" => ", lowercase with hyphens, no spaces",
            "html" => ", HTML content",
            "text_long" => ", a few paragraphs",
            _ => ", one or two sentences"
        };
    }

    /// <summary>
    /// Completes with a short token budget — for titles, selects, numbers, dates, booleans.
    /// </summary>
    private static async Task<string?> CompleteShortAsync(LLMMessage[] prompt, CancellationToken cancellationToken)
    {
        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var request = new LLMRequest
        {
            Messages = prompt,
            MaxTokens = Math.Min(aiConfig.MaxLightCompletionTokens, 64),
            Temperature = aiConfig.Temperature
        };

        var result = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
        if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.Data.Content))
            return null;

        var truncated = TruncateAtContinuation(result.Data.Content.Trim());
        // Short responses should be single-line; take first non-empty line
        return truncated.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
    }

    /// <summary>
    /// Completes with a larger token budget — for HTML/WYSIWYG and long text fields.
    /// </summary>
    private static async Task<string?> CompleteLongAsync(LLMMessage[] prompt, CancellationToken cancellationToken)
    {
        var aiConfig = RfConfiguration.AiServiceConfiguration!;
        var request = new LLMRequest
        {
            Messages = prompt,
            MaxTokens = aiConfig.MaxCompletionTokens,
            Temperature = aiConfig.Temperature
        };

        var result = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
        if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.Data.Content))
            return null;

        var output = TruncateAtContinuation(result.Data.Content.Trim());

        // Some models prepend "Title: ...", "Introduction:", etc. — strip header-like lines
        for (var pass = 0; pass < 3; pass++)
        {
            var firstNewline = output.IndexOf('\n');
            if (firstNewline <= 0 || firstNewline >= 120) break;
            var firstLine = output[..firstNewline].Trim();
            if (!firstLine.Contains(':') || firstLine.Contains("://")) break;
            var afterFirst = output[(firstNewline + 1)..].TrimStart();
            if (afterFirst.Length <= 50) break;
            output = afterFirst;
        }

        return output;
    }

    /// <summary>
    /// Some models generate past their answer, producing fake "User:" or
    /// "Question:" continuations, or fall into repetition loops. Truncate at the first marker
    /// or when a repeated block is detected.
    /// </summary>
    internal static string TruncateAtContinuation(string response)
    {
        var lines = response.Split('\n');
        var sb = new System.Text.StringBuilder();
        var seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastSafeLength = 0; // sb.Length at last confirmed-unique substantial line

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Stop at fake conversation continuations
            if (trimmed.StartsWith("User:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Question:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase))
                break;

            // For non-empty lines, check for repetition loops
            if (trimmed.Length > 2)
            {
                // Check for exact-line repetition
                if (!seenLines.Add(trimmed))
                {
                    // Duplicate detected — truncate back to before any label lines that preceded it
                    var truncated = sb.ToString(0, lastSafeLength).Trim();
                    return truncated.Length > 0 ? truncated : response.Split('\n')[0].Trim();
                }

                // Check for prefix-pattern repetition (e.g. "Title: X" then "Title: Y")
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx > 0 && colonIdx < 20)
                {
                    var prefix = trimmed[..colonIdx].Trim();
                    if (prefix.Length > 0 && prefix.All(c => char.IsLetter(c) || c == ' ') &&
                        !seenPrefixes.Add(prefix))
                    {
                        var truncated = sb.ToString(0, lastSafeLength).Trim();
                        return truncated.Length > 0 ? truncated : response.Split('\n')[0].Trim();
                    }
                }
            }

            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);

            // Update safe position only after substantial unique lines that aren't labels/headers
            if (trimmed.Length > 5 && !trimmed.EndsWith(':'))
                lastSafeLength = sb.Length;
        }
        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : response.Split('\n')[0].Trim();
    }

    // ════════════════════════════════════════════════════════════════
    // Value parsing and cleanup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cleans up LLM output: removes surrounding quotes, labels, markdown fences.
    /// </summary>
    internal static string CleanValue(string raw)
    {
        var v = raw.Trim();

        // Remove markdown code fences
        if (v.StartsWith("```") && v.EndsWith("```"))
        {
            v = v[3..^3];
            var newline = v.IndexOf('\n');
            if (newline >= 0 && newline < 20) // strip language hint like ```html
                v = v[(newline + 1)..];
            v = v.Trim();
        }

        // Detect JSON objects/arrays — LLMs sometimes return structured JSON instead of prose.
        // Try to extract a meaningful text value from the JSON; fall back to raw if extraction fails.
        if ((v.StartsWith('{') && v.EndsWith('}')) || (v.StartsWith('[') && v.EndsWith(']')))
        {
            try
            {
                var token = JToken.Parse(v);
                // Try common patterns: {"description": "..."}, {"title": "...", "description": "..."}
                if (token is JObject obj)
                {
                    var textValue = obj["description"]?.Value<string>()
                                 ?? obj["content"]?.Value<string>()
                                 ?? obj["text"]?.Value<string>()
                                 ?? obj["value"]?.Value<string>()
                                 ?? obj["title"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(textValue))
                        v = textValue.Trim();
                }
                else if (token is JArray arr && arr.Count > 0 && arr[0] is JObject firstObj)
                {
                    var textValue = firstObj["description"]?.Value<string>()
                                 ?? firstObj["content"]?.Value<string>()
                                 ?? firstObj["text"]?.Value<string>()
                                 ?? firstObj["title"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(textValue))
                        v = textValue.Trim();
                }
            }
            catch
            {
                // Not valid JSON — continue with normal cleaning
            }
        }

        // Remove surrounding quotes (loop to handle multiple layers)
        while (v.Length > 2 && ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\''))))
            v = v[1..^1].Trim();
        // Strip instruction-echo patterns: "The X should be Y" → Y
        var shouldBeIdx = v.IndexOf("should be ", StringComparison.OrdinalIgnoreCase);
        if (shouldBeIdx >= 0 && shouldBeIdx < 60)
        {
            var afterShouldBe = v[(shouldBeIdx + "should be ".Length)..].Trim();
            if (afterShouldBe.Length > 0)
                v = afterShouldBe;
        }

        // Strip quotes again after echo/label stripping
        while (v.Length > 2)
        {
            if ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\''))
                v = v[1..^1].Trim();
            else break;
        }
        // Remove common label prefixes like "Title: value" but not URLs or timestamps
        var colonIdx = v.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 25 && !v.Contains('\n'))
        {
            var prefix = v[..colonIdx].Trim().ToLowerInvariant();
            var afterColon = v[(colonIdx + 1)..].TrimStart();
            // Only strip if prefix is pure letters/spaces (not "http", "https", or digits like "10")
            if (prefix.All(c => char.IsLetter(c) || c == ' ') &&
                prefix.Split(' ').Length <= 4 &&
                prefix is not ("http" or "https") &&
                afterColon.Length > 0 && !afterColon.StartsWith("//"))
            {
                v = afterColon;
            }
        }

        // Second pass: strip quotes that were inside a label prefix (e.g. 'Title: "value"')
        while (v.Length > 2 && ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\''))))
            v = v[1..^1].Trim();

        return v;
    }

    /// <summary>
    /// Extracts the first integer found in a string.
    /// </summary>
    private static string ExtractFirstNumber(string text)
    {
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                if (start == -1) start = i;
            }
            else if (start >= 0)
            {
                return text[start..i];
            }
        }
        return start >= 0 ? text[start..] : "";
    }

    /// <summary>
    /// Returns today's date formatted according to the field's configured date format.
    /// Falls back to yyyy-MM-dd if the field has no date options.
    /// </summary>
    private static string FormatTodayForDateField(FieldSchema field)
    {
        var fmt = field.DateOptions?.Format;
        if (!string.IsNullOrEmpty(fmt))
        {
            try { return DateTime.UtcNow.ToString(fmt); }
            catch { /* invalid format string — fall through */ }
        }
        return DateTime.UtcNow.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Parses LLM plain-text output into the appropriate JToken for the field type.
    /// </summary>
    internal static JToken? ParseFieldValue(FieldSchema field, string value)
    {
        switch (field.Type)
        {
            case FieldSchemaType.Text:
            case FieldSchemaType.Email:
            case FieldSchemaType.Url:
                // For short text fields, take only the first line
                var firstLine = value.Split('\n', 2)[0].Trim();
                return string.IsNullOrEmpty(firstLine) ? null : firstLine;

            case FieldSchemaType.TextArea:
            case FieldSchemaType.WysiwygEditor:
                return value;

            case FieldSchemaType.Number:
            case FieldSchemaType.Range:
                var numStr = ExtractFirstNumber(value);
                if (double.TryParse(numStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var num))
                    return num;
                // Try parsing the whole value as a fallback
                if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var num2))
                    return num2;
                return null;

            case FieldSchemaType.Checkbox:
                var lower = value.ToLowerInvariant().Trim();
                if (lower.StartsWith("true") || lower.StartsWith("yes") || lower == "1"
                    || lower.Contains("enabled") || lower.Contains("allow"))
                    return true;
                if (lower.StartsWith("false") || lower.StartsWith("no") || lower == "0"
                    || lower.Contains("disabled") || lower.Contains("disallow"))
                    return false;
                return null;

            case FieldSchemaType.DatePicker:
                // Extract a date-like pattern and reformat to the field's configured format
                var targetFmt = field.DateOptions?.Format ?? "yyyy-MM-dd";
                var dateMatchDashed = System.Text.RegularExpressions.Regex.Match(value, @"\d{4}-\d{2}-\d{2}");
                if (dateMatchDashed.Success && DateTime.TryParseExact(dateMatchDashed.Value, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDashed))
                {
                    try { return parsedDashed.ToString(targetFmt); } catch { return dateMatchDashed.Value; }
                }
                var dateMatchCompact = System.Text.RegularExpressions.Regex.Match(value, @"(?<!\d)(\d{8})(?!\d)");
                if (dateMatchCompact.Success && DateTime.TryParseExact(dateMatchCompact.Groups[1].Value, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedCompact))
                {
                    try { return parsedCompact.ToString(targetFmt); } catch { return dateMatchCompact.Groups[1].Value; }
                }
                // Try general date parsing as a last resort
                if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsedGeneral))
                {
                    try { return parsedGeneral.ToString(targetFmt); } catch { return null; }
                }
                // CJK date patterns: 2023年10月1日, 2023年10月01日
                var cjkMatch = System.Text.RegularExpressions.Regex.Match(value, @"(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日?");
                if (cjkMatch.Success)
                {
                    try
                    {
                        var dt = new DateTime(int.Parse(cjkMatch.Groups[1].Value),
                            int.Parse(cjkMatch.Groups[2].Value), int.Parse(cjkMatch.Groups[3].Value));
                        return dt.ToString(targetFmt);
                    }
                    catch { /* fall through */ }
                }
                // Try en-US culture for natural language dates ("October 1, 2023")
                if (DateTime.TryParse(value, new System.Globalization.CultureInfo("en-US"),
                    System.Globalization.DateTimeStyles.None, out var parsedEnUs))
                {
                    try { return parsedEnUs.ToString(targetFmt); } catch { return null; }
                }
                return null;

            case FieldSchemaType.Select when field.SelectOptions?.AllowMultiple == true:
                var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var arr = new JArray();
                foreach (var v in values)
                {
                    if (field.SelectOptions?.Choices is { Count: > 0 })
                    {
                        var match = field.SelectOptions.Choices.FirstOrDefault(c =>
                            c.Value.Equals(v, StringComparison.OrdinalIgnoreCase));
                        if (match != null) arr.Add(match.Value);
                    }
                    else arr.Add(v);
                }
                return arr.Count > 0 ? arr : null;

            case FieldSchemaType.Select:
                if (field.SelectOptions?.Choices is { Count: > 0 })
                {
                    // Try exact match first
                    var exact = field.SelectOptions.Choices.FirstOrDefault(c =>
                        c.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (exact != null) return exact.Value;

                    // Try contains match (LLM might add extra text)
                    var contains = field.SelectOptions.Choices.FirstOrDefault(c =>
                        value.Contains(c.Value, StringComparison.OrdinalIgnoreCase));
                    if (contains != null) return contains.Value;

                    // Try label match
                    var labelMatch = field.SelectOptions.Choices.FirstOrDefault(c =>
                        c.Label != null && value.Contains(c.Label, StringComparison.OrdinalIgnoreCase));
                    if (labelMatch != null) return labelMatch.Value;

                    // Fuzzy: normalize separators (short-term ↔ short_term ↔ short term)
                    var normalized = NormalizeSeparators(value);
                    var fuzzy = field.SelectOptions.Choices.FirstOrDefault(c =>
                        normalized.Contains(NormalizeSeparators(c.Value), StringComparison.OrdinalIgnoreCase)
                        || (c.Label != null && normalized.Contains(NormalizeSeparators(c.Label), StringComparison.OrdinalIgnoreCase)));
                    if (fuzzy != null) return fuzzy.Value;

                    return null; // Don't use invalid enum values
                }
                return value;

            default:
                return value;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Schema helpers
    // ════════════════════════════════════════════════════════════════

    private static string BuildFieldDescription(FieldSchema field)
    {
        var desc = field.Label;
        if (!string.IsNullOrEmpty(field.Instructions))
            desc += $" — {StripHtmlTags(field.Instructions)}";
        return desc;
    }

    private static string GetSimpleTypeName(FieldSchema field)
    {
        return field.Type switch
        {
            FieldSchemaType.WysiwygEditor => "html",
            FieldSchemaType.TextArea => "text_long",
            FieldSchemaType.Number or FieldSchemaType.Range => "number",
            FieldSchemaType.Checkbox => "boolean",
            FieldSchemaType.DatePicker => "date",
            FieldSchemaType.Text when IsSlugField(field.Name) => "slug",
            FieldSchemaType.Text => "short_text",
            FieldSchemaType.Email => "short_text",
            FieldSchemaType.Url => "short_text",
            FieldSchemaType.Select => "enum",
            _ => "string"
        };
    }

    private static bool IsSlugField(string fieldName)
    {
        var lower = fieldName.ToLowerInvariant();
        return lower is "slug" or "url_slug" or "url-slug" || lower.Contains("slug");
    }

    /// <summary>
    /// Evaluates simple display conditions — delegates to the centralized implementation.
    /// </summary>
    private static bool IsDisplayConditionMet(string condition, JObject currentValues)
        => AiEntityGeneratorValidator.IsDisplayConditionMet(condition, currentValues);

    private static List<string>? GetEnumValues(FieldSchema field)
    {
        if (field.Type != FieldSchemaType.Select || field.SelectOptions?.Choices is not { Count: > 0 })
            return null;
        return field.SelectOptions.Choices.Select(c => c.Value).ToList();
    }

    // ════════════════════════════════════════════════════════════════
    // Post-processing: derive computable fields, clean fabricated values
    // ════════════════════════════════════════════════════════════════

    internal static void PostProcessFields(JObject result, List<FieldSchema> fields)
    {
        var title = result["title"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(title)) return;

        // Find first substantial content value (WYSIWYG or TextArea, not just title echo)
        string? contentText = null;
        foreach (var f in fields)
        {
            if (f.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea
                && result[f.Name] is { Type: JTokenType.String } token)
            {
                var val = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(val) && val.Length > title.Length)
                {
                    contentText = val;
                    break;
                }
            }
        }

        foreach (var f in fields)
        {
            // Derive slug from title
            if (f.Name.Contains("slug", StringComparison.OrdinalIgnoreCase) && f.Type == FieldSchemaType.Text)
            {
                result[f.Name] = Slugify(title);
                continue;
            }

            // Always derive excerpt from content (LLM output for excerpt is unreliable)
            if (f.Type == FieldSchemaType.TextArea
                && f.Name.Contains("excerpt", StringComparison.OrdinalIgnoreCase)
                && contentText != null)
            {
                var plain = StripHtmlTags(contentText);
                result[f.Name] = plain.Length > 200 ? plain[..200].TrimEnd() + "..." : plain;
                continue;
            }

            // Fix fields that just echo the title
            if (result[f.Name] is JValue { Type: JTokenType.String } jv)
            {
                var val = jv.Value<string>();
                if (IsEchoOfTitle(val, title))
                {
                    // Remove fabricated URLs (echo of title is never a valid URL)
                    if (f.Type == FieldSchemaType.Url)
                        result.Remove(f.Name);
                }
            }

            // Clean URL fields that don't look like real URLs
            if (f.Type == FieldSchemaType.Url && result[f.Name] is JValue { Type: JTokenType.String } urlVal)
            {
                var url = urlVal.Value<string>();
                if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    result.Remove(f.Name);
            }

            // Compute reading time from content word count
            if (f.Type is FieldSchemaType.Number or FieldSchemaType.Range
                && f.Name.Contains("reading_time", StringComparison.OrdinalIgnoreCase)
                && contentText != null)
            {
                var wordCount = contentText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                result[f.Name] = Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));
            }

            // Process groups (derive SEO fields, clean nested URLs)
            if (f.Type == FieldSchemaType.Group && f.GroupOptions?.ChildSchema != null
                && result[f.Name] is JObject groupObj)
            {
                PostProcessGroup(groupObj, f.GroupOptions.ChildSchema, title, contentText);
            }

            // Clean repeater items with fabricated URLs
            if (f.Type == FieldSchemaType.Repeater && f.RepeaterOptions?.ItemSchema != null
                && result[f.Name] is JArray arr)
            {
                CleanRepeaterUrls(arr, f.RepeaterOptions.ItemSchema);
            }

            // Safety net: ensure DatePicker fields are never null — use today's date
            if (f.Type == FieldSchemaType.DatePicker
                && result.ContainsKey(f.Name) && result[f.Name]!.Type == JTokenType.Null)
            {
                result[f.Name] = FormatTodayForDateField(f);
            }
        }
    }

    private static void PostProcessGroup(
        JObject groupObj, List<FieldSchema> childSchema, string title, string? contentText)
    {
        var plainContent = contentText != null ? StripHtmlTags(contentText) : null;
        var excerpt = plainContent is { Length: > 160 } ? plainContent[..160].TrimEnd() + "..." : plainContent ?? title;

        foreach (var f in childSchema)
        {
            // Always derive SEO fields from title/content — LLM output is unreliable for these
            if (f.Name.Contains("title", StringComparison.OrdinalIgnoreCase) && f.Type == FieldSchemaType.Text)
            {
                groupObj[f.Name] = title;
                continue;
            }
            if (f.Name.Contains("description", StringComparison.OrdinalIgnoreCase)
                && f.Type is FieldSchemaType.TextArea or FieldSchemaType.Text)
            {
                groupObj[f.Name] = excerpt;
                continue;
            }
            if (f.Name.Contains("keyword", StringComparison.OrdinalIgnoreCase) && f.Type == FieldSchemaType.Text)
            {
                var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
                      "of", "with", "by", "from", "is", "it", "as", "be", "was", "are", "this", "that" };
                groupObj[f.Name] = string.Join(", ", title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 2 && !stopWords.Contains(w))
                    .Select(w => w.ToLowerInvariant().Trim(',', '.', '!', '?', ';', ':')));
                continue;
            }

            // Remove all URL fields in SEO groups — LLM fabricates URLs
            if (f.Type == FieldSchemaType.Url)
                groupObj.Remove(f.Name);
        }
    }

    private static void CleanRepeaterUrls(JArray arr, List<FieldSchema> itemSchema)
    {
        var urlFields = itemSchema.Where(f => f.Type == FieldSchemaType.Url).Select(f => f.Name).ToList();
        if (urlFields.Count == 0) return;

        var toRemove = new List<JToken>();
        foreach (var item in arr)
        {
            if (item is not JObject itemObj) continue;
            var hasValidUrl = false;
            foreach (var urlField in urlFields)
            {
                var url = itemObj[urlField]?.Value<string>();
                if (!string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    hasValidUrl = true;
                }
                else
                {
                    itemObj.Remove(urlField);
                }
            }
            // Remove items that only had URL + title but URL was fabricated
            if (!hasValidUrl && itemObj.Count <= 1)
                toRemove.Add(item);
        }
        foreach (var item in toRemove)
            arr.Remove(item);
    }

    internal static bool IsEchoOfTitle(string? value, string title)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        // Exact or near-exact match
        if (v.Equals(title, StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Replace("\"", "").Trim().Equals(title, StringComparison.OrdinalIgnoreCase)) return true;
        // Contains the entire title
        if (v.Contains(title, StringComparison.OrdinalIgnoreCase) && v.Length < title.Length + 30) return true;
        return false;
    }

    /// <summary>
    /// Detects values that are JSON objects or arrays — these should never appear in content/text fields.
    /// CleanValue tries to extract a text value from JSON, but if the result still looks like JSON, reject it.
    /// </summary>
    internal static bool LooksLikeJsonStructure(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length < 2) return false;
        if ((trimmed[0] == '{' && trimmed[^1] == '}') || (trimmed[0] == '[' && trimmed[^1] == ']'))
        {
            try { JToken.Parse(trimmed); return true; }
            catch { return false; }
        }
        return false;
    }

    /// <summary>
    /// Detects when SmolLM2 echoes back the question as its answer.
    /// Compares the response to the question text — if the response contains most of the question
    /// or starts with the field name pattern, it's an echo.
    /// </summary>
    internal static bool IsEchoOfQuestion(string response, string question)
    {
        var r = response.Trim();
        var q = question.Trim();

        // Direct echo of the full question or substantial portion
        if (r.Equals(q, StringComparison.OrdinalIgnoreCase)) return true;

        // Response contains a substantial portion of the question
        if (q.Length > 20 && r.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;

        // Check if the response is a paraphrase of the question description
        // Split question into significant words and check if response contains most of them
        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.Trim(',', '.', '(', ')', ':').ToLowerInvariant())
            .Where(w => w.Length > 3)
            .ToArray();
        if (qWords.Length >= 4)
        {
            var rLower = r.ToLowerInvariant();
            var matchCount = qWords.Count(w => rLower.Contains(w));
            if (matchCount >= qWords.Length * 0.7) return true;
        }

        // Response starts with the field name followed by = or (
        var firstWord = r.Split(' ', 2)[0].TrimEnd('(', '=', ':');
        if (firstWord.Length > 2)
        {
            var qFirstWord = q.Split(' ', 2)[0].TrimEnd('(', '=', ':');
            if (firstWord.Equals(qFirstWord, StringComparison.OrdinalIgnoreCase)
                && (r.Contains('(') || r.Contains('=') || r.Contains(':'))) return true;
        }

        // Response echoes a date question format ("A date (YYYY-MM-DD) for:")
        if (q.StartsWith("A date", StringComparison.OrdinalIgnoreCase)
            && r.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    internal static string Slugify(string text)
    {
        var slug = text.ToLowerInvariant().Replace("'", "").Replace("\"", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
        return slug.Trim('-');
    }

    /// <summary>Normalize hyphens, underscores, and spaces to a single space for fuzzy comparison.</summary>
    private static string NormalizeSeparators(string text) => text.Replace('_', ' ').Replace('-', ' ');

    private static string StripHtmlTags(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", " ").Trim();
    }
}
