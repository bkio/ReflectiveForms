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
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// POST /rf/api/ai/suggest
///
/// AI-powered field suggestions based on [AISuggestion] attribute.
/// </summary>
internal class AiSuggestFieldEndpoint : BaseEndpoint
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
        var targetField = body["target_field"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(targetField))
            return HttpStatusCode.BadRequest.ToResult("'target_field' is required.");

        var currentFields = (body["fields"] ?? body["current_fields"]) as JObject ?? new JObject();

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out _))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("UPDATE", entityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

        var suggestion = await AiFieldSuggestionHandler.SuggestAsync(entityName, targetField, currentFields, cancellationToken);
        if (suggestion == null)
            return HttpStatusCode.BadRequest.ToResult($"Field '{targetField}' does not have an [AISuggestion] attribute or suggestion failed.");

        var response = new JObject { ["suggestion"] = suggestion };
        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }
}
