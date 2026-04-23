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
            "SHARING_CANDIDATES" => HandleSharingCandidates(entityName, RequesterUser.NotNull().Fields),
            _ => HttpStatusCode.BadRequest.ToResult("Unknown operation.")
        };
    }

    private async Task<IResult> HandleRead(string entityName, CancellationToken cancellationToken)
    {
        if (!RequestBodyJsonObject.NotNull().TryGetValue("id", out var idToken) || !int.TryParse(idToken.ToString(), out var id))
            return HttpStatusCode.BadRequest.ToResult("Request body should contain -id- field.");

        var result = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
        if (!result.IsSuccessful) return result.ErrorMessage.ToResult();

        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasIndividualSharing)
        {
            var access = GetEntitySharingAccessLevel(entityName, result.Data.NotNull(), RequesterUser.NotNull());
            if (access == SharingAccessLevel.None)
                return HttpStatusCode.Forbidden.ToResult($"You do not have access to this {RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.EntityReadableNameSingular.ToLowerInvariant()}.");
            result.Data!["access_level"] = access.ToString().ToLowerInvariant();
        }

        if (RootManager.IsSystemManagedEntity(entityName, id))
            result.Data!["is_system_managed"] = true;

        // Indicate whether the requesting user can edit the author field
        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasAuthor)
        {
            var requester = RequesterUser.NotNull();
            var isAdmin = RootManager.HasEntityAdminRole(entityName, requester.Fields);
            var isAuthor = result.Data!.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                           && authorToken.Type == JTokenType.Integer
                           && authorToken.Value<int>() == requester.Id;
            result.Data!["can_edit_author"] = isAdmin || isAuthor;
        }

        return result.Data.ToResult();
    }

    private async Task<IResult> HandlePeekAll(string entityName, CancellationToken cancellationToken)
    {
        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasIndividualSharing)
            return await HandlePeekAllWithSharing(entityName, cancellationToken);

        var result = await RfConfiguration.RepositoryService.PeekAllAsync(entityName, cancellationToken);
        if (!result.IsSuccessful) return result.ErrorMessage.ToResult();
        AnnotateSystemManagedEntities(entityName, result.Data.NotNull());
        return result.Data.ToResult();
    }

    private async Task<IResult> HandlePeekAllPaginated(HttpRequest request, string entityName, CancellationToken cancellationToken)
    {
        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasIndividualSharing)
            return await HandlePeekAllWithSharing(entityName, cancellationToken);

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
        if (!result.IsSuccessful)
            return result.StatusCode.ToResult(result.ErrorMessage);
        if (result.Data!["items"] is JArray paginatedItems)
            AnnotateSystemManagedEntities(entityName, paginatedItems);
        return result.Data.ToResult();
    }

    private async Task<IResult> HandleCreate(string entityName, CrudMethodInfo crudMethodInfo, CancellationToken cancellationToken)
    {
        var body = RequestBodyJsonObject.NotNull();

        // Always set the author to the requesting user for entities with HasAuthor.
        // This prevents author impersonation — the client-provided value is overridden.
        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasAuthor)
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

        if (RootManager.IsSystemManagedEntity(entityName, id))
            return HttpStatusCode.Forbidden.ToResult("This entity is managed by the system and cannot be modified.");

        var entityConfig = RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration;

        // Fetch existing entity when needed for access control (sharing or author protection)
        JObject? existingEntity = null;
        if (entityConfig.HasIndividualSharing || entityConfig.HasAuthor)
        {
            var existing = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
            if (!existing.IsSuccessful) return existing.StatusCode.ToResult(existing.ErrorMessage);
            existingEntity = existing.Data.NotNull();
        }

        if (entityConfig.HasIndividualSharing)
        {
            var access = GetEntitySharingAccessLevel(entityName, existingEntity!, RequesterUser.NotNull());
            if (access < SharingAccessLevel.Edit)
                return HttpStatusCode.Forbidden.ToResult($"You do not have edit access to this {entityConfig.EntityReadableNameSingular.ToLowerInvariant()}.");

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

        // Protect author field: only admin or the current author can change it
        if (entityConfig.HasAuthor)
        {
            var requester = RequesterUser.NotNull();
            var isAdmin = RootManager.HasEntityAdminRole(entityName, requester.Fields);
            var isAuthor = existingEntity!.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                           && authorToken.Type == JTokenType.Integer
                           && authorToken.Value<int>() == requester.Id;

            if (!isAdmin && !isAuthor)
            {
                RequestBodyJsonObject.NotNull().Remove(EntityModelAttributes.Author);
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

        if (RootManager.IsSystemManagedEntity(entityName, id))
            return HttpStatusCode.Forbidden.ToResult("This entity is managed by the system and cannot be deleted.");

        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasIndividualSharing)
        {
            var existing = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
            if (!existing.IsSuccessful) return existing.StatusCode.ToResult(existing.ErrorMessage);

            var readableName = RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.EntityReadableNameSingular.ToLowerInvariant();
            var access = GetEntitySharingAccessLevel(entityName, existing.Data.NotNull(), RequesterUser.NotNull());
            if (access != SharingAccessLevel.Owner)
                return HttpStatusCode.Forbidden.ToResult($"Only the {readableName} owner can delete this {readableName}.");
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

    // ── Individual sharing access control ─────────────────────────────

    internal enum SharingAccessLevel { None, View, Edit, Owner }

    /// <summary>
    /// Determines the access level the given user has on an individually-shared entity.
    /// Priority: admin > owner > shared user (edit/view) > shared role (edit/view) > public > none.
    /// </summary>
    internal static SharingAccessLevel GetEntitySharingAccessLevel(string entityName, JObject entity, EntityModel<UserEntityFieldsModel> user)
    {
        // Users with the Owner or entity admin role always get full access
        if (RootManager.HasEntityAdminRole(entityName, user.Fields))
        {
            return SharingAccessLevel.Owner;
        }

        // Owner always has full access
        if (entity.TryGetValue(EntityModelAttributes.Author, out var authorToken)
            && authorToken.Type == JTokenType.Integer
            && authorToken.Value<int>() == user.Id)
        {
            return SharingAccessLevel.Owner;
        }

        var fields = entity[EntityModelAttributes.Fields] as JObject;

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

        // Public entities: anyone with PEEK_ALL permission on this entity type can view
        if (fields?["is_public"]?.Value<bool>() == true)
        {
            return SharingAccessLevel.View;
        }

        return SharingAccessLevel.None;
    }

    /// <summary>
    /// Fetches all entities of the given individually-shared type, filters by the current
    /// user's access, and returns them as a peek-overview JArray.
    /// Used for both PEEK_ALL and PEEK_ALL_PAGINATED of individually-shared entities.
    /// </summary>
    private async Task<IResult> HandlePeekAllWithSharing(string entityName, CancellationToken cancellationToken)
    {
        var user = RequesterUser.NotNull();
        var accessible = new JArray();

        await foreach (var item in RfConfiguration.RepositoryService.GetAllAsync(entityName, cancellationToken: cancellationToken))
        {
            if (!item.IsSuccessful) continue;
            var entity = item.Data.NotNull();

            var access = GetEntitySharingAccessLevel(entityName, entity, user);
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

            if (RootManager.IsSystemManagedEntity(entityName, peek[EntityModelAttributes.Id]!.Value<int>()))
                peek["is_system_managed"] = true;

            accessible.Add(peek);
        }

        return accessible.ToResult();
    }

    /// <summary>
    /// Returns users and roles eligible for individual sharing on the given entity type.
    /// Each user/role is annotated with the maximum permission they can be granted
    /// based on their IAM capabilities (READ → "view", UPDATE → "edit").
    /// </summary>
    private static IResult HandleSharingCandidates(string entityName, UserEntityFieldsModel requesterFields)
    {
        var candidateUsers = new JArray();
        // Only return user candidates if the requester can peek the users entity
        if (requesterFields.CanUserDo("PEEK_ALL", RfReservedEntities.UsersEntityName))
        {
            var users = RfConfiguration.UserEntitiesCache.FindEntitiesAndGetCopies();
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
        }

        var candidateRoles = new JArray();
        // Only return role candidates if the requester can peek the iam-role entity
        if (requesterFields.CanUserDo("PEEK_ALL", RfReservedEntities.IamRoleEntityName))
        {
            var roles = RfConfiguration.IamRoleEntitiesCache.FindEntitiesAndGetCopies();
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
        }

        return new JObject
        {
            ["users"] = candidateUsers,
            ["roles"] = candidateRoles
        }.ToResult();
    }

    private static void AnnotateSystemManagedEntities(string entityName, JArray items)
    {
        foreach (var item in items)
        {
            if (item is JObject obj
                && obj.TryGetValue(EntityModelAttributes.Id, out var idVal)
                && idVal.Type == JTokenType.Integer
                && RootManager.IsSystemManagedEntity(entityName, idVal.Value<int>()))
            {
                obj["is_system_managed"] = true;
            }
        }
    }
}
