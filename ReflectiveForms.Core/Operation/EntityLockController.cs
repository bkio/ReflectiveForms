// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;

namespace ReflectiveForms.Core.Operation;

public class EntityLockState
{
    [JsonProperty("entity_id")]
    public required int EntityId  { get; init; }

    [JsonProperty("locked_by_user_id")]
    public required int LockedByUserId  { get; init; } = -1;

    [JsonProperty("locked_by_user_name")]
    public required string? LockedByUserName { get; init; }

    /// <summary>
    /// Unique identifier for the browser tab holding the lock.
    /// When the same user opens a second tab, the tab_id differs from the
    /// one stored here, so the second tab is denied the lock.
    /// Null for legacy locks created before this field was introduced.
    /// </summary>
    [JsonProperty("locked_by_tab_id")]
    public string? LockedByTabId { get; init; }
}
public enum EntityLockOwnerStatus
{
    OwnedByUser,
    OwnedByOtherUser,
    /// <summary>Same user but a different browser tab.</summary>
    OwnedByUserDifferentTab,
    NotLocked
}
public record EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus OwnerStatus, EntityLockState? LockState);

public static class EntityLockController
{
    private const string EntityLockMemoryScopePrefix = "ReflectiveForms.Core.Operation.EntityLockController:MemoryScope";
    private const string MutexLockMemoryScopePrefix = "ReflectiveForms.Core.Operation.EntityLockController:MutexScope";
    private const string StateKey = "state";

    private static async Task<MemoryScopeMutex> CreateEntityMutexAsync(
        string entityType,
        int id,
        CancellationToken cancellationToken)
    {
        return await MemoryScopeMutex.CreateEntityScopeAsync(
            RfConfiguration.RepositoryService.MemoryServiceInstance,
            new MemoryScopeLambda($"{MutexLockMemoryScopePrefix}:{entityType}"),
            $"{id}",
            TimeSpan.FromMinutes(1),
            cancellationToken);
    }

    /// <summary>
    /// No internal locking is done here as there is only one memory command being executed.
    /// </summary>
    public static async Task<OperationResult<EntityLockState?>> GetLockStatusAsync(
        string entityType,
        int id,
        CancellationToken cancellationToken)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        var result = await RfConfiguration.RepositoryService.MemoryServiceInstance.GetKeyValueAsync(
            memoryScope,
            StateKey,
            cancellationToken);
        if (!result.IsSuccessful)
            return OperationResult<EntityLockState?>.Failure($"Error: EntityLockController->GetLockStatusAsync: GetKeyValueAsync has failed. Error: {result.ErrorMessage}", result.StatusCode);

        if (result.Data == null)
            return OperationResult<EntityLockState?>.Success(null);

        var deserialized = JsonConvert.DeserializeObject<EntityLockState>(result.Data.AsString);
        return OperationResult<EntityLockState?>.Success(deserialized);
    }

    public static async Task<OperationResult<IReadOnlyDictionary<int, EntityLockState>>> GetAllLockedAsync(
        string entityType,
        CancellationToken cancellationToken)
    {
        var prefix = $"{EntityLockMemoryScopePrefix}:{entityType}:";

        var result = await RfConfiguration.RepositoryService.MemoryServiceInstance.ScanMemoryScopesWithPattern(
            $"{prefix}*",
            cancellationToken);

        if (!result.IsSuccessful)
            return OperationResult<IReadOnlyDictionary<int, EntityLockState>>.Failure($"Error: EntityLockController->GetLockStatusForAllAsync: ScanMemoryScopesWithPattern has failed. Error: {result.ErrorMessage}", result.StatusCode);

        var ids = result.Data
            .Where(item => item.StartsWith(prefix))
            .Select(item => item[prefix.Length..]) // removes the prefix
            .Select(idStr => int.TryParse(idStr, out var id) ? (int?)id : null)
            .Where(id => id.HasValue)
            .Select(id => id.NotNull())
            .ToList();

        var tasks = ids.Select(id => GetLockStatusAsync(entityType, id, cancellationToken)).ToList();
        await Task.WhenAll(tasks);

        var resultDict = new Dictionary<int, EntityLockState>();
        foreach (var task in tasks)
        {
            if (!task.Result.IsSuccessful)
                return OperationResult<IReadOnlyDictionary<int, EntityLockState>>.Failure(task.Result.ErrorMessage, result.StatusCode);
            if (task.Result.Data == null)
                continue;
            resultDict[task.Result.Data.EntityId] = task.Result.Data;
        }
        return OperationResult<IReadOnlyDictionary<int, EntityLockState>>.Success(resultDict);
    }

    private static async Task<OperationResult<EntityLockOwnerStatusAndEntityLockState>> CheckIfLockIsLockedByUserIdUnsafeAsync(
        string entityType,
        int id,
        int userId,
        CancellationToken cancellationToken,
        string? tabId = null)
    {
        var getLockStatusResult = await GetLockStatusAsync(entityType, id, cancellationToken);
        if (!getLockStatusResult.IsSuccessful)
            return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Failure(getLockStatusResult.ErrorMessage, getLockStatusResult.StatusCode);

        var state = getLockStatusResult.Data;

        if (state != null)
        {
            if (state.LockedByUserId != userId)
                return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
                    new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.OwnedByOtherUser, state));

            // Same user — check tab_id. If both the stored and incoming tab_id are present
            // and they differ, this is a different browser tab.
            if (!string.IsNullOrEmpty(tabId)
                && !string.IsNullOrEmpty(state.LockedByTabId)
                && !string.Equals(tabId, state.LockedByTabId, StringComparison.Ordinal))
            {
                return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
                    new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.OwnedByUserDifferentTab, state));
            }

            return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
                new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.OwnedByUser, state));
        }
        return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
            new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.NotLocked, null));
    }

    private static async Task<OperationResult<bool>> SetTtlAndNewLockStateUnsafeAsync(
        IMemoryScope memoryScope,
        EntityLockState newLockObject,
        CancellationToken cancellationToken)
    {
        // Lock TTL = configured inactivity timeout + 60 s buffer.
        // The frontend heartbeats every ~15 s, refreshing this TTL each time.
        // When the user goes inactive the frontend releases explicitly; the TTL
        // is only a safety net for browser crashes / network drops.
        var lockTtl = TimeSpan.FromMilliseconds(RfConfiguration.EditInactivityTimeoutMs + 60_000);

        var setExpireResult = await RfConfiguration.RepositoryService.MemoryServiceInstance.SetKeyExpireTimeAsync(
            memoryScope,
            lockTtl,
            cancellationToken);
        if (!setExpireResult.IsSuccessful)
            return OperationResult<bool>.Failure(setExpireResult.ErrorMessage, setExpireResult.StatusCode);

        var setKeyResult = await RfConfiguration.RepositoryService.MemoryServiceInstance.SetKeyValuesAsync(
            memoryScope,
            new Dictionary<string, Primitive>
            {
                {
                    StateKey,
                    JsonConvert.SerializeObject(newLockObject)
                }
            },
            false,
            cancellationToken);
        return !setKeyResult.IsSuccessful
            ? OperationResult<bool>.Failure($"Error: EntityLockController->TryToLock: SetKeyValuesAsync has failed. Error: {setKeyResult.ErrorMessage}", setKeyResult.StatusCode)
            : OperationResult<bool>.Success(true);
    }

    public static async Task<OperationResult<bool>> TryToLockAsync(
        string entityType,
        int id,
        int userId,
        CancellationToken cancellationToken,
        string? tabId = null)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        var userObject = RfConfiguration.UserEntitiesCache.GetEntityCopy(userId);
        if (userObject == null)
            return OperationResult<bool>.Failure($"Lock-owning user {userId} not found.", HttpStatusCode.NotFound);

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken, tabId);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByUser:
                // Same user, same tab — refresh TTL and return success
                var existingState = lockCheckResult.Data.LockState!;
                var refreshedState = new EntityLockState
                {
                    EntityId = existingState.EntityId,
                    LockedByUserId = existingState.LockedByUserId,
                    LockedByUserName = existingState.LockedByUserName,
                    LockedByTabId = existingState.LockedByTabId
                };
                return await SetTtlAndNewLockStateUnsafeAsync(memoryScope, refreshedState, cancellationToken);
            case EntityLockOwnerStatus.OwnedByUserDifferentTab:
                return OperationResult<bool>.Failure("Lock is held by you in another tab/window.", HttpStatusCode.Conflict);
            case EntityLockOwnerStatus.OwnedByOtherUser:
                return OperationResult<bool>.Failure($"Lock-owning user is owned by another user.", HttpStatusCode.Conflict);
            case EntityLockOwnerStatus.NotLocked:
            default:
                break;
        }

        var newLockObject = new EntityLockState
        {
            EntityId = id,
            LockedByUserId = userId,
            LockedByUserName = userObject.Title.Text,
            LockedByTabId = tabId
        };
        return await SetTtlAndNewLockStateUnsafeAsync(memoryScope, newLockObject, cancellationToken);
    }
    public static async Task<OperationResult<bool>> TryToUnlockAsync(
        string entityType,
        int id,
        int userId,
        CancellationToken cancellationToken,
        string? tabId = null)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken, tabId);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByOtherUser:
                return OperationResult<bool>.Failure($"Lock is owned by another user.", HttpStatusCode.Conflict);
            case EntityLockOwnerStatus.OwnedByUserDifferentTab:
                // Cannot unlock a lock held by your other tab
                return OperationResult<bool>.Failure("Lock is held by you in another tab/window.", HttpStatusCode.Conflict);
            case EntityLockOwnerStatus.NotLocked:
                return OperationResult<bool>.Success(true);
            case EntityLockOwnerStatus.OwnedByUser:
            default:
                break;
        }

        await RfConfiguration.RepositoryService.MemoryServiceInstance.DeleteKeyAsync(
            memoryScope,
            StateKey,
            false,
            cancellationToken);

        return OperationResult<bool>.Success(true);
    }
    public static async Task<OperationResult<bool>> HeartbeatAsync(string entityType, int id, int userId, CancellationToken cancellationToken, string? tabId = null)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken, tabId);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByOtherUser:
                return OperationResult<bool>.Failure($"Lock is owned by another user.", HttpStatusCode.Conflict);
            case EntityLockOwnerStatus.OwnedByUserDifferentTab:
                return OperationResult<bool>.Failure("Lock is held by you in another tab/window.", HttpStatusCode.Conflict);
            case EntityLockOwnerStatus.NotLocked:
                return OperationResult<bool>.Failure($"Lock is not locked.", HttpStatusCode.BadRequest);
            case EntityLockOwnerStatus.OwnedByUser:
            default:
                break;
        }
        return await SetTtlAndNewLockStateUnsafeAsync(memoryScope, lockCheckResult.Data.LockState.NotNull(), cancellationToken);
    }
    public static async Task<OperationResult<bool>> DoesUserStillOwnTheLockAsync(
        string entityType,
        int id,
        int userId,
        bool heartbeatIfOwns = false,
        CancellationToken cancellationToken = default)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByOtherUser:
            case EntityLockOwnerStatus.NotLocked:
                return OperationResult<bool>.Success(false);
            case EntityLockOwnerStatus.OwnedByUser:
            default:
                break;
        }

        if (!heartbeatIfOwns)
            return OperationResult<bool>.Success(true);

        return await SetTtlAndNewLockStateUnsafeAsync(memoryScope, lockCheckResult.Data.LockState.NotNull(), cancellationToken);
    }
}
