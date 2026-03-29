// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Repositories;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class Crud: BaseEndpoint
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
        if (!request.TryCrudOperationTypeParameter(out var operation, out failedResult))
            return failedResult.NotNull();

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var config))
            return HttpStatusCode.BadRequest.ToResult("Unknown entity type.");
        var crudMethodInfo = config.CrudMethodInfo;

        // Auth check — PEEK_ALL_PAGINATED uses same permissions as PEEK_ALL
        var authOperation = operation == "PEEK_ALL_PAGINATED" ? "PEEK_ALL" : operation;
        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo(authOperation, entityName))
        {
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");
        }

        var uid = EntityUpdaterIdentity.NormalUpdate(RequesterUser.NotNull().Id, userFields.EmailAddress);

        // Dispatch by operation
        return operation switch
        {
            "READ" => await HandleRead(entityName, cancellationToken),
            "PEEK_ALL" => await HandlePeekAll(entityName, cancellationToken),
            "PEEK_ALL_PAGINATED" => await HandlePeekAllPaginated(context.Request, entityName, cancellationToken),
            "CREATE" => await HandleCreate(entityName, crudMethodInfo, cancellationToken),
            "UPDATE" => await HandleUpdate(entityName, crudMethodInfo, uid, cancellationToken),
            "DELETE" => await HandleDelete(entityName, crudMethodInfo, cancellationToken),
            _ => HttpStatusCode.BadRequest.ToResult("Unknown operation.")
        };
    }

    private async Task<IResult> HandleRead(string entityName, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        var result = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
        return !result.IsSuccessful ? result.ErrorMessage.ToResult() : result.Data.ToResult();
    }

    private static async Task<IResult> HandlePeekAll(string entityName, CancellationToken cancellationToken)
    {
        var result = await RfConfiguration.RepositoryService.PeekAllAsync(entityName, cancellationToken);
        return !result.IsSuccessful ? result.ErrorMessage.ToResult() : result.Data.ToResult();
    }

    private static async Task<IResult> HandlePeekAllPaginated(HttpRequest request, string entityName, CancellationToken cancellationToken)
    {
        var pageSize = 20;
        if (request.Query.TryGetValue("page_size", out var pageSizeValues)
            && int.TryParse(pageSizeValues.ToString(), out var parsedPageSize)
            && parsedPageSize is > 0 and <= 100)
        {
            pageSize = parsedPageSize;
        }

        string? pageToken = null;
        if (request.Query.TryGetValue("page_token", out var pageTokenValues))
        {
            var tokenStr = pageTokenValues.ToString();
            if (!string.IsNullOrWhiteSpace(tokenStr))
                pageToken = tokenStr;
        }

        var result = await RfConfiguration.RepositoryService.PeekAllPaginatedAsync(
            entityName, pageSize, pageToken, cancellationToken);
        return !result.IsSuccessful
            ? result.StatusCode.ToResult(result.ErrorMessage)
            : result.Data.ToResult();
    }

    private async Task<IResult> HandleCreate(string entityName, CrudMethodInfo crudMethodInfo, CancellationToken cancellationToken)
    {
        var t = (Task<OperationResult<JObject>>)crudMethodInfo.PutOneAsyncMethodInfo.Invoke(RfConfiguration.RepositoryService, [
            entityName,
            RequestBodyJsonObject.NotNull(),
            cancellationToken]).NotNull();
        var result = await t.NotNull();
        return !result.IsSuccessful ? result.StatusCode.ToResult(result.ErrorMessage) : result.Data.ToResult();
    }

    private async Task<IResult> HandleUpdate(string entityName, CrudMethodInfo crudMethodInfo, EntityUpdaterIdentity uid, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        var t = (Task<OperationResult<JObject>>)crudMethodInfo.UpdateOneAsyncMethodInfo.Invoke(RfConfiguration.RepositoryService, [
            entityName,
            id,
            RequestBodyJsonObject.NotNull(),
            uid,
            cancellationToken]).NotNull();
        var result = await t.NotNull();
        return !result.IsSuccessful ? result.StatusCode.ToResult(result.ErrorMessage) : result.Data.ToResult();
    }

    private async Task<IResult> HandleDelete(string entityName, CrudMethodInfo crudMethodInfo, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        var t = (Task<OperationResult<JObject>>)crudMethodInfo.DeleteOneAsyncMethodInfo.Invoke(RfConfiguration.RepositoryService, [
            entityName,
            id,
            cancellationToken]).NotNull();
        var result = await t.NotNull();
        return !result.IsSuccessful ? result.ErrorMessage.ToResult() : result.Data.ToResult();
    }
}
