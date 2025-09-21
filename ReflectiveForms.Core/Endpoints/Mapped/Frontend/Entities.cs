// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.PageGenerators;

namespace ReflectiveForms.Core.Endpoints.Mapped.Frontend;

internal class Entities : BaseEndpoint
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
        // ReSharper disable once InvertIf
        if (request.Query.ContainsKey("id"))
        {
            if (!request.TryGetEntityIdParameter(out var idVal, out failedResult, false))
                return failedResult.NotNull();
            id = idVal;
        }

        return id == null
            ? await HandlePeekRequestAsync(context, entityName, cancellationToken)
            : await HandleGetRequest(entityName, id.Value, cancellationToken);
    }

    private async Task<IResult> HandleGetRequest(string entityName, int id, CancellationToken cancellationToken)
    {
        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("READ", entityName))
            return Results.Forbid();

        var generator = new ForSingleEntityView(entityName, id, IsRequesterRootUser);

        var generateResult = await generator.GenerateAsync(cancellationToken);

        return !generateResult.IsSuccessful
            ? generateResult.ErrorMessage.ToResult()
            : Results.Content(generateResult.Data, "text/html; charset=utf-8");
    }

    private async Task<IResult> HandlePeekRequestAsync(HttpContext context, string entityName, CancellationToken cancellationToken)
    {
        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("PEEK_ALL", entityName))
            return Results.Forbid();

        var query = context.Request.Query;

        // ShowOnlyByAuthor
        var showOnlyByAuthor = query.TryGetValue("show_only_by_author", out var authorValues)
                               && int.TryParse(authorValues.FirstOrDefault(), out var authorTmp)
            ? authorTmp
            : -1;

        // ShowOnlyByCategoryNames (nullable)
        var showOnlyByCategoryNames = query
            .Where(kvp => kvp.Key.StartsWith("show_only_by_category_"))
            .Select(kvp => kvp.Value.FirstOrDefault() ?? string.Empty)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        if (showOnlyByCategoryNames.Count == 0) showOnlyByCategoryNames = null;

        // ShowOnlyByTagNames (nullable)
        var showOnlyByTagNames = query
            .Where(kvp => kvp.Key.StartsWith("show_only_by_tag_"))
            .Select(kvp => kvp.Value.FirstOrDefault() ?? string.Empty)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        if (showOnlyByTagNames.Count == 0) showOnlyByTagNames = null;

        // SortBy (nullable)
        var sortBy = query.TryGetValue("sort_by", out var sortValues)
            ? sortValues.FirstOrDefault()
            : null;

        var generator = new ForAllEntitiesEditViewForAdmin(
            entityName,
            IsRequesterRootUser,
            userFields.CanUserDo("READ", entityName),
            userFields.CanUserDo("UPDATE", entityName),
            userFields.CanUserDo("CREATE", entityName),
            userFields.CanUserDo("DELETE", entityName),
            showOnlyByAuthor,
            showOnlyByCategoryNames.NotNull(),
            showOnlyByTagNames.NotNull(),
            sortBy);

        var generateResult = await generator.GenerateAsync(cancellationToken);

        return !generateResult.IsSuccessful
            ? generateResult.ErrorMessage.ToResult()
            : Results.Content(generateResult.Data, "text/html; charset=utf-8");
    }
}
