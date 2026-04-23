// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;

namespace ReflectiveForms.Core.Ai;

internal record RelationSuggestionResult(int Id, string? Title, double Score);

/// <summary>
/// Uses semantic search to suggest related entities for Relation fields.
/// Embeds the current entity's text and searches the target entity type's vector collection.
/// </summary>
internal static class AiRelationSuggestionHandler
{
    internal static async Task<List<RelationSuggestionResult>> SuggestAsync(
        string targetEntityName,
        string currentText,
        int topK,
        CancellationToken cancellationToken)
    {
        var results = new List<RelationSuggestionResult>();

        // Target entity must have semantic search enabled
        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(targetEntityName, out var targetConfig))
            return results;

        if (!targetConfig.EntityConfiguration.SupportsSemanticSearch)
            return results;

        var collectionName = $"rf_semantic_{targetEntityName}";

        // Use SemanticSearchAsync bridge extension (embed query + search in one call)
        var searchResult = await AiConfiguration.VectorService.SemanticSearchAsync(
            AiConfiguration.LightLlmService,
            collectionName,
            currentText,
            topK: topK * 2, // Over-fetch to compensate for potential orphans
            filter: null,
            includeMetadata: true,
            cancellationToken);

        if (!searchResult.IsSuccessful || searchResult.Data == null)
            return results;

        foreach (var candidate in searchResult.Data)
        {
            if (!int.TryParse(candidate.Id, out var entityId)) continue;

            // Verify entity still exists (orphan check)
            var exists = await AiConfiguration.DatabaseService.GetItemAsync(
                targetEntityName,
                new DbKey(EntityModelAttributes.Id, entityId),
                null, cancellationToken);

            if (!exists.IsSuccessful || exists.Data == null)
            {
                // Orphan — clean up (best-effort)
                try { await AiVectorIndexer.DeleteEntityAsync(targetEntityName, entityId); }
                catch (Exception ex) { RfConfiguration.LogError(ex); }
                continue;
            }

            var title = exists.Data[EntityModelAttributes.Title]?[EntityModelAttributes.TitleRendered]?.Value<string>();
            results.Add(new RelationSuggestionResult(entityId, title, candidate.Score));

            if (results.Count >= topK) break;
        }

        return results;
    }
}
