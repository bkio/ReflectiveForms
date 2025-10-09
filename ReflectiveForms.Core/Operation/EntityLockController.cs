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
}
public enum EntityLockOwnerStatus
{
    OwnedByUser,
    OwnedByOtherUser,
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
        CancellationToken cancellationToken)
    {
        var getLockStatusResult = await GetLockStatusAsync(entityType, id, cancellationToken);
        if (!getLockStatusResult.IsSuccessful)
            return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Failure(getLockStatusResult.ErrorMessage, getLockStatusResult.StatusCode);

        var state = getLockStatusResult.Data;

        if (state != null)
        {
            return state.LockedByUserId == userId
                ? OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
                    new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.OwnedByUser, state))
                : OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
                    new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.OwnedByOtherUser, state));
        }
        return OperationResult<EntityLockOwnerStatusAndEntityLockState>.Success(
            new EntityLockOwnerStatusAndEntityLockState(EntityLockOwnerStatus.NotLocked, null));
    }

    private static async Task<OperationResult<bool>> SetTtlAndNewLockStateUnsafeAsync(
        IMemoryScope memoryScope,
        EntityLockState newLockObject,
        CancellationToken cancellationToken)
    {
        var setExpireResult = await RfConfiguration.RepositoryService.MemoryServiceInstance.SetKeyExpireTimeAsync(
            memoryScope,
            TimeSpan.FromSeconds(65),
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
        CancellationToken cancellationToken)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        var userObject = RfConfiguration.UserEntitiesCache.GetEntityCopy(userId);
        if (userObject == null)
            return OperationResult<bool>.Failure($"Lock-owning user {userId} not found.", HttpStatusCode.NotFound);

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByUser:
                return OperationResult<bool>.Success(true);
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
            LockedByUserName = userObject.Title.Text
        };
        return await SetTtlAndNewLockStateUnsafeAsync(memoryScope, newLockObject, cancellationToken);
    }
    public static async Task<OperationResult<bool>> TryToUnlockAsync(
        string entityType,
        int id,
        int userId,
        CancellationToken cancellationToken)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByOtherUser:
                return OperationResult<bool>.Failure($"Lock is owned by another user.", HttpStatusCode.Conflict);
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
    public static async Task<OperationResult<bool>> HeartbeatAsync(string entityType, int id, int userId, CancellationToken cancellationToken)
    {
        var memoryScope = new MemoryScopeLambda($"{EntityLockMemoryScopePrefix}:{entityType}:{id}");

        await using var mutex = await CreateEntityMutexAsync(entityType, id, cancellationToken);

        var lockCheckResult = await CheckIfLockIsLockedByUserIdUnsafeAsync(entityType, id, userId, cancellationToken);
        if (!lockCheckResult.IsSuccessful)
            return OperationResult<bool>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);

        switch (lockCheckResult.Data.OwnerStatus)
        {
            case EntityLockOwnerStatus.OwnedByOtherUser:
                return OperationResult<bool>.Failure($"Lock is owned by another user.", HttpStatusCode.Conflict);
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
