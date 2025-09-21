// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.PageGenerators;

namespace ReflectiveForms.Core.Endpoints.Mapped.Frontend;

internal class EntitiesAdmin : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Get];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // Validate query params
        if (!request.TryGetTypeParameter(out var entityName, out var failedResult))
            return failedResult.NotNull();

        int? id = null;
        if (request.Query.ContainsKey("id"))
        {
            if (!request.TryGetEntityIdParameter(out var idVal, out failedResult, false))
                return failedResult.NotNull();
            id = idVal;
        }

        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo(id == null ? "CREATE" : "UPDATE", entityName))
            return Results.Forbid();

        var generator = new ForSingleEntityEditForAdmin(entityName, id ?? -1, IsRequesterRootUser, RequesterUser.NotNull().Id);

        var generateResult = await generator.GenerateAsync(cancellationToken);

        return !generateResult.IsSuccessful
            ? generateResult.ErrorMessage.ToResult()
            : Results.Content(generateResult.Data, "text/html; charset=utf-8");
    }
}
