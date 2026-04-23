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
/// POST /rf/api/ai/sanity_check
///
/// Standalone AI sanity check for real-time frontend feedback.
/// </summary>
internal class AiSanityCheckEndpoint : BaseEndpoint
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
        var fieldName = body["field_name"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(fieldName))
            return HttpStatusCode.BadRequest.ToResult("'field_name' is required.");

        var fieldValue = body["field_value"];
        if (fieldValue == null || fieldValue.Type == JTokenType.Null)
            return HttpStatusCode.BadRequest.ToResult("'field_value' is required.");

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("UPDATE", entityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

        // Find [AISanityCheck] attributes for this field
        var fieldsModelType = configBase.EntityConfiguration.EntityFieldsModelType;
        var checks = AiAttributeHelper.FindAiSanityChecks(fieldsModelType, fieldName);
        if (checks.Count == 0)
            return HttpStatusCode.BadRequest.ToResult($"Field '{fieldName}' has no [AISanityCheck] attributes.");

        var results = await AiSanityCheckHandler.CheckFieldAsync(entityName, fieldName, fieldValue, checks, cancellationToken);

        var responseArray = new JArray();
        foreach (var r in results)
        {
            responseArray.Add(new JObject
            {
                ["check"] = r.Check,
                ["passed"] = r.Passed,
                ["severity"] = r.Severity.ToString(),
                ["message"] = r.Message
            });
        }

        var response = new JObject { ["results"] = responseArray };
        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }
}
