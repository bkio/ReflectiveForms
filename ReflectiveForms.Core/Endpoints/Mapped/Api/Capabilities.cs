// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class Capabilities : BaseEndpoint
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
        var userFields = RequesterUser.NotNull().Fields;
        var result = new Dictionary<string, object>();

        foreach (var entityName in RfConfiguration.EntityNameToConfiguration.Keys)
        {
            result[entityName] = new
            {
                can_peek_all = userFields.CanUserDo("PEEK_ALL", entityName),
                can_read = userFields.CanUserDo("READ", entityName),
                can_create = userFields.CanUserDo("CREATE", entityName),
                can_update = userFields.CanUserDo("UPDATE", entityName),
                can_delete = userFields.CanUserDo("DELETE", entityName),
            };
        }

        return Task.FromResult(Results.Ok(result));
    }
}
