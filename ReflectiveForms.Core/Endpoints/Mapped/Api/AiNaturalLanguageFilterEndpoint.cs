// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
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
/// POST /rf/api/ai/nl_filter
///
/// Translates natural language into structured filter conditions and executes the query.
/// </summary>
internal class AiNaturalLanguageFilterEndpoint : BaseEndpoint
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
        var query = body["query"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(query))
            return HttpStatusCode.BadRequest.ToResult("'query' is required.");

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        if (!configBase.EntityConfiguration.SupportsNaturalLanguageFilter)
            return HttpStatusCode.BadRequest.ToResult($"Entity type '{entityName}' does not support natural language filtering.");

        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("PEEK_ALL", entityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

        var filterResult = await AiNaturalLanguageFilterHandler.FilterAsync(entityName, query, cancellationToken);
        if (filterResult == null)
            return HttpStatusCode.InternalServerError.ToResult("Failed to process natural language filter.");

        // Post-filter for sharing if needed
        var filteredResults = new JArray();
        foreach (var item in filterResult.Results)
        {
            if (configBase.EntityConfiguration.HasIndividualSharing)
            {
                var accessLevel = GetEntitySharingAccessLevel(entityName, item, RequesterUser.NotNull());
                if (accessLevel == SharingAccessLevel.None)
                    continue;
            }

            filteredResults.Add(new JObject
            {
                ["id"] = item[EntityModelAttributes.Id]?.Value<int>(),
                ["title"] = item[EntityModelAttributes.Title]?[EntityModelAttributes.TitleRendered]?.Value<string>(),
                ["modified_gmt"] = item[EntityModelAttributes.ModifiedGmt]?.Value<string>()
            });
        }

        var interpretedArray = new JArray();
        foreach (var f in filterResult.InterpretedFilters)
        {
            interpretedArray.Add(new JObject
            {
                ["field"] = f.Field,
                ["operator"] = f.Operator,
                ["value"] = f.Value
            });
        }

        var response = new JObject
        {
            ["interpreted_filters"] = interpretedArray,
            ["combination"] = filterResult.Combination,
            ["natural_language_interpretation"] = filterResult.NaturalLanguageInterpretation,
            ["results"] = filteredResults,
            ["used_vector_fallback"] = filterResult.UsedVectorFallback
        };

        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }
}
