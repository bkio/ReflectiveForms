// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using static ReflectiveForms.Core.Endpoints.Mapped.Api.Crud;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// POST /rf/api/ai/semantic_search
///
/// Request: { "query": "...", "entity_name": "...", "top_k": 10 }
/// Response: { "results": [{ "entity_name": "...", "entity_id": 42, "title": "...", "score": 0.91 }] }
/// </summary>
internal class AiSemanticSearchEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods() => [RequestHttpVerb.Post];

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch() => RequestBodyType.JsonObject;

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (RfConfiguration.AiServiceConfiguration == null)
            return HttpStatusCode.NotImplemented.ToResult("AI features are not configured.");

        var body = RequestBodyJsonObject.NotNull();
        var query = body["query"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(query))
            return HttpStatusCode.BadRequest.ToResult("'query' is required.");

        var entityName = body["entity_name"]?.Value<string>();
        var topK = Math.Clamp(body["top_k"]?.Value<int>() ?? 10, 1, 100);

        var userFields = RequesterUser.NotNull().Fields;

        // Determine which entity types to search
        var targetEntities = new List<(string EntityName, EntityFinalConfigurationBase Config)>();

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            // Single entity search
            if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
                return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

            if (!configBase.EntityConfiguration.SupportsSemanticSearch)
                return HttpStatusCode.BadRequest.ToResult($"Entity type '{entityName}' does not support semantic search.");

            if (!userFields.CanUserDo("PEEK_ALL", entityName))
                return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

            targetEntities.Add((entityName, configBase));
        }
        else
        {
            // Cross-entity search: find all entity types where user has PEEK_ALL and semantic search is enabled
            foreach (var (name, configBase) in RfConfiguration.EntityNameToConfiguration)
            {
                if (configBase.EntityConfiguration.SupportsSemanticSearch &&
                    userFields.CanUserDo("PEEK_ALL", name))
                {
                    targetEntities.Add((name, configBase));
                }
            }
        }

        if (targetEntities.Count == 0)
            return Results.Json(new JObject { ["results"] = new JArray() }, statusCode: 200);

        // Query each collection and merge results by score
        var allResults = new List<(string EntityName, int EntityId, string Title, double Score)>();

        foreach (var (targetEntityName, config) in targetEntities)
        {
            var collectionName = AiVectorIndexer.GetCollectionName(targetEntityName);

            // Over-fetch for sharing filter
            var vectorResults = await AiConfiguration.VectorService.SemanticSearchAsync(
                AiConfiguration.LightLlmService, collectionName, query,
                topK: topK * 3, filter: null, includeMetadata: true, cancellationToken);

            if (!vectorResults.IsSuccessful || vectorResults.Data == null)
                continue;

            foreach (var candidate in vectorResults.Data)
            {
                var candidateEntityName = candidate.Metadata?["entity_name"]?.Value<string>() ?? targetEntityName;
                if (!int.TryParse(candidate.Id, out var candidateEntityId))
                    continue;

                // Verify entity still exists (orphan check)
                var exists = await AiConfiguration.DatabaseService.GetItemAsync(
                    candidateEntityName,
                    new DbKey(EntityModelAttributes.Id, candidateEntityId),
                    null,
                    cancellationToken);

                if (!exists.IsSuccessful || exists.Data == null)
                {
                    // Orphan vector — clean up silently
                    try { await AiVectorIndexer.DeleteEntityAsync(candidateEntityName, candidateEntityId); }
                    catch (Exception ex) { RfConfiguration.LogError(ex); }
                    continue;
                }

                // Per-entity sharing check (if applicable)
                if (config.EntityConfiguration.HasIndividualSharing)
                {
                    var accessLevel = Crud.GetEntitySharingAccessLevel(candidateEntityName, exists.Data, RequesterUser.NotNull());
                    if (accessLevel == SharingAccessLevel.None)
                        continue;
                }

                var title = candidate.Metadata?["title"]?.Value<string>() ?? "";

                // Hybrid scoring: boost results where the query appears in the title
                var score = candidate.Score;
                if (!string.IsNullOrWhiteSpace(title) && title.Contains(query, StringComparison.OrdinalIgnoreCase))
                    score = Math.Min(1.0f, score + 0.15f);

                allResults.Add((candidateEntityName, candidateEntityId, title, score));
            }
        }

        // Sort by score descending, take topK
        allResults.Sort((a, b) => b.Score.CompareTo(a.Score));
        var finalResults = allResults.Count > topK ? allResults.GetRange(0, topK) : allResults;

        var responseArray = new JArray();
        foreach (var (resEntityName, resEntityId, resTitle, resScore) in finalResults)
        {
            responseArray.Add(new JObject
            {
                ["entity_name"] = resEntityName,
                ["entity_id"] = resEntityId,
                ["title"] = resTitle,
                ["score"] = resScore
            });
        }

        var response = new JObject { ["results"] = responseArray };
        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }
}
