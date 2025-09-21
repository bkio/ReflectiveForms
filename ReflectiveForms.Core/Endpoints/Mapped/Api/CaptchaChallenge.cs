// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Services;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class CaptchaChallenge : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Get];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    public override bool IsAuthenticatedEndpoint() => false;

    protected override Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var (question, answer) = CaptchaService.GenerateMathCaptcha();

        CaptchaService.StoreCaptchaInSession(context, question, answer);

        return Task.FromResult(Results.Ok(new { question = question }));
    }
}
