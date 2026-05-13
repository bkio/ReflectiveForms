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

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Embeds entity text and upserts/deletes vector points.
/// Called from EntityRepositoryService save/delete hooks (best-effort).
/// </summary>
internal static class AiVectorIndexer
{
    internal static string GetCollectionName(string entityName) => $"rf_semantic_{entityName}";

    /// <summary>
    /// Extract text from an entity, embed it directly, and upsert the vector point.
    /// Raw extracted text is embedded into the vector for accurate similarity matching.
    /// An LLM-generated summary is stored in metadata for display in search results.
    /// Returns true if the vector was successfully upserted, false otherwise.
    /// </summary>
    internal static async Task<bool> IndexEntityAsync(string entityName, int entityId, JObject entity, CancellationToken cancellationToken)
    {
        var text = AiTextExtractor.ExtractText(entityName, entity);
        if (string.IsNullOrWhiteSpace(text))
            return false; // No embeddable text — skip silently

        var title = entity[EntityModelAttributes.Title]?[EntityModelAttributes.TitleRendered]?.Value<string>() ?? "";
        var modifiedGmt = entity[EntityModelAttributes.ModifiedGmt]?.Value<string>() ?? DateTime.UtcNow.ToString("o");

        // Embed the raw extracted text directly.
        // Truncate to stay within typical embedding model context limits.
        var textToEmbed = text;
        if (textToEmbed.Length > 8000)
            textToEmbed = textToEmbed[..8000];

        var embeddingResult = await AiConfiguration.EmbeddingLlmService.CreateEmbeddingAsync(textToEmbed, cancellationToken);
        if (!embeddingResult.IsSuccessful)
        {
            RfConfiguration.LogError($"AiVectorIndexer: Failed to create embedding for {entityName}/{entityId}: {embeddingResult.ErrorMessage}");
            return false;
        }

        // Generate an LLM summary for display in search results (best-effort).
        // Falls back to a truncated version of the raw text if summarization fails.
        var summary = await SummarizeEntityAsync(entityName, text, cancellationToken) ?? Truncate(text, 300);

        var metadata = new JObject
        {
            ["entity_id"] = entityId,
            ["entity_name"] = entityName,
            ["title"] = title,
            ["summary"] = summary,
            ["modified_gmt"] = modifiedGmt,
            ["indexed_at"] = DateTime.UtcNow.ToString("o")
        };

        var point = new VectorPoint
        {
            Id = entityId.ToString(),
            Vector = embeddingResult.Data,
            Metadata = metadata
        };

        var upsertResult = await AiConfiguration.VectorService.UpsertAsync(
            GetCollectionName(entityName), point, cancellationToken);
        if (!upsertResult.IsSuccessful)
        {
            RfConfiguration.LogError($"AiVectorIndexer: Failed to upsert vector for {entityName}/{entityId}: {upsertResult.ErrorMessage}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Delete the vector point for a deleted entity.
    /// </summary>
    internal static async Task DeleteEntityAsync(string entityName, int entityId)
    {
        var deleteResult = await AiConfiguration.VectorService.DeleteAsync(
            GetCollectionName(entityName), entityId.ToString());
        if (!deleteResult.IsSuccessful)
        {
            RfConfiguration.LogError($"AiVectorIndexer: Failed to delete vector for {entityName}/{entityId}: {deleteResult.ErrorMessage}");
        }
    }

    /// <summary>
    /// Re-index all entities of a given type. Used for bulk operations (migration, model change).
    /// </summary>
    internal static async Task ReIndexAsync(string entityName, string mode, CancellationToken cancellationToken)
    {
        var collectionName = GetCollectionName(entityName);

        var allEntities = await AiConfiguration.DatabaseService.ScanTableAsync(
            EntityRepositoryService.GetEntityTableName(entityName), cancellationToken);
        if (!allEntities.IsSuccessful)
        {
            RfConfiguration.LogError($"AiVectorIndexer.ReIndexAsync: ScanTableAsync failed for {entityName}: {allEntities.ErrorMessage}");
            return;
        }

        foreach (var entity in allEntities.Data.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityId = (int)(long)entity[EntityModelAttributes.Id].NotNull();

            if (mode == "incremental")
            {
                // Check if vector exists and is up-to-date
                var vectorPoint = await AiConfiguration.VectorService.GetAsync(
                    collectionName, entityId.ToString(),
                    includeVector: false, includeMetadata: true, cancellationToken);

                if (vectorPoint.IsSuccessful && vectorPoint.Data != null)
                {
                    var indexedAt = vectorPoint.Data.Metadata?["indexed_at"]?.Value<DateTime>();
                    var modifiedGmt = entity[EntityModelAttributes.ModifiedGmt]?.Value<DateTime>();
                    if (indexedAt != null && modifiedGmt != null && modifiedGmt <= indexedAt)
                        continue; // Up-to-date — skip
                }
            }

            await IndexEntityAsync(entityName, entityId, entity, cancellationToken);
        }
    }

    /// <summary>
    /// Ask the light LLM to produce a short summary for display in search results.
    /// Returns null if summarization fails (caller falls back to truncated raw text).
    /// </summary>
    private static async Task<string?> SummarizeEntityAsync(string entityName, string extractedText, CancellationToken cancellationToken)
    {
        try
        {
            var truncatedInput = extractedText.Length > 4000 ? extractedText[..4000] : extractedText;

            var request = new LLMRequest
            {
                Messages =
                [
                    new LLMMessage
                    {
                        Role = LLMRole.System,
                        Content = RfConfiguration.AiServiceConfiguration!.SystemPromptPrefix +
                            "\nSummarize the following entity content in 1-2 concise sentences for search result display. " +
                            "Focus on the entity's key topic and purpose. Output ONLY the summary, nothing else."
                    },
                    new LLMMessage
                    {
                        Role = LLMRole.User,
                        Content = $"Entity type: {entityName}\n\n{truncatedInput}"
                    }
                ],
                MaxTokens = 128,
                Temperature = 0.3f
            };

            var result = await AiConfiguration.LightLlmService.CompleteAsync(request, cancellationToken);
            if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.Data.Content))
                return result.Data.Content.Trim();
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError($"AiVectorIndexer.SummarizeEntityAsync: {ex.Message}");
        }
        return null;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
