// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Schema;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// API endpoint that returns JSON schemas for entity types.
/// These schemas can be consumed by frontend applications to dynamically render forms.
///
/// GET /rf/api/schema?type={entityName}  - Get schema for a specific entity
/// GET /rf/api/schema                     - Get schemas for all entities
/// </summary>
internal class SchemaEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Get];
    }

    // Schema endpoint can be public - it only returns metadata, not actual data
    public override bool IsAuthenticatedEndpoint() => false;

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    protected override Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // Check if requesting a specific entity type
        if (request.Query.TryGetValue("type", out var typeValues) && typeValues.Count > 0)
        {
            var entityName = typeValues.ToString();
            if (string.IsNullOrWhiteSpace(entityName))
            {
                return Task.FromResult(HttpStatusCode.BadRequest.ToResult("Entity type parameter is required."));
            }

            var result = EntitySchemaGenerator.GenerateSchema(entityName);
            if (!result.IsSuccessful)
            {
                return Task.FromResult(result.StatusCode.ToResult(result.ErrorMessage));
            }

            var json = JsonConvert.SerializeObject(result.Data, Formatting.None);
            return Task.FromResult(Results.Content(json, "application/json"));
        }

        // Return all schemas
        var allResult = EntitySchemaGenerator.GenerateAllSchemas();
        if (!allResult.IsSuccessful)
        {
            return Task.FromResult(allResult.StatusCode.ToResult(allResult.ErrorMessage));
        }

        var allJson = JsonConvert.SerializeObject(allResult.Data, Formatting.None);
        return Task.FromResult(Results.Content(allJson, "application/json"));
    }
}
