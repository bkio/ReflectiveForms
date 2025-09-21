// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class SanityCheck : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Post];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.JsonObject;
    }

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // Validate query params
        if (!request.TryGetTypeParameter(out var entityName, out var failedResult))
            return failedResult.NotNull();

        // Auth check
        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("UPDATE", entityName))
        {
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");
        }

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var finalConfiguration))
            return HttpStatusCode.NotFound.ToResult($"Entity -{entityName}- is not found.");

        var result = await finalConfiguration.UpsertSanityCheck((RequestBodyJsonObject.NotNull(), cancellationToken));
        return !result.IsSuccessful
            ? result.ErrorMessage.ToResult()
            : HttpStatusCode.OK.ToResult("The object has passed the sanity check.");
    }
}
