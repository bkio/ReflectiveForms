// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models;
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

        // Auth check — PEEK_ALL_PAGINATED uses same permissions as PEEK_ALL, HISTORY uses READ
        // SHARING_CANDIDATES requires UPDATE (only owners/editors configure sharing)
        var authOperation = operation switch
        {
            "PEEK_ALL_PAGINATED" => "PEEK_ALL",
            "HISTORY" => "READ",
            "SHARING_CANDIDATES" => "UPDATE",
            _ => operation
        };
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
            "HISTORY" => await HandleHistory(entityName, cancellationToken),
            "SHARING_CANDIDATES" => HandleSharingCandidates(entityName),
            _ => HttpStatusCode.BadRequest.ToResult("Unknown operation.")
        };
    }

    private async Task<IResult> HandleRead(string entityName, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        var result = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
        if (!result.IsSuccessful) return result.ErrorMessage.ToResult();

        if (entityName == RfReservedEntities.SheetsEntityName)
        {
            var access = GetEntitySharingAccessLevel(result.Data.NotNull(), RequesterUser.NotNull());
            if (access == SharingAccessLevel.None)
                return HttpStatusCode.Forbidden.ToResult("You do not have access to this sheet.");
            result.Data!["access_level"] = access.ToString().ToLowerInvariant();
        }

        return result.Data.ToResult();
    }

    private async Task<IResult> HandlePeekAll(string entityName, CancellationToken cancellationToken)
    {
        if (entityName == RfReservedEntities.SheetsEntityName)
            return await HandlePeekAllSheets(cancellationToken);

        var result = await RfConfiguration.RepositoryService.PeekAllAsync(entityName, cancellationToken);
        return !result.IsSuccessful ? result.ErrorMessage.ToResult() : result.Data.ToResult();
    }

    private async Task<IResult> HandlePeekAllPaginated(HttpRequest request, string entityName, CancellationToken cancellationToken)
    {
        if (entityName == RfReservedEntities.SheetsEntityName)
            return await HandlePeekAllSheets(cancellationToken);

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
        var body = RequestBodyJsonObject.NotNull();

        // Automatically set the author to the requesting user for entities with HasAuthor.
        // This ensures the creator is always recorded even if the client omits the field.
        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasAuthor
            && !body.ContainsKey(EntityModelAttributes.Author))
        {
            body[EntityModelAttributes.Author] = RequesterUser.NotNull().Id;
        }

        var t = (Task<OperationResult<JObject>>)crudMethodInfo.PutOneAsyncMethodInfo.Invoke(RfConfiguration.RepositoryService, [
            entityName,
            body,
            cancellationToken]).NotNull();
        var result = await t.NotNull();
        return !result.IsSuccessful ? result.StatusCode.ToResult(result.ErrorMessage) : result.Data.ToResult();
    }

    private async Task<IResult> HandleUpdate(string entityName, CrudMethodInfo crudMethodInfo, EntityUpdaterIdentity uid, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        if (entityName == RfReservedEntities.SheetsEntityName)
        {
            var existing = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
            if (!existing.IsSuccessful) return existing.StatusCode.ToResult(existing.ErrorMessage);

            var access = GetEntitySharingAccessLevel(existing.Data.NotNull(), RequesterUser.NotNull());
            if (access < SharingAccessLevel.Edit)
                return HttpStatusCode.Forbidden.ToResult("You do not have edit access to this sheet.");

            // Only the owner can change sharing settings
            if (access != SharingAccessLevel.Owner)
            {
                var body = RequestBodyJsonObject.NotNull();
                if (body.TryGetValue("fields", out var fieldsToken) && fieldsToken is JObject fieldsObj)
                {
                    fieldsObj.Remove("is_public");
                    fieldsObj.Remove("shared_users");
                    fieldsObj.Remove("shared_roles");
                }
            }
        }

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

        if (entityName == RfReservedEntities.SheetsEntityName)
        {
            var existing = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
            if (!existing.IsSuccessful) return existing.StatusCode.ToResult(existing.ErrorMessage);

            var access = GetEntitySharingAccessLevel(existing.Data.NotNull(), RequesterUser.NotNull());
            if (access != SharingAccessLevel.Owner)
                return HttpStatusCode.Forbidden.ToResult("Only the sheet owner can delete this sheet.");
        }

        var t = (Task<OperationResult<JObject>>)crudMethodInfo.DeleteOneAsyncMethodInfo.Invoke(RfConfiguration.RepositoryService, [
            entityName,
            id,
            RequesterUser.NotNull().Id,
            cancellationToken]).NotNull();
        var result = await t.NotNull();
        return !result.IsSuccessful ? result.StatusCode.ToResult(result.ErrorMessage) : result.Data.ToResult();
    }

    private async Task<IResult> HandleHistory(string entityName, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        var result = await RfConfiguration.RepositoryService.GetEntityRevisionsAsync(entityName, id, cancellationToken);
        return !result.IsSuccessful ? result.StatusCode.ToResult(result.ErrorMessage) : result.Data.ToResult();
    }

    // ── Sheet access control ─────────────────────────────────────────

    internal enum SharingAccessLevel { None, View, Edit, Owner }

    /// <summary>
    /// Determines the access level the given user has on a sheet entity.
    /// Priority: admin > owner > shared user (edit/view) > shared role (edit/view) > public > none.
    /// </summary>
    internal static SharingAccessLevel GetEntitySharingAccessLevel(JObject sheetEntity, EntityModel<UserEntityFieldsModel> user)
    {
        // Users with the Owner or Sheets Admin role always get full access to all sheets
        if (RootManager.HasSheetAdminRole(user.Fields))
        {
            return SharingAccessLevel.Owner;
        }

        // Owner always has full access
        if (sheetEntity.TryGetValue(EntityModelAttributes.Author, out var authorToken)
            && authorToken.Type == JTokenType.Integer
            && authorToken.Value<int>() == user.Id)
        {
            return SharingAccessLevel.Owner;
        }

        var fields = sheetEntity[EntityModelAttributes.Fields] as JObject;

        // Accumulate the best access level from both shared_users and shared_roles,
        // so a user gains the highest permission across all access vectors.
        // (e.g. shared as "view" directly but "edit" via a role → should receive "edit")
        var bestAccess = SharingAccessLevel.None;

        // Check shared_users
        if (fields?["shared_users"] is JArray sharedUsers)
        {
            foreach (var entry in sharedUsers)
            {
                if (entry is JObject su
                    && su.TryGetValue("user", out var userIdToken)
                    && userIdToken.Type == JTokenType.Integer
                    && userIdToken.Value<int>() == user.Id)
                {
                    var perm = su["permission"]?.Value<string>() ?? "view";
                    var level = perm == "edit" ? SharingAccessLevel.Edit : SharingAccessLevel.View;
                    if (level > bestAccess) bestAccess = level;
                }
            }
        }

        // Check shared_roles
        if (fields?["shared_roles"] is JArray sharedRoles && sharedRoles.Count > 0)
        {
            var userRoleIds = new HashSet<int>(user.Fields.Roles.Select(r => r.RoleId));
            foreach (var entry in sharedRoles)
            {
                if (entry is JObject sr
                    && sr.TryGetValue("role", out var roleIdToken)
                    && roleIdToken.Type == JTokenType.Integer
                    && userRoleIds.Contains(roleIdToken.Value<int>()))
                {
                    var perm = sr["permission"]?.Value<string>() ?? "view";
                    var level = perm == "edit" ? SharingAccessLevel.Edit : SharingAccessLevel.View;
                    if (level > bestAccess) bestAccess = level;
                }
            }
        }

        if (bestAccess > SharingAccessLevel.None) return bestAccess;

        // Public sheets: anyone with PEEK_ALL permission on rf-sheets can view
        if (fields?["is_public"]?.Value<bool>() == true)
        {
            return SharingAccessLevel.View;
        }

        return SharingAccessLevel.None;
    }

    /// <summary>
    /// Fetches all sheets from the full entity table, filters by the current
    /// user's access, and returns them as a peek-overview JArray.
    /// Used for both PEEK_ALL and PEEK_ALL_PAGINATED of rf-sheets.
    /// </summary>
    private async Task<IResult> HandlePeekAllSheets(CancellationToken cancellationToken)
    {
        var user = RequesterUser.NotNull();
        var accessible = new JArray();

        await foreach (var item in RfConfiguration.RepositoryService.GetAllAsync(RfReservedEntities.SheetsEntityName, cancellationToken: cancellationToken))
        {
            if (!item.IsSuccessful) continue;
            var entity = item.Data.NotNull();

            var access = GetEntitySharingAccessLevel(entity, user);
            if (access == SharingAccessLevel.None) continue;

            // Build lean peek overview object
            var peek = new JObject { [EntityModelAttributes.Id] = entity[EntityModelAttributes.Id] };

            if (entity.TryGetValue(EntityModelAttributes.Title, out var titleToken))
            {
                // Flatten title: {rendered: "..."} → "..."
                if (titleToken is JObject titleObj
                    && titleObj.TryGetValue(EntityModelAttributes.TitleRendered, out var renderedToken)
                    && renderedToken.Type == JTokenType.String)
                    peek[EntityModelAttributes.Title] = renderedToken.Value<string>();
                else
                    peek[EntityModelAttributes.Title] = titleToken;
            }
            if (entity.TryGetValue(EntityModelAttributes.Modified, out var modToken))
                peek[EntityModelAttributes.Modified] = modToken;
            if (entity.TryGetValue(EntityModelAttributes.ModifiedGmt, out var modGmtToken))
                peek[EntityModelAttributes.ModifiedGmt] = modGmtToken;
            if (entity.TryGetValue(EntityModelAttributes.Date, out var dateToken))
                peek[EntityModelAttributes.Date] = dateToken;
            if (entity.TryGetValue(EntityModelAttributes.DateGmt, out var dateGmtToken))
                peek[EntityModelAttributes.DateGmt] = dateGmtToken;

            // Resolve author display name
            if (entity.TryGetValue(EntityModelAttributes.Author, out var authorToken) && authorToken.Type == JTokenType.Integer)
            {
                var authorId = authorToken.Value<int>();
                peek[$"{EntityModelAttributes.Author}_{EntityModelAttributes.Id}"] = authorId;
                var authorUser = RfConfiguration.UserEntitiesCache.GetEntityCopy(authorId);
                peek[EntityModelAttributes.Author] = authorUser != null ? authorUser.Title.Text : $"User: {authorId}";
            }

            // Include access level so the frontend knows the user's permission
            peek["access_level"] = access.ToString().ToLowerInvariant();

            accessible.Add(peek);
        }

        return accessible.ToResult();
    }

    /// <summary>
    /// Returns users and roles eligible for individual sharing on the given entity type.
    /// Each user/role is annotated with the maximum permission they can be granted
    /// based on their IAM capabilities (READ → "view", UPDATE → "edit").
    /// </summary>
    private static IResult HandleSharingCandidates(string entityName)
    {
        var users = RfConfiguration.UserEntitiesCache.FindEntitiesAndGetCopies();
        var roles = RfConfiguration.IamRoleEntitiesCache.FindEntitiesAndGetCopies();

        var candidateUsers = new JArray();
        foreach (var user in users)
        {
            var canRead = user.Fields.CanUserDo("READ", entityName);
            var canUpdate = user.Fields.CanUserDo("UPDATE", entityName);

            if (!canRead && !canUpdate) continue;

            candidateUsers.Add(new JObject
            {
                ["id"] = user.Id,
                ["name"] = user.Title.Text,
                ["max_permission"] = canUpdate ? "edit" : "view"
            });
        }

        var candidateRoles = new JArray();
        foreach (var role in roles)
        {
            var canRead = role.Fields.CanDo(entityName, "READ");
            var canUpdate = role.Fields.CanDo(entityName, "UPDATE");

            if (!canRead && !canUpdate) continue;

            candidateRoles.Add(new JObject
            {
                ["id"] = role.Id,
                ["name"] = role.Title.Text,
                ["max_permission"] = canUpdate ? "edit" : "view"
            });
        }

        return new JObject
        {
            ["users"] = candidateUsers,
            ["roles"] = candidateRoles
        }.ToResult();
    }
}
