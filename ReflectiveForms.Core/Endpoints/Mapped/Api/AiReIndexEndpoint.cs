// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints.Enums;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// POST /rf/api/ai/reindex?type={entityName}&amp;mode=full|incremental
///
/// Root user only. Re-indexes all entities of a given type in the vector DB.
/// </summary>
internal class AiReIndexEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods() => [RequestHttpVerb.Post];

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch() => RequestBodyType.NotRelevant;

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (RfConfiguration.AiServiceConfiguration == null)
            return HttpStatusCode.NotImplemented.ToResult("AI features are not configured.");

        if (!IsRequesterRootUser)
            return HttpStatusCode.Forbidden.ToResult("Only the root user can trigger re-indexing.");

        var request = context.Request;
        if (!request.TryGetTypeParameter(out var entityName, out var failedResult))
            return failedResult.NotNull();

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        if (!configBase.EntityConfiguration.SupportsSemanticSearch)
            return HttpStatusCode.BadRequest.ToResult($"Entity type '{entityName}' does not support semantic search.");

        var mode = request.Query.TryGetValue("mode", out var modeValues) ? modeValues.ToString() : "full";
        if (mode != "full" && mode != "incremental")
            return HttpStatusCode.BadRequest.ToResult("'mode' must be 'full' or 'incremental'.");

        // Concurrency guard — fail fast if another reindex is already running
        MemoryScopeMutex? mutex = null;
        using var reindexCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            mutex = await MemoryScopeMutex.CreateEntityScopeAsync(
                AiConfiguration.MemoryService,
                new MemoryScopeLambda("rf:ai:sync"),
                $"reindex-{entityName}",
                TimeSpan.FromMinutes(30),
                reindexCts.Token);
        }
        catch (OperationCanceledException)
        {
            return HttpStatusCode.Conflict.ToResult("Reindex already in progress for this entity type.");
        }

        await using (mutex!)
        {
            try
            {
                await AiVectorIndexer.ReIndexAsync(entityName, mode, cancellationToken);
                return HttpStatusCode.OK.ToResult("Reindex completed successfully.");
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499); // Client closed
            }
            catch (Exception ex)
            {
                RfConfiguration.LogError(ex);
                return HttpStatusCode.InternalServerError.ToResult($"Reindex failed: {ex.GetBaseException().Message}");
            }
        }
    }
}
