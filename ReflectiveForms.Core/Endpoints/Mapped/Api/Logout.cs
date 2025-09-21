// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class Logout : BaseEndpoint
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

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        // Sign out from cookie authentication
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Clear any authentication cookies
        context.Response.Cookies.Delete(RfConfiguration.EndpointConfiguration.AuthCookieName);

        return Results.Ok(new { message = "Logged out successfully" });
    }
}
