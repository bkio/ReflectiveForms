// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
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
/// POST /rf/api/ai/diff_summary
///
/// AI-powered revision diff summary. Fetches revisions server-side.
/// </summary>
internal class AiDiffSummaryEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods() => [RequestHttpVerb.Post];

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch() => RequestBodyType.JsonObject;

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (RfConfiguration.AiServiceConfiguration == null)
            return HttpStatusCode.NotImplemented.ToResult("AI features are not configured.");

        if (!context.Request.TryGetTypeParameter(out var entityName, out var failedResult))
            return failedResult!;

        var body = RequestBodyJsonObject.NotNull();
        var entityId = body["entity_id"]?.Value<int>();
        if (entityId == null)
            return HttpStatusCode.BadRequest.ToResult("'entity_id' is required.");

        var revisionIndex = body["revision_index"]?.Value<int>();
        if (revisionIndex == null)
            return HttpStatusCode.BadRequest.ToResult("'revision_index' is required.");

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        if (!configBase.EntityConfiguration.SupportsAiDiffSummary)
            return HttpStatusCode.BadRequest.ToResult($"Entity type '{entityName}' does not support AI diff summaries.");

        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("READ", entityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

        // Per-entity sharing check
        if (configBase.EntityConfiguration.HasIndividualSharing)
        {
            var entityResult = await AiConfiguration.DatabaseService.GetItemAsync(
                entityName,
                new DbKey(EntityModelAttributes.Id, entityId.Value),
                null, cancellationToken);

            if (!entityResult.IsSuccessful || entityResult.Data == null)
                return HttpStatusCode.NotFound.ToResult("Entity not found.");

            var accessLevel = GetEntitySharingAccessLevel(entityName, entityResult.Data, RequesterUser.NotNull());
            if (accessLevel == SharingAccessLevel.None)
                return HttpStatusCode.Forbidden.ToResult("User does not have access to this entity.");
        }

        var summary = await AiDiffSummaryHandler.SummarizeAsync(entityName, entityId.Value, revisionIndex.Value, cancellationToken);
        if (summary == null)
            return HttpStatusCode.InternalServerError.ToResult("Failed to generate diff summary.");

        var response = new JObject { ["summary"] = summary };
        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }
}
