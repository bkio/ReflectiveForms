// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Schema;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// API endpoint that returns an auto-generated OpenAPI 3.1 spec.
///
/// GET /rf/api/openapi.json
/// </summary>
internal class OpenApiEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Get];
    }

    /// <summary>
    /// Authentication is controlled by <see cref="Ai.OpenApiConfiguration.RequireAuthentication"/>.
    /// When false (default), the endpoint is public (same as /schema).
    /// When true, requires JwtOrCookie authentication.
    /// </summary>
    public override bool IsAuthenticatedEndpoint() =>
        RfConfiguration.EndpointConfiguration.OpenApi?.RequireAuthentication ?? false;

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    protected override Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var spec = OpenApiGenerator.Generate();
        var json = JsonConvert.SerializeObject(spec, Formatting.Indented);
        return Task.FromResult(Results.Content(json, "application/json"));
    }
}
