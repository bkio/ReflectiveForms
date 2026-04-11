// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class EntityLockControl: BaseEndpoint
{
    /// <summary>Per-request tab identifier extracted from the query string.</summary>
    private string? _tabId;

    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Get, RequestHttpVerb.Post];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // Extract optional tab_id for per-tab lock isolation
        _tabId = request.Query.TryGetValue("tab_id", out var tabVal) ? tabVal.ToString() : null;
        if (string.IsNullOrWhiteSpace(_tabId)) _tabId = null;

        // Validate query params
        if (!request.TryGetTypeParameter(out var entityName, out var failedResult))
            return failedResult.NotNull();

        int? id = null;
        if (request.TryGetEntityIdParameter(out var idValue, out _))
            id = idValue;

        if (!request.Query.TryGetValue("operation", out var opValues))
            return HttpStatusCode.BadRequest.ToResult("Url parameter -operation- is mandatory and must match an expected operation.");

        return request.Method.Equals("POST", StringComparison.InvariantCultureIgnoreCase)
            ? await HandlePostAsync(entityName, id, opValues.ToString(), cancellationToken)
            : await HandleGetAsync(entityName, id, opValues.ToString(), cancellationToken);
    }

    private async Task<IResult> HandleGetAsync(string entityName, int? id, string operation, CancellationToken cancellationToken)
    {
        if (operation is not ("status_one" or "all_locked" or "do_i_still_own_lock"))
            return HttpStatusCode.BadRequest.ToResult("Unknown operation.");

        return operation switch
        {
            "status_one" => await HandleStatusOneAsync(entityName, id, cancellationToken),
            "all_locked" => await AllLockedAsync(entityName, cancellationToken),
            "do_i_still_own_lock" => await DoIStillOwnLockAsync(entityName, id, cancellationToken),
            _ => throw new NotImplementedException()
        };
    }

    private async Task<IResult> HandlePostAsync(string entityName, int? id, string operation, CancellationToken cancellationToken)
    {
        if (operation is not ("try_lock" or "try_unlock" or "heartbeat"))
            return HttpStatusCode.BadRequest.ToResult("Unknown operation.");

        if (id is null or <= 0)
            return HttpStatusCode.BadRequest.ToResult("Id is mandatory and must be a positive integer.");

        return operation switch
        {
            "try_lock" => await TryLockAsync(entityName, id.Value, cancellationToken),
            "try_unlock" => await TryUnlockAsync(entityName, id.Value, cancellationToken),
            "heartbeat" => await HeartbeatAsync(entityName, id.Value, cancellationToken),
            _ => throw new NotImplementedException()
        };
    }

    private async Task<IResult> DoIStillOwnLockAsync(string entityName, int? id, CancellationToken cancellationToken)
    {
        if (id is null or <= 0)
            return HttpStatusCode.BadRequest.ToResult("Id is mandatory and must be a positive integer.");

        var result = await EntityLockController.DoesUserStillOwnTheLockAsync(
            entityName,
            id.Value,
            RequesterUser.NotNull().Id,
            true,
            cancellationToken);

        if (!result.IsSuccessful)
            return result.StatusCode.ToResult(result.ErrorMessage);

        return new JObject
        {
            ["still_owning"] = result.Data
        }.ToResult();
    }

    private static async Task<IResult> AllLockedAsync(string entityName, CancellationToken cancellationToken)
    {
        var result = await EntityLockController.GetAllLockedAsync(entityName, cancellationToken);

        if (!result.IsSuccessful)
            return result.StatusCode.ToResult(result.ErrorMessage);

        var response = new JArray();
        foreach (var state in result.Data.Values)
        {
            response.Add(JObject.FromObject(state));
        }
        return response.ToResult();
    }

    private static async Task<IResult> HandleStatusOneAsync(string entityName, int? id, CancellationToken cancellationToken)
    {
        if (id is null or <= 0)
            return HttpStatusCode.BadRequest.ToResult("Id is mandatory and must be a positive integer.");

        var result = await EntityLockController.GetLockStatusAsync(entityName, id.Value, cancellationToken);

        if (!result.IsSuccessful)
            return result.StatusCode.ToResult(result.ErrorMessage);

        return result.Data == null
            ? HttpStatusCode.NotFound.ToResult("Entity is not locked.")
            : JObject.FromObject(result.Data).ToResult();
    }

    private async Task<IResult> HeartbeatAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var result = await EntityLockController.HeartbeatAsync(entityName, id,  RequesterUser.NotNull().Id, cancellationToken, _tabId);
        return !result.IsSuccessful
            ? result.StatusCode.ToResult(result.ErrorMessage)
            : HttpStatusCode.OK.ToResult("Heartbeat successful.");
    }

    private async Task<IResult> TryUnlockAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var result = await EntityLockController.TryToUnlockAsync(entityName, id,  RequesterUser.NotNull().Id, cancellationToken, _tabId);
        return !result.IsSuccessful
            ? result.StatusCode.ToResult(result.ErrorMessage)
            : HttpStatusCode.OK.ToResult("Unlock successful.");
    }

    private async Task<IResult> TryLockAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        // For individually-shared entity types, verify the user has edit access to this specific entity
        if (RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.HasIndividualSharing)
        {
            var existing = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
            if (!existing.IsSuccessful)
                return existing.StatusCode.ToResult(existing.ErrorMessage);

            var access = Crud.GetEntitySharingAccessLevel(entityName, existing.Data.NotNull(), RequesterUser.NotNull());
            if (access < Crud.SharingAccessLevel.Edit)
                return HttpStatusCode.Forbidden.ToResult("You do not have edit access to this entity.");
        }

        var result = await EntityLockController.TryToLockAsync(entityName, id, RequesterUser.NotNull().Id, cancellationToken, _tabId);
        return !result.IsSuccessful
            ? result.StatusCode.ToResult(result.ErrorMessage)
            : HttpStatusCode.OK.ToResult("Lock successful.");
    }
}
