// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class AuthCheck : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Post];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    public override bool IsAuthenticatedEndpoint() => true;

    protected override Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(Results.Ok(new { authenticated = true }));
    }
}
