// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Repositories;

public static class ConditionBuilder
{
    internal static IDatabaseService? DatabaseService { set; private get; }

    public static Func<string, Condition> AttributeExists => DatabaseService.NotNull().AttributeExists;
    public static Func<string, Condition> AttributeNotExists => DatabaseService.NotNull().AttributeNotExists;
    public static Func<string, Primitive, Condition> AttributeEquals => DatabaseService.NotNull().AttributeEquals;
    public static Func<string, Primitive, Condition> AttributeNotEquals => DatabaseService.NotNull().AttributeNotEquals;
    public static Func<string, Primitive, Condition> AttributeIsGreaterThan => DatabaseService.NotNull().AttributeIsGreaterThan;
    public static Func<string, Primitive, Condition> AttributeIsGreaterOrEqual => DatabaseService.NotNull().AttributeIsGreaterOrEqual;
    public static Func<string, Primitive, Condition> AttributeIsLessThan => DatabaseService.NotNull().AttributeIsLessThan;
    public static Func<string, Primitive, Condition> AttributeIsLessOrEqual => DatabaseService.NotNull().AttributeIsLessOrEqual;
    public static Func<string, Primitive, Condition> ArrayElementExists => DatabaseService.NotNull().ArrayElementExists;
    public static Func<string, Primitive, Condition> ArrayElementNotExists => DatabaseService.NotNull().ArrayElementNotExists;
}

public class EntityRepositoryService
{
    internal EntityRepositoryService(EntityRepositoryServiceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration.DatabaseService);
        if (!configuration.DatabaseService.IsInitialized)
            throw new InvalidOperationException("DatabaseService is not initialized.");

        ConditionBuilder.DatabaseService = configuration.DatabaseService;

        ArgumentNullException.ThrowIfNull(configuration.MemoryService);
        if (!configuration.MemoryService.IsInitialized)
            throw new InvalidOperationException("MemoryService is not initialized.");

        _db = configuration.DatabaseService;
        _pubSubService = configuration.PubSubService;
        MemoryServiceInstance = configuration.MemoryService;
        FileServiceConfiguration = configuration.FileServiceConfiguration;

        // ReSharper disable once SuspiciousTypeConversion.Global
        (_db as DatabaseServiceBase)?.SetOptions(new DbOptions
        {
            AutoSortArrays = DbAutoSortArrays.No,
            AutoConvertRoundableFloatToInt = DbAutoConvertRoundableFloatToInt.Yes
        });
    }
    private readonly IDatabaseService _db;
    private readonly IPubSubService _pubSubService;
    internal IMemoryService MemoryServiceInstance { get; }
    internal IDatabaseService DatabaseServiceInstance => _db;
    internal FileServiceConfiguration FileServiceConfiguration { get; }

    private readonly IMemoryScope _mutexScope = new MemoryScopeLambda("ReflectiveForms.Core.Repositories.EntityRepositoryService");
    private const string PubSubEntityChangedTopicPrefix = "ReflectiveForms.Core.Repositories.EntityRepositoryService.OnEntityChanged";

    public async Task SubscribeToOnEntityChangedAsync<T>(string entityName, int entityId, Action<EntityChangedMessage<T>> callback, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        await SubscribeAsync($"{PubSubEntityChangedTopicPrefix}-{entityName}-{entityId}", callback, cancellationToken);
    }
    public async Task SubscribeToOnEntitiesChangedAsync<T>(string entityName, Action<EntityChangedMessage<T>> callback, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        await SubscribeAsync($"{PubSubEntityChangedTopicPrefix}-{entityName}", callback, cancellationToken);
    }
    private async Task SubscribeAsync<T>(string topic, Action<EntityChangedMessage<T>> callback, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        await _pubSubService.SubscribeAsync(topic, (_, serialized) =>
            {
                var deserialized = serialized.DeserializeObjectWithPolymorphism<EntityChangedMessage<T>>();
                if (deserialized == null)
                {
                    RfConfiguration.LogError(new JsonException($"EntityRepositoryService->SubscribeToOnEntityChangedAsync: Deserialization failed. Serialized: {serialized}"));
                }
                else
                {
                    callback(deserialized);
                }
                return Task.CompletedTask;
            },
            onError: RfConfiguration.LogError,
            cancellationToken: cancellationToken);
    }
    private async Task PublishEntityChangedAsync<T>(
        string entityName,
        int entityId,
        EntityChangedEventType eventType,
        JObject? oldEntityState,
        JObject? newEntityState,
        CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        var entityModelType = RfConfiguration.EntityNameToConfiguration[entityName].EntityModelType;

        EntityModel<T>? oldEntityStateAsObject = null;
        EntityModel<T>? newEntityStateAsObject = null;
        if (oldEntityState != null)
            oldEntityStateAsObject = (EntityModel<T>)oldEntityState.ToObjectWithPolymorphism(entityModelType).NotNull();
        if (newEntityState != null)
            newEntityStateAsObject = (EntityModel<T>)newEntityState.ToObjectWithPolymorphism(entityModelType).NotNull();

        var message =
            new EntityChangedMessage<T>(
                entityName,
                entityId,
                eventType,
                oldEntityStateAsObject,
                newEntityStateAsObject)
                .SerializeObjectWithPolymorphism();

        var tasks = new List<Task>
        {
            _pubSubService.PublishAsync(
                $"{PubSubEntityChangedTopicPrefix}-{entityName}",
                message,
                cancellationToken),
            _pubSubService.PublishAsync(
                $"{PubSubEntityChangedTopicPrefix}-{entityName}-{entityId}",
                message,
                cancellationToken)
        };
        await Task.WhenAll(tasks);
    }

    internal static string GetEntityTableName(string entityName) => entityName;
    private static string GetEntityPeekOverviewTableName(string entityName) => $"{entityName}-peek-overview";
    private static string GetEntityHistoryTableName(string entityName) => $"{entityName}-history";
    private const string GlobalIndexesTableName = "indexes";
    private const string GlobalIndexesKeyName = "last_entity_index";
    private const string GlobalIndexesLastValueAttributeName = "last";
    private const string HistoryTableOldRevisionsCountAttributeName = "old_revisions_count";
    private const string HistoryTableOldRevisionContainerAttributeNamePrefix = "old_revision_";
    private const string HistoryTableOldRevisionInContainerObjectAttributeName = "object";
    private const string HistoryTableOldRevisionInContainerModifiedByIdAttributeName = "modified_by_id";
    private const string HistoryTableOldRevisionInContainerModifiedByEmailAttributeName = "modified_by_email";

    private const int DefaultPageSize = 50;

    private async IAsyncEnumerable<OperationResult<JObject>> InternalGetAllPaginatedAsync(
        string entityName,
        int maxItems = int.MaxValue,
        ConditionCoupling? filter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var configuration = RfConfiguration.EntityNameToConfiguration[entityName];
        var generatedNo = 0;
        string? nextToken = null;
        do
        {
            var pageSize = Math.Min(DefaultPageSize, maxItems - generatedNo);

            var result = filter != null
                ? await _db.ScanTableWithFilterPaginatedAsync(
                    GetEntityTableName(entityName),
                    filter,
                    pageSize,
                    nextToken,
                    cancellationToken)
                : await _db.ScanTablePaginatedAsync(
                    GetEntityTableName(entityName),
                    pageSize,
                    nextToken,
                    cancellationToken);

            if (!result.IsSuccessful)
            {
                yield return OperationResult<JObject>.Failure(
                    $"GetAllAsync has failed with: {result.ErrorMessage}",
                    result.StatusCode);
                yield break;
            }
            nextToken = result.Data.NextPageToken;

            foreach (var item in result.Data.Items)
            {
                // Merge C# model defaults so that fields added after entity creation appear
                var merged = EntityDefaultsMerger.MergeDefaults(item, configuration);
                yield return OperationResult<JObject>.Success(merged);
            }

            generatedNo += result.Data.Items.Count;
        } while (nextToken != null && generatedNo < maxItems);
    }

    public IAsyncEnumerable<OperationResult<JObject>> GetAllAsync(
        string entityName,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetAllPaginatedAsync(entityName, maxItems ?? int.MaxValue, null, cancellationToken);
    }

    public IAsyncEnumerable<OperationResult<JObject>> GetByAuthorIdAsync(
        string entityName,
        int authorId,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetAllPaginatedAsync(entityName, maxItems ?? int.MaxValue, _db.AttributeEquals(EntityModelAttributes.Author, authorId), cancellationToken);
    }

    public IAsyncEnumerable<OperationResult<JObject>> GetByFilterAsync(
        string entityName,
        ConditionCoupling filter,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetAllPaginatedAsync(entityName, maxItems ?? int.MaxValue, filter, cancellationToken);
    }

    public IAsyncEnumerable<OperationResult<JObject>> GetByFilterByAuthorIdAsync(
        string entityName,
        int authorId,
        ConditionCoupling filter,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetAllPaginatedAsync(entityName, maxItems ?? int.MaxValue, _db.AttributeEquals(EntityModelAttributes.Author, authorId).And(filter), cancellationToken);
    }

    public IAsyncEnumerable<OperationResult<JObject>> GetByFilterByTagIdAsync(
        string entityName,
        int tagId,
        ConditionCoupling filter,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetAllPaginatedAsync(entityName, maxItems ?? int.MaxValue, _db.ArrayElementExists(EntityModelAttributes.Tags, tagId).And(filter), cancellationToken);
    }

    public IAsyncEnumerable<OperationResult<JObject>> GetByTagIdAsync(
        string entityName,
        int tagId,
        int? maxItems = null,
        CancellationToken cancellationToken = default)
    {
        return InternalGetAllPaginatedAsync(entityName, maxItems ?? int.MaxValue, _db.ArrayElementExists(EntityModelAttributes.Tags, tagId), cancellationToken);
    }

    public async Task<OperationResult<JObject>> GetOneAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var result = await _db.GetItemAsync(
            GetEntityTableName(entityName),
            new DbKey(EntityModelAttributes.Id, id),
            null,
            cancellationToken);
        if (!result.IsSuccessful)
            return OperationResult<JObject>.Failure($"GetOneAsync has failed with: {result.ErrorMessage}", result.StatusCode);
        if (result.Data == null)
            return OperationResult<JObject>.Failure("Not found.", HttpStatusCode.NotFound);

        // Merge C# model defaults so that fields added after entity creation appear
        var configuration = RfConfiguration.EntityNameToConfiguration[entityName];
        var merged = EntityDefaultsMerger.MergeDefaults(result.Data, configuration);
        return OperationResult<JObject>.Success(merged);
    }

    public async Task<OperationResult<bool>> DoesExistAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var result = await _db.ItemExistsAsync(
            GetEntityPeekOverviewTableName(entityName),
            new DbKey(EntityModelAttributes.Id, id),
            null,
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<bool>.Failure($"DoesExistAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : result;
    }
    private const string PutOneAsyncMethodName = "PutOneAsync";
    public async Task<OperationResult<JObject>> PutOneAsync<T>(
        string entityName,
        JObject body,
        CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        // Note: If parameters of this method change, remember to update Crud.cs as well. Reflection is used there.

        FixBodyForMustHaveFields(entityName, body);

        JObject? result = null;

        var newId = -1;

        var success = false;
        var trialCount = 1;
        while (++trialCount <= 5)
        {
            var incrementResult = await _db.IncrementAttributeAsync(GlobalIndexesTableName,
                new DbKey(EntityModelAttributes.Id, GlobalIndexesKeyName),
                GlobalIndexesLastValueAttributeName,
                1.0,
                null,
                cancellationToken);
            if (!incrementResult.IsSuccessful)
                return OperationResult<JObject>.Failure($"PutOneAsync has failed with: {incrementResult.ErrorMessage}", incrementResult.StatusCode);

            newId = (int)incrementResult.Data;

            await using var mutex = await MemoryScopeMutex.CreateEntityScopeAsync(
                MemoryServiceInstance,
                _mutexScope,
                $"{EntityModelAttributes.Id}:{newId}",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            var idKey = new DbKey(EntityModelAttributes.Id, newId);

            body[EntityModelAttributes.Id] = newId;
            body[EntityModelAttributes.Link] = RfConfiguration.EndpointConfiguration.GetEntityUrl(entityName, newId);

            var configuration = RfConfiguration.EntityNameToConfiguration[entityName];

            var newBody = (JObject)configuration.DefaultJObject.DeepClone();
            newBody.Merge(body, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace
            });

            var sanityCheck = await configuration.UpsertSanityCheck((newBody, cancellationToken));
            if (!sanityCheck.IsSuccessful)
                return OperationResult<JObject>.Failure($"Sanity check for {entityName}, entity {EntityModelAttributes.Id}: {newId} has failed with {sanityCheck.ErrorMessage}", HttpStatusCode.BadRequest);

            var putItemResult = await _db.PutItemAsync(
                GetEntityTableName(entityName),
                idKey,
                newBody,
                DbReturnItemBehavior.ReturnNewValues,
                false,
                cancellationToken);
            if (!putItemResult.IsSuccessful || putItemResult.Data == null)
                continue;

            result = putItemResult.Data;

            success = true;

            var extractionResult = await TryExtractingPeekOverviewFromBodyAsync(entityName, result, cancellationToken);
            if (extractionResult.IsSuccessful)
            {
                var peekOverview = extractionResult.Data;

                var updateResult = await _db.UpdateItemAsync(
                    GetEntityPeekOverviewTableName(entityName),
                    idKey,
                    peekOverview,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationToken);
                if (!updateResult.IsSuccessful)
                {
                    var error = $"Error: EntityRepository->PutOneAsync: PutItemAsync has succeeded, but UpdateItemAsync has failed. Id: {idKey.Value.AsInteger} Peek Overview: {peekOverview} ({updateResult.StatusCode})";
                    var deleteResult = await _db.DeleteItemAsync(
                        GetEntityTableName(entityName),
                        idKey,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);
                    if (!deleteResult.IsSuccessful)
                    {
                        return OperationResult<JObject>.Failure(
                            $"FATAL ERROR: EntityRepository->PutOneAsync: DeleteItemAsync has failed. (Failed: Remove back {entityName}->{newId}) ({deleteResult.StatusCode})" +
                            $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                    }
                    success = false;
                    return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);
                }
            }

            // Do NOT run the hook inside the mutex — it may call UpdateOneAsync
            // on the same entity, which would deadlock on the same mutex.
            break;
        }
        if (!success)
        {
            return OperationResult<JObject>.Failure($"EntityRepository->PutOneAsync: PutItemAsync has failed for entity type {entityName}.", HttpStatusCode.InternalServerError);
        }

        // Run the post-create hook OUTSIDE the mutex to avoid deadlock
        // when the hook calls UpdateOneAsync on the same entity.
        await PostCreateHook<T>(entityName, newId, result.NotNull(), cancellationToken);

        // Re-read the entity after the hook in case it was modified (e.g. password hashing)
        var postHookRead = await _db.GetItemAsync(
            GetEntityTableName(entityName),
            new DbKey(EntityModelAttributes.Id, newId),
            null,
            cancellationToken);
        if (postHookRead.IsSuccessful && postHookRead.Data != null)
            result = postHookRead.Data;

        // Vector indexing (best-effort) — after hook re-read, before publish
        if (RfConfiguration.AiServiceConfiguration != null &&
            RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.SupportsSemanticSearch)
        {
            try
            {
                await Ai.AiVectorIndexer.IndexEntityAsync(entityName, newId, result.NotNull(), cancellationToken);
            }
            catch (Exception ex)
            {
                RfConfiguration.LogError(ex);
            }
        }

        await PublishEntityChangedAsync<T>(
            entityName,
            newId,
            EntityChangedEventType.Created,
            null,
            result,
            cancellationToken);

        return OperationResult<JObject>.Success(result.NotNull());
    }

    private const string UpdateOneAsyncMethodName = "UpdateOneAsync";
    public async Task<OperationResult<JObject>> UpdateOneAsync<T>(
        string entityName,
        int id,
        JObject body,
        EntityUpdaterIdentity entityUpdaterIdentity,
        CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        // Note: If parameters of this method change, remember to update Crud.cs as well. Reflection is used there.

        if (!entityUpdaterIdentity.IsDuringHookUpdate && entityUpdaterIdentity.UserId > 0)
        {
            var lockCheckResult = await CheckEntityNotLockedByAnotherUserAsync(entityName, id, entityUpdaterIdentity.UserId, cancellationToken);
            if (!lockCheckResult.IsSuccessful)
                return OperationResult<JObject>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);
        }

        body[EntityModelAttributes.Id] = id;

        JObject? result;
        JObject? oldObject;

        FixBodyForMustHaveFields(entityName, body);
        {
            await using var mutex = await MemoryScopeMutex.CreateEntityScopeAsync(
                MemoryServiceInstance,
                _mutexScope,
                $"{EntityModelAttributes.Id}:{id}",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            var key = new DbKey(EntityModelAttributes.Id, id);

            var getItemResult = await _db.GetItemAsync(
                GetEntityTableName(entityName),
                key,
                null,
                cancellationToken);
            if (!getItemResult.IsSuccessful)
            {
                return OperationResult<JObject>.Failure($"EntityRepository->UpdateOneAsync: GetItemAsync has failed. Id: {id} Entity Name: {entityName}", getItemResult.StatusCode);
            }
            oldObject = getItemResult.Data;
            if (oldObject == null)
            {
                return OperationResult<JObject>.Failure($"EntityRepository->UpdateOneAsync: Entity does not exist. Id: {id} Entity Name: {entityName}", HttpStatusCode.NotFound);
            }
            async Task<OperationResult<JObject>> RevertBackUpdate(string error)
            {
                var revertBackUpdateResult = await _db.UpdateItemAsync(
                    GetEntityTableName(entityName),
                    key,
                    oldObject,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationToken);

                if (!revertBackUpdateResult.IsSuccessful)
                {
                    return OperationResult<JObject>.Failure(
                        $"FATAL ERROR: EntityRepository->UpdateOneAsync: UpdateItemAsync has failed. (Failed: Revert back {entityName}->{id} to the old state.) ({revertBackUpdateResult.StatusCode})" +
                        $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                }
                return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);
            }

            var newBody = EntityDefaultsMerger.MergeDefaults(oldObject, RfConfiguration.EntityNameToConfiguration[entityName]);
            newBody.Merge(body, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace
            });

            var sanityCheckResult = await RfConfiguration.EntityNameToConfiguration[entityName].UpsertSanityCheck((newBody, cancellationToken));
            if (!sanityCheckResult.IsSuccessful)
            {
                return OperationResult<JObject>.Failure($"Sanity check for {entityName}, entity {EntityModelAttributes.Id}: {id} has failed with {sanityCheckResult.ErrorMessage}", HttpStatusCode.BadRequest);
            }

            var updateResult = await _db.UpdateItemAsync(
                GetEntityTableName(entityName),
                key,
                newBody,
                DbReturnItemBehavior.ReturnNewValues,
                null,
                cancellationToken);
            if (!updateResult.IsSuccessful)
            {
                return OperationResult<JObject>.Failure($"EntityRepository->UpdateOneAsync: UpdateItemAsync has failed. Id: {id} Entity Name: {entityName}", updateResult.StatusCode);
            }

            result = updateResult.Data;

            JObject? previousOldRevisionsState = null;
            if (!entityUpdaterIdentity.IsDuringHookUpdate)
            {
                var historyGetItemResult = await _db.GetItemAsync(
                    GetEntityHistoryTableName(entityName),
                    key,
                    null,
                    cancellationToken);
                if (!historyGetItemResult.IsSuccessful)
                {
                    return await RevertBackUpdate($"Error: EntityRepository->UpdateOneAsync: GetItemAsync for {entityName}-history has failed. Id: {key.Value.AsInteger} ({historyGetItemResult.StatusCode})");
                }

                var oldRevisions = historyGetItemResult.Data;

                var gmtNowTimeString = DateUtility.DateTimeToDesiredString(DateTime.UtcNow);
                var localNowTimeString = DateUtility.DateTimeToDesiredString(DateTime.UtcNow.ToLocalTime());

                var oldRevisionsNewObject = new JObject
                {
                    [HistoryTableOldRevisionInContainerObjectAttributeName] = oldObject,
                    [EntityModelAttributes.Date] = localNowTimeString,
                    [EntityModelAttributes.DateGmt] = gmtNowTimeString,
                    [HistoryTableOldRevisionInContainerModifiedByIdAttributeName] = entityUpdaterIdentity.UserId,
                    [HistoryTableOldRevisionInContainerModifiedByEmailAttributeName] = entityUpdaterIdentity.UserEmail
                };

                if (oldRevisions == null)
                {
                    oldRevisions = new JObject()
                    {
                        [HistoryTableOldRevisionsCountAttributeName] = 1,
                        [$"{HistoryTableOldRevisionContainerAttributeNamePrefix}1"] = oldRevisionsNewObject
                    };

                    var historyPutItemResult = await _db.PutItemAsync(
                        GetEntityHistoryTableName(entityName),
                        key,
                        oldRevisions,
                        DbReturnItemBehavior.DoNotReturn,
                        false,
                        cancellationToken);
                    if (!historyPutItemResult.IsSuccessful)
                    {
                        return await RevertBackUpdate($"Error: EntityRepository->UpdateOneAsync: PutItemAsync for {entityName}-history has failed. Id: {key.Value.AsInteger} ({historyPutItemResult.StatusCode})");
                    }
                }
                else
                {
                    previousOldRevisionsState = (JObject)oldRevisions.DeepClone();

                    var oldRevisionsCount = (int)oldRevisions[HistoryTableOldRevisionsCountAttributeName].NotNull() + 1;
                    oldRevisions[HistoryTableOldRevisionsCountAttributeName] = oldRevisionsCount;
                    oldRevisions.Add($"{HistoryTableOldRevisionContainerAttributeNamePrefix}{oldRevisionsCount}", oldRevisionsNewObject);

                    var updateHistoryResult = await _db.UpdateItemAsync(
                        GetEntityHistoryTableName(entityName),
                        key,
                        oldRevisions,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);
                    if (!updateHistoryResult.IsSuccessful)
                    {
                        return await RevertBackUpdate($"Error: EntityRepository->UpdateOneAsync: UpdateItemAsync for {entityName}-history has failed. Id: {key.Value.AsInteger} ({updateHistoryResult.StatusCode})");
                    }
                }
            }

            async Task<OperationResult<JObject>> RevertBackRevisionsUpdate(string error)
            {
                if (entityUpdaterIdentity.IsDuringHookUpdate)
                    return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);

                if (previousOldRevisionsState != null)
                {
                    var revertBackRevisionsUpdateResult = await _db.UpdateItemAsync(
                        GetEntityHistoryTableName(entityName),
                        key,
                        previousOldRevisionsState,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);

                    if (!revertBackRevisionsUpdateResult.IsSuccessful)
                    {
                        return OperationResult<JObject>.Failure(
                            $"FATAL ERROR: EntityRepository->UpdateOneAsync: UpdateItemAsync has failed. (Failed: Revert back {entityName}-history->{id} to the old state.) ({revertBackRevisionsUpdateResult.StatusCode})" +
                            $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                    }
                }
                else
                {
                    var revertBackRevisionsDeleteResult = await _db.DeleteItemAsync(
                        GetEntityHistoryTableName(entityName),
                        key,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);
                    if (!revertBackRevisionsDeleteResult.IsSuccessful)
                    {
                        return OperationResult<JObject>.Failure(
                            $"FATAL ERROR: EntityRepository->UpdateOneAsync: DeleteItemAsync has failed. (Failed: Revert back {entityName}-history->{id} to the old state.) ({revertBackRevisionsDeleteResult.StatusCode})" +
                            $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                    }
                }
                return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);
            }

            JObject? oldPeekOverview = null;
            var oldPeekOverviewExtractSucceeded = false;

            var extractionResult = await TryExtractingPeekOverviewFromBodyAsync(entityName, result, cancellationToken);
            if (extractionResult.IsSuccessful)
            {
                var peekOverview = extractionResult.Data;

                var updatePeekOverviewResult = await _db.UpdateItemAsync(
                    GetEntityPeekOverviewTableName(entityName),
                    key,
                    peekOverview,
                    DbReturnItemBehavior.ReturnOldValues,
                    null,
                    cancellationToken);
                if (!updatePeekOverviewResult.IsSuccessful)
                {
                    return
                        await RevertBackRevisionsUpdate(
                        (await RevertBackUpdate(
                            $"Error: EntityRepository->UpdateOneAsync: UpdateItemAsync for {entityName}-peek-overview has failed. Id: {key.Value.AsInteger} Peek Overview: {peekOverview} ({updatePeekOverviewResult.StatusCode})"))
                                .ErrorMessage);
                }
                oldPeekOverviewExtractSucceeded = true;
                oldPeekOverview = updatePeekOverviewResult.Data; //Might be null
            }

            async Task<OperationResult<JObject>> RevertBackPeekOverviewUpdate(string error)
            {
                if (oldPeekOverview != null)
                {
                    var revertBackPeekOverviewUpdateResult = await _db.UpdateItemAsync(
                        GetEntityPeekOverviewTableName(entityName),
                        key,
                        oldPeekOverview,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);

                    if (!revertBackPeekOverviewUpdateResult.IsSuccessful)
                    {
                        return OperationResult<JObject>.Failure(
                            $"FATAL ERROR: EntityRepository->UpdateOneAsync: UpdateItemAsync has failed. (Failed: Revert back {entityName}-peek-overview->{id} to the old state.) ({revertBackPeekOverviewUpdateResult.StatusCode})" +
                            $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                    }
                }
                else if (oldPeekOverviewExtractSucceeded)
                {
                    var revertBackPeekOverviewDeleteResult = await _db.DeleteItemAsync(
                        GetEntityPeekOverviewTableName(entityName),
                        key,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);
                    if (!revertBackPeekOverviewDeleteResult.IsSuccessful)
                    {
                        return OperationResult<JObject>.Failure(
                            $"FATAL ERROR: EntityRepository->UpdateOneAsync: DeleteItemAsync has failed. (Failed: Revert back {entityName}-peek-overview->{id} to the old state.) ({revertBackPeekOverviewDeleteResult.StatusCode})" +
                            $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                    }
                }
                return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);
            }

            var fixOthersResult = await FixTheUpdateForOthersThatHaveReferenceToThisAsync(entityName, id, result.NotNull(), cancellationToken);
            if (!fixOthersResult.IsSuccessful)
            {
                return
                    await RevertBackRevisionsUpdate(
                        (await RevertBackPeekOverviewUpdate(
                            (await RevertBackUpdate(
                                $"Error: EntityRepository->UpdateOneAsync: FixTheUpdateForOthersThatHaveReferenceToThisAsync has failed. Id: {id} Entity Name: {entityName} ({fixOthersResult.StatusCode})"))
                            .ErrorMessage))
                        .ErrorMessage);
            }
        }

        // Run the post-update hook OUTSIDE the mutex to avoid deadlock
        // when the hook calls UpdateOneAsync on the same entity.
        await PostUpdateHook<T>(entityName, id, oldObject, result.NotNull(), cancellationToken);

        // Vector indexing (best-effort) — after hook, before publish
        if (RfConfiguration.AiServiceConfiguration != null &&
            RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.SupportsSemanticSearch)
        {
            try
            {
                await Ai.AiVectorIndexer.IndexEntityAsync(entityName, id, result.NotNull(), cancellationToken);
            }
            catch (Exception ex)
            {
                RfConfiguration.LogError(ex);
            }
        }

        await PublishEntityChangedAsync<T>(
            entityName,
            id,
            EntityChangedEventType.Updated,
            oldObject,
            result,
            cancellationToken);

        return OperationResult<JObject>.Success(result.NotNull());
    }

    public async Task<OperationResult<bool>> GetEntityHistoryStateAsync(
        string entityName,
        int id,
        CancellationToken cancellationToken)
    {
        var getItemResult = await _db.GetItemAsync(
            GetEntityHistoryTableName(entityName),
            new DbKey(EntityModelAttributes.Id, id),
            null,
            cancellationToken);
        return !getItemResult.IsSuccessful
            ?
            OperationResult<bool>.Failure(
                $"Error: EntityRepository->GetEntityHistoryStateAsync: GetItem has failed. Id: {id} Entity Name: {entityName}", getItemResult.StatusCode)
            : getItemResult.Data == null
                ? OperationResult<bool>.Failure($"Entity not found. Id: {id} Entity Name: {entityName}", HttpStatusCode.NotFound)
                : OperationResult<bool>.Success(true);
    }

    public async Task<OperationResult<JObject>> GetEntityRevisionsAsync(
        string entityName,
        int id,
        CancellationToken cancellationToken)
    {
        var getItemResult = await _db.GetItemAsync(
            GetEntityHistoryTableName(entityName),
            new DbKey(EntityModelAttributes.Id, id),
            null,
            cancellationToken);
        if (!getItemResult.IsSuccessful)
            return OperationResult<JObject>.Failure(
                $"Error: EntityRepository->GetEntityRevisionsAsync: GetItem has failed. Id: {id} Entity Name: {entityName}", getItemResult.StatusCode);

        var historyData = getItemResult.Data;
        if (historyData == null)
        {
            // No history exists — entity has never been updated
            return OperationResult<JObject>.Success(new JObject
            {
                ["revisions_count"] = 0,
                ["revisions"] = new JArray()
            });
        }

        var count = (int)(historyData[HistoryTableOldRevisionsCountAttributeName] ?? 0);
        var revisions = new JArray();
        for (var i = 1; i <= count; i++)
        {
            var revisionKey = $"{HistoryTableOldRevisionContainerAttributeNamePrefix}{i}";
            var revisionContainer = historyData[revisionKey] as JObject;
            if (revisionContainer == null) continue;

            revisions.Add(new JObject
            {
                ["revision_number"] = i,
                [EntityModelAttributes.Date] = revisionContainer[EntityModelAttributes.Date],
                [EntityModelAttributes.DateGmt] = revisionContainer[EntityModelAttributes.DateGmt],
                [HistoryTableOldRevisionInContainerModifiedByEmailAttributeName] = revisionContainer[HistoryTableOldRevisionInContainerModifiedByEmailAttributeName],
                [HistoryTableOldRevisionInContainerObjectAttributeName] = revisionContainer[HistoryTableOldRevisionInContainerObjectAttributeName]
            });
        }

        return OperationResult<JObject>.Success(new JObject
        {
            ["revisions_count"] = count,
            ["revisions"] = revisions
        });
    }

    private async Task<OperationResult<bool>> FixTheUpdateForOthersThatHaveReferenceToThisAsync(string entityName, int id, JObject body, CancellationToken cancellationToken)
    {
        if (!body.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleObject)
            || !titleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered))
            titleRendered = null;

        var isForCategories = entityName is RfReservedEntities.CategoriesEntityName;
        var isForTags = entityName is RfReservedEntities.TagsEntityName;
        if (titleRendered != null && (isForCategories || isForTags))
        {
            return await FixTheUpdateForRelevantPostTypesAsync(
                GetAllPostTypesManagedByThisDbExceptCategoriesAndTags(),
                isForCategories ? $"{EntityModelAttributes.Categories}_{EntityModelAttributes.Id}s" : $"{EntityModelAttributes.Tags}_{EntityModelAttributes.Id}s",
                true,
                id,
                isForCategories ? EntityModelAttributes.Categories : EntityModelAttributes.Tags,
                titleRendered,
                cancellationToken);
        }

        //Parent fix for other types
        return await FixTheUpdateForRelevantPostTypesAsync(
            new List<string> { entityName },
            $"{EntityModelAttributes.Parent}_{EntityModelAttributes.Id}",
            false,
            id,
            "Parent",
            titleRendered ?? $"Parent: {id}",
            cancellationToken);
    }
    private async Task<OperationResult<bool>> FixTheDeleteForOthersThatHaveReferenceToThisAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var isForCategories = entityName == RfReservedEntities.CategoriesEntityName;
        var isForTags = entityName == RfReservedEntities.TagsEntityName;
        if (isForCategories || isForTags)
        {
            return await FixTheDeleteForRelevantPostTypesAsync(
                GetAllPostTypesManagedByThisDbExceptCategoriesAndTags(),
                isForCategories ? $"{EntityModelAttributes.Categories}_{EntityModelAttributes.Id}s" : $"{EntityModelAttributes.Tags}_{EntityModelAttributes.Id}s",
                isForCategories ? EntityModelAttributes.Categories : EntityModelAttributes.Tags,
                true,
                id,
                isForCategories ? EntityModelAttributes.Categories : EntityModelAttributes.Tags,
                cancellationToken);
        }

        //Parent fix for other types
        return await FixTheDeleteForRelevantPostTypesAsync(
            [entityName],
            $"{EntityModelAttributes.Parent}_{EntityModelAttributes.Id}",
            EntityModelAttributes.Parent,
            false,
            id,
            EntityModelAttributes.Parent,
            cancellationToken);
    }
    private readonly List<string> _allPostTypesManagedByThisDbExceptCategoriesAndTags = [];
    private bool _setupAllPostTypesManagedByThisDbExceptCategoriesAndTags;
    private List<string> GetAllPostTypesManagedByThisDbExceptCategoriesAndTags()
    {
        lock (_allPostTypesManagedByThisDbExceptCategoriesAndTags)
        {
            if (_setupAllPostTypesManagedByThisDbExceptCategoriesAndTags)
                return _allPostTypesManagedByThisDbExceptCategoriesAndTags;
            _setupAllPostTypesManagedByThisDbExceptCategoriesAndTags = true;

            foreach (var pType in RfConfiguration.EntityNameToConfiguration.Keys)
            {
                if (pType is RfReservedEntities.CategoriesEntityName or RfReservedEntities.TagsEntityName) continue;

                _allPostTypesManagedByThisDbExceptCategoriesAndTags.Add(pType);
            }
        }
        return _allPostTypesManagedByThisDbExceptCategoriesAndTags;
    }

    public async Task<OperationResult<bool>> FixTheUpdateForRelevantPostTypesAsync(
        IEnumerable<string> relevantPostTypes,
        string conditionIdAttribute,
        bool conditionIsArray,
        int conditionIdValue,
        string typeNameAttribute,
        string typeNameValue,
        CancellationToken cancellationToken)
    {
        var hasFailed = new Atomicable<bool>(false, ThreadSafetyMode.MultipleProducers);
        var errors = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(relevantPostTypes, cancellationToken, async (pType, cancellationTokenForEach) =>
        {
            var tableName = $"{RfConfiguration.EntityNameToConfiguration[pType].EntityConfiguration.EntityName}-peek-overview";

            if (hasFailed.Value) return;

            var scanResult = await _db.ScanTableWithFilterAsync(
                tableName,
                conditionIsArray
                    ? _db.ArrayElementExists(conditionIdAttribute, conditionIdValue)
                    : _db.AttributeEquals(conditionIdAttribute, conditionIdValue),
                cancellationTokenForEach);
            if (!scanResult.IsSuccessful)
            {
                if (hasFailed.Value) return;
                hasFailed.Value = true;
                errors.Add($"Error: FixTheUpdateForRelevantPostTypesAsync: ScanTableWithFilterAsync for table {tableName} has failed.");
                return;
            }

            if (hasFailed.Value) return;

            var relevantPosts = scanResult.Data.Items;

            foreach (var relevantPost in relevantPosts)
            {
                if (hasFailed.Value) return;

                if (conditionIsArray)
                {
                    var displayAsArray = (JArray)relevantPost[typeNameAttribute].NotNull();

                    var idsAsArray = (JArray)relevantPost[conditionIdAttribute].NotNull();
                    for (var i = 0; i < idsAsArray.Count; i++)
                    {
                        if ((int)idsAsArray[i] != conditionIdValue) continue; //Find index. They should have the same index.
                        displayAsArray[i] = typeNameValue;
                        break;
                    }
                }
                else
                {
                    relevantPost[typeNameAttribute] = typeNameValue;
                }

                var updateResult = await _db.UpdateItemAsync(
                    tableName,
                    new DbKey(EntityModelAttributes.Id, (long)relevantPost[EntityModelAttributes.Id].NotNull()),
                    relevantPost,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationTokenForEach);
                if (updateResult.IsSuccessful) continue;
                if (hasFailed.Value) return;
                hasFailed.Value = true;
                errors.Add($"Error: FixTheUpdateForRelevantPostTypesAsync: UpdateItem for table {tableName} has failed for id {(long)relevantPost[EntityModelAttributes.Id].NotNull()}.");
                return;
            }
        });

        return hasFailed.Value
            ? OperationResult<bool>.Failure(string.Join(Environment.NewLine, errors), HttpStatusCode.InternalServerError)
            : OperationResult<bool>.Success(true);
    }
    public async Task<OperationResult<bool>> FixTheDeleteForRelevantPostTypesAsync(
        List<string> relevantPostTypes,
        string conditionIdAttributePeekAllTable,
        string conditionIdAttributeActualTable,
        bool conditionIsArray,
        int oldConditionIdValue,
        string typeNameAttributePeekAllTable,
        CancellationToken cancellationToken)
    {
        var failure = new Atomicable<bool>(false, ThreadSafetyMode.MultipleProducers);

        var errors = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(relevantPostTypes, cancellationToken, async (pType, cancellationTokenForEach) =>
        {
            var actualTableName = RfConfiguration.EntityNameToConfiguration[pType].EntityConfiguration.EntityName;

            if (failure.Value) return;

            var scanTableResult = await _db.ScanTableWithFilterAsync(
                actualTableName,
                conditionIsArray
                    ? _db.ArrayElementExists(conditionIdAttributeActualTable, oldConditionIdValue)
                    : _db.AttributeEquals(conditionIdAttributeActualTable, oldConditionIdValue),
                cancellationTokenForEach);
            if (!scanTableResult.IsSuccessful)
            {
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: ScanTableWithFilterAsync for table {actualTableName} has failed.");
                return;
            }
            var actualRelevantPosts = scanTableResult.Data.Items;

            if (failure.Value) return;

            foreach (var actualRelevantPost in actualRelevantPosts)
            {
                if (failure.Value) return;

                if (conditionIsArray)
                {
                    var newArray = new JArray();
                    var asArray = (JArray)actualRelevantPost[conditionIdAttributeActualTable].NotNull();
                    foreach (var cTok in asArray)
                    {
                        if ((int)cTok == oldConditionIdValue)
                            continue;

                        newArray.Add(cTok);
                    }
                    actualRelevantPost[conditionIdAttributeActualTable] = newArray;
                }
                else
                {
                    actualRelevantPost[conditionIdAttributeActualTable] = -1;
                }

                var updateResult = await _db.UpdateItemAsync(
                    actualTableName,
                    new DbKey(EntityModelAttributes.Id, (long)actualRelevantPost[EntityModelAttributes.Id].NotNull()),
                    actualRelevantPost,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationTokenForEach);
                if (updateResult.IsSuccessful) continue;
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: UpdateItemAsync for table {actualTableName} has failed for id {(long)actualRelevantPost[EntityModelAttributes.Id].NotNull()}.");
                return;
            }

            //
            //
            //

            var peekAllTableName = $"{actualTableName}-peek-overview";

            if (failure.Value) return;

            scanTableResult = await _db.ScanTableWithFilterAsync(
                peekAllTableName,
                conditionIsArray
                    ? _db.ArrayElementExists(conditionIdAttributePeekAllTable, oldConditionIdValue)
                    : _db.AttributeEquals(conditionIdAttributePeekAllTable, oldConditionIdValue),
                cancellationTokenForEach);
            if (!scanTableResult.IsSuccessful)
            {
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: ScanTableWithFilterAsync for table {peekAllTableName} has failed.");
                return;
            }
            var peekAllRelevantPosts = scanTableResult.Data.Items;

            if (failure.Value) return;

            foreach (var peekAllRelevantPost in peekAllRelevantPosts)
            {
                if (failure.Value) return;

                if (conditionIsArray)
                {
                    var displayAsArray = (JArray)peekAllRelevantPost[typeNameAttributePeekAllTable].NotNull();

                    var idsAsArray = (JArray)peekAllRelevantPost[conditionIdAttributePeekAllTable].NotNull();
                    for (var i = 0; i < idsAsArray.Count; i++)
                    {
                        if ((int)idsAsArray[i] != oldConditionIdValue) continue; //Find index. They should have the same index.
                        displayAsArray.RemoveAt(i);
                        idsAsArray.RemoveAt(i);
                        break;
                    }
                }
                else
                {
                    peekAllRelevantPost[conditionIdAttributePeekAllTable] = -1;
                    peekAllRelevantPost[typeNameAttributePeekAllTable] = "";
                }

                var updateResult = await _db.UpdateItemAsync(
                    peekAllTableName,
                    new DbKey(EntityModelAttributes.Id, (long)peekAllRelevantPost[EntityModelAttributes.Id].NotNull()),
                    peekAllRelevantPost,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationTokenForEach);
                if (updateResult.IsSuccessful) continue;
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: UpdateItemAsync for table {peekAllTableName} has failed for id {(long)peekAllRelevantPost[EntityModelAttributes.Id].NotNull()}.");
                return;
            }
        });

        return !failure.Value
            ? OperationResult<bool>.Success(true)
            : OperationResult<bool>.Failure(string.Join(Environment.NewLine, errors), HttpStatusCode.InternalServerError);
    }

    private static void FixBodyForMustHaveFields(string entityName, JObject body)
    {
        //Slug
        {
            string? rawTitle = null;

            if (body.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleObject) && titleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered))
                rawTitle = titleRendered;

            if (rawTitle != null)
            {
                body[EntityModelAttributes.Slug] = rawTitle.SanitizeToSlug();
            }
        }

        //Link
        if (!body.TryGetTypedValue(EntityModelAttributes.Id, out int bodyId))
        {
            bodyId = -1;
            //In case this is creating operation, we will create the link later in the flow.
        }
        else
        {
            body[EntityModelAttributes.Link] = RfConfiguration.EndpointConfiguration.GetEntityUrl(entityName, bodyId);
        }

        var gmtNowTimeString = DateUtility.DateTimeToDesiredString(DateTime.UtcNow);
        var localNowTimeString = DateUtility.DateTimeToDesiredString(DateTime.UtcNow.ToLocalTime());

        if (!body.ContainsKey(EntityModelAttributes.DateGmt))
        {
            if (!body.ContainsKey(EntityModelAttributes.ModifiedGmt))
            {
                body[EntityModelAttributes.DateGmt] = gmtNowTimeString;
                body[EntityModelAttributes.Date] = localNowTimeString;
            }
            else
            {
                if (body.TryGetTypedValue(EntityModelAttributes.ModifiedGmt, out string? modifiedGmtString)
                    && (DateUtility.FromDesiredStringToDateTime(modifiedGmtString, out var modifiedGmt)
                        || (modifiedGmtString != null
                            && modifiedGmtString.EndsWith("Z", StringComparison.Ordinal)
                            && DateTime.TryParseExact(modifiedGmtString[..^1], s_nonCanonicalIso8601NoZFormats, null, DateTimeStyles.None, out modifiedGmt))))
                {
                    body[EntityModelAttributes.DateGmt] = DateUtility.DateTimeToDesiredString(modifiedGmt);
                    body[EntityModelAttributes.Date] = DateUtility.DateTimeToDesiredString(modifiedGmt.ToLocalTime());
                }
                else
                {
                    RfConfiguration.LogError(new Exception($"Warning: FixBodyForMustHaveFields-> Failed to parse {modifiedGmtString} into DateTime. Fallen back to current date. Entity: {entityName} entity {EntityModelAttributes.Id}: {bodyId}"));

                    body[EntityModelAttributes.DateGmt] = gmtNowTimeString;
                    body[EntityModelAttributes.Date] = localNowTimeString;
                }
            }
        }
        else
        {
            if (body[EntityModelAttributes.DateGmt] is { Type: JTokenType.Date })
            {
                var dateGmt = (DateTime)body[EntityModelAttributes.DateGmt].NotNull();
                body[EntityModelAttributes.DateGmt] = DateUtility.DateTimeToDesiredString(dateGmt);
            }
            else if (body[EntityModelAttributes.DateGmt]?.Type == JTokenType.String)
            {
                var s = body[EntityModelAttributes.DateGmt]!.Value<string>();
                if (s != null
                    && !DateUtility.FromDesiredStringToDateTime(s, out _)
                    && s.EndsWith("Z", StringComparison.Ordinal)
                    && DateTime.TryParseExact(s[..^1], s_nonCanonicalIso8601NoZFormats, null, DateTimeStyles.None, out var parsedGmt))
                    body[EntityModelAttributes.DateGmt] = DateUtility.DateTimeToDesiredString(parsedGmt);
            }
            if (body[EntityModelAttributes.Date] is { Type: JTokenType.Date })
            {
                var date = (DateTime)body[EntityModelAttributes.Date].NotNull();
                body[EntityModelAttributes.Date] = DateUtility.DateTimeToDesiredString(date);
            }
            else if (body[EntityModelAttributes.Date]?.Type == JTokenType.String)
            {
                var s = body[EntityModelAttributes.Date]!.Value<string>();
                if (s != null
                    && !DateUtility.FromDesiredStringToDateTime(s, out _)
                    && s.EndsWith("Z", StringComparison.Ordinal)
                    && DateTime.TryParseExact(s[..^1], s_nonCanonicalIso8601NoZFormats, null, DateTimeStyles.None, out var parsedDate))
                    body[EntityModelAttributes.Date] = DateUtility.DateTimeToDesiredString(parsedDate);
            }
        }

        body[EntityModelAttributes.ModifiedGmt] = gmtNowTimeString;
        body[EntityModelAttributes.Modified] = localNowTimeString;
    }

    // ISO 8601 formats WITHOUT the trailing "Z" literal, used with s[..^1] to strip
    // the "Z" before parsing. This prevents DateTime.TryParseExact from treating "Z"
    // as a UTC timezone indicator and converting the result to local time.
    private static readonly string[] s_nonCanonicalIso8601NoZFormats =
    [
        "yyyy-MM-ddTHH:mm:ss.fffffff", // 7-decimal — Newtonsoft "O" round-trip format
        "yyyy-MM-ddTHH:mm:ss.ffffff",  // 6-decimal
        "yyyy-MM-ddTHH:mm:ss.fffff",   // 5-decimal
        "yyyy-MM-ddTHH:mm:ss.ffff",    // 4-decimal
        "yyyy-MM-ddTHH:mm:ss.ff",      // 2-decimal (FFFFFFF with 10ms precision)
        "yyyy-MM-ddTHH:mm:ss.f",       // 1-decimal (FFFFFFF with 100ms precision)
        "yyyy-MM-ddTHH:mm:ss",         // no decimal (FFFFFFF with ms=0)
    ];

    private const string DeleteOneAsyncMethodName = "DeleteOneAsync";
    public async Task<OperationResult<JObject>> DeleteOneAsync<T>(string entityName, int id, int requestingUserId, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        // Note: If parameters of this method change, remember to update Crud.cs as well. Reflection is used there.

        if (requestingUserId > 0)
        {
            var lockCheckResult = await CheckEntityNotLockedByAnotherUserAsync(entityName, id, requestingUserId, cancellationToken);
            if (!lockCheckResult.IsSuccessful)
                return OperationResult<JObject>.Failure(lockCheckResult.ErrorMessage, lockCheckResult.StatusCode);
        }

        var key = new DbKey(EntityModelAttributes.Id, id);

        JObject? lastBody;
        {
            await using var mutex = await MemoryScopeMutex.CreateEntityScopeAsync(
                MemoryServiceInstance,
                _mutexScope,
                $"{EntityModelAttributes.Id}:{id}",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            var deleteResult = await _db.DeleteItemAsync(
                GetEntityTableName(entityName),
                key,
                DbReturnItemBehavior.ReturnOldValues,
                null,
                cancellationToken);
            if (!deleteResult.IsSuccessful)
            {
                var existsResult = await _db.ItemExistsAsync(
                    GetEntityTableName(entityName),
                    key,
                    null,
                    cancellationToken);
                if (existsResult is { IsSuccessful: true, Data: true })
                {
                    return OperationResult<JObject>.Failure($"Error: EntityRepository->DeleteOneAsync: DeleteItemAsync failed. Item exists. (Item-Self) Id: {id}", existsResult.StatusCode);
                }
            }
            lastBody = deleteResult.Data;

            var deletePeekOverviewResult = await _db.DeleteItemAsync(
                GetEntityPeekOverviewTableName(entityName),
                key,
                DbReturnItemBehavior.DoNotReturn,
                null,
                cancellationToken);
            if (!deletePeekOverviewResult.IsSuccessful)
            {
                var existsResult = await _db.ItemExistsAsync(
                    GetEntityPeekOverviewTableName(entityName),
                    key,
                    null,
                    cancellationToken);
                if (existsResult is { IsSuccessful: true, Data: true })
                {
                    return OperationResult<JObject>.Failure($"Error: EntityRepository->DeleteOneAsync: DeleteItemAsync failed. Item exists. (Peek-Overview) Id: {id}", existsResult.StatusCode);
                }
            }

            var fixOthersResult = await FixTheDeleteForOthersThatHaveReferenceToThisAsync(entityName, id, cancellationToken);
            if (!fixOthersResult.IsSuccessful)
            {
                return OperationResult<JObject>.Failure($"Warning: EntityRepository->DeleteOneAsync: FixTheDelete_ForOthersThatHaveReferenceToThis has failed. Id: {id} Entity: {entityName}", fixOthersResult.StatusCode);
            }
        }

        // Run the post-delete hook OUTSIDE the mutex to avoid deadlock
        // when the hook calls UpdateOneAsync/DeleteOneAsync on the same entity.
        await PostDeleteHook<T>(entityName, id, lastBody.NotNull(), cancellationToken);

        // Vector deletion (best-effort) — after hook, before publish
        if (RfConfiguration.AiServiceConfiguration != null &&
            RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.SupportsSemanticSearch)
        {
            try
            {
                await Ai.AiVectorIndexer.DeleteEntityAsync(entityName, id);
            }
            catch (Exception ex)
            {
                RfConfiguration.LogError(ex);
            }
        }

        await PublishEntityChangedAsync<T>(
            entityName,
            id,
            EntityChangedEventType.Deleted,
            lastBody,
            null,
            cancellationToken);

        return OperationResult<JObject>.Success(lastBody.NotNull());
    }

    public async Task<OperationResult<JArray>> PeekAllAsync(string entityName, CancellationToken cancellationToken)
    {
        var scanResult = await _db.ScanTableAsync(
            GetEntityPeekOverviewTableName(entityName),
            cancellationToken);
        return !scanResult.IsSuccessful
            ? OperationResult<JArray>.Failure($"Error: EntityRepository->PeekAllAsync: ScanTableAsync has failed with: {scanResult.ErrorMessage}", scanResult.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(scanResult.Data.Items));
    }

    public async Task<OperationResult<JArray>> FullReadAllAsync(string entityName, CancellationToken cancellationToken)
    {
        var scanResult = await _db.ScanTableAsync(
            GetEntityTableName(entityName),
            cancellationToken);
        if (!scanResult.IsSuccessful)
            return OperationResult<JArray>.Failure($"Error: EntityRepository->FullReadAllAsync: ScanTableAsync has failed with: {scanResult.ErrorMessage}", scanResult.StatusCode);

        // Merge C# model defaults into each full entity
        var configuration = RfConfiguration.EntityNameToConfiguration[entityName];
        var items = new JArray();
        foreach (var item in scanResult.Data.Items)
            items.Add(EntityDefaultsMerger.MergeDefaults(item, configuration));
        return OperationResult<JArray>.Success(items);
    }

    public async Task<OperationResult<JObject>> PeekAllPaginatedAsync(
        string entityName,
        int pageSize,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var scanResult = await _db.ScanTablePaginatedAsync(
            GetEntityPeekOverviewTableName(entityName),
            pageSize,
            pageToken,
            cancellationToken);

        if (!scanResult.IsSuccessful)
            return OperationResult<JObject>.Failure(
                $"Error: EntityRepository->PeekAllPaginatedAsync: ScanTablePaginatedAsync has failed with: {scanResult.ErrorMessage}",
                scanResult.StatusCode);

        var result = new JObject
        {
            ["items"] = ListOfJObjectToJArray(scanResult.Data.Items),
            ["next_page_token"] = scanResult.Data.NextPageToken,
            ["total_count"] = scanResult.Data.TotalCount
        };
        return OperationResult<JObject>.Success(result);
    }

    private async Task<OperationResult<JObject>> TryExtractingPeekOverviewFromBodyAsync(string entityName, JObject? body, CancellationToken cancellationToken)
    {
        if (body == null)
            return OperationResult<JObject>.Failure("Body is null", HttpStatusCode.InternalServerError);
        if (!body.TryGetTypedValue(EntityModelAttributes.Id, out int id))
            return OperationResult<JObject>.Failure($"Body does not have {EntityModelAttributes.Id}", HttpStatusCode.InternalServerError);

        var result = new JObject
        {
            [EntityModelAttributes.Id] = id
        };

        if (body.TryGetTypedValue(EntityModelAttributes.Author, out int authorId) && authorId > 0)
        {
            var extractionResult = await TryGetOneInternalForPeekOverviewExtractionAsync(RfReservedEntities.UsersEntityName, authorId, cancellationToken);
            if (!extractionResult.IsSuccessful
                || !extractionResult.Data.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleObj)
                || !titleObj.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? authorDisplayName))
            {
                result[EntityModelAttributes.Author] = $"User: {authorId}";
            }
            else
            {
                result[EntityModelAttributes.Author] = authorDisplayName;
            }
            result[$"{EntityModelAttributes.Author}_{EntityModelAttributes.Id}"] = authorId;
        }

        if (body.TryGetTypedValue(EntityModelAttributes.Parent, out int parentId) && parentId > 0)
        {
            var extractionResult = await TryGetOneInternalForPeekOverviewExtractionAsync(entityName, parentId, cancellationToken);
            if (!extractionResult.IsSuccessful)
            {
                result[EntityModelAttributes.Parent] = $"Parent: {parentId}";
            }
            else
            {
                var parentObject = extractionResult.Data;

                string? parentDisplayName = null;

                if (parentObject.TryGetTypedValue(EntityModelAttributes.Title, out JObject? parentTitleObject) && parentTitleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? parentTitleRendered))
                    parentDisplayName = parentTitleRendered;

                if (parentDisplayName != null)
                {
                    result[EntityModelAttributes.Parent] = parentDisplayName;
                }
                else
                {
                    result[EntityModelAttributes.Parent] = $"Parent: {parentId}";
                }
            }
            result[$"{EntityModelAttributes.Parent}_{EntityModelAttributes.Id}"] = parentId;
        }

        if (body.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleObject) && titleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered))
            result[EntityModelAttributes.Title] = titleRendered;

        if (entityName != RfReservedEntities.TagsEntityName && entityName != RfReservedEntities.CategoriesEntityName)
        {
            var categoriesJArray = new JArray();
            var categoriesIdsJArray = new JArray();
            result[EntityModelAttributes.Categories] = categoriesJArray;
            result[$"{EntityModelAttributes.Categories}_{EntityModelAttributes.Id}s"] = categoriesIdsJArray;
            if (body.TryGetTypedValue(EntityModelAttributes.Categories, out JArray? categories))
            {
                foreach (var categoryIdToken in categories.NotNull())
                {
                    var categoryId = (int)categoryIdToken;

                    var extractionResult = await TryGetOneInternalForPeekOverviewExtractionAsync(RfReservedEntities.CategoriesEntityName, categoryId, cancellationToken);
                    if (!extractionResult.IsSuccessful
                        || !extractionResult.Data.TryGetTypedValue(EntityModelAttributes.Title, out JObject? categoryTitleObject)
                        || !categoryTitleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? categoryDisplayName))
                    {
                        categoriesJArray.Add($"Category: {categoryId}");
                    }
                    else
                    {
                        categoriesJArray.Add(categoryDisplayName);
                    }
                    categoriesIdsJArray.Add(categoryId);
                }
            }

            var tagsJArray = new JArray();
            var tagsIdsJArray = new JArray();
            result[EntityModelAttributes.Tags] = tagsJArray;
            result[$"{EntityModelAttributes.Tags}_{EntityModelAttributes.Id}s"] = tagsIdsJArray;
            if (body.TryGetTypedValue(EntityModelAttributes.Tags, out JArray? tags))
            {
                foreach (var tagIdToken in tags.NotNull())
                {
                    var tagId = (int)tagIdToken;

                    var extractionResult = await TryGetOneInternalForPeekOverviewExtractionAsync(RfReservedEntities.TagsEntityName, tagId, cancellationToken);
                    if (!extractionResult.IsSuccessful
                        || !extractionResult.Data.TryGetTypedValue(EntityModelAttributes.Title, out JObject? tagTitleObject)
                        || !tagTitleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? tagDisplayName))
                    {
                        tagsJArray.Add($"Tag: {tagId}");
                    }
                    else
                    {
                        tagsJArray.Add(tagDisplayName);
                    }
                    tagsIdsJArray.Add(tagId);
                }
            }
        }

        if (body.TryGetTypedValue(EntityModelAttributes.DateGmt, out string? dateGmt))
            result[EntityModelAttributes.DateGmt] = dateGmt;

        if (body.TryGetTypedValue(EntityModelAttributes.Date, out string? date))
            result[EntityModelAttributes.Date] = date;

        if (body.TryGetTypedValue(EntityModelAttributes.ModifiedGmt, out string? lastModifiedGmt))
            result[EntityModelAttributes.ModifiedGmt] = lastModifiedGmt;

        if (body.TryGetTypedValue(EntityModelAttributes.Modified, out string? lastModified))
            result[EntityModelAttributes.Modified] = lastModified;

        return OperationResult<JObject>.Success(result);
    }

    private async Task<OperationResult<JObject>> TryGetOneInternalForPeekOverviewExtractionAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        return await GetOneAsync(entityName, id, cancellationToken);
    }

    private static JArray ListOfJObjectToJArray(IReadOnlyList<JObject> input)
    {
        var output = new JArray();
        foreach (var item in input)
        {
            output.Add(item);
        }
        return output;
    }

    private static async Task PostCreateHook<T>(string entityName, int newId, JObject body, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        var finalConfig = RfConfiguration.EntityNameToConfiguration[entityName];
        var builder = (EntityConfigurationBuilder<T>)finalConfig.EntityConfiguration;
        if (builder.HooksSetup?.PostCreateHook == null) return;

        await builder.HooksSetup.PostCreateHook(
            new PostCreateHookModel<T>(
                entityName,
                newId,
                (EntityModel<T>)body.ToObjectWithPolymorphism(finalConfig.EntityModelType).NotNull()), cancellationToken);
    }
    private static async Task PostUpdateHook<T>(string entityName, int id, JObject oldBody, JObject newBody, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        var finalConfig = RfConfiguration.EntityNameToConfiguration[entityName];
        var builder = (EntityConfigurationBuilder<T>)finalConfig.EntityConfiguration;
        if (builder.HooksSetup?.PostUpdateHook == null) return;

        await builder.HooksSetup.PostUpdateHook(
            new PostUpdateHookModel<T>(
                entityName,
                id,
                (EntityModel<T>)oldBody.ToObjectWithPolymorphism(finalConfig.EntityModelType).NotNull(),
                (EntityModel<T>)newBody.ToObjectWithPolymorphism(finalConfig.EntityModelType).NotNull()), cancellationToken);
    }
    private static async Task PostDeleteHook<T>(string entityName, int id, JObject lastBody, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        var finalConfig = RfConfiguration.EntityNameToConfiguration[entityName];
        var builder = (EntityConfigurationBuilder<T>)finalConfig.EntityConfiguration;
        if (builder.HooksSetup?.PostDeleteHook == null) return;

        await builder.HooksSetup.PostDeleteHook(
            new PostDeleteHookModel<T>(
                entityName,
                id,
                (EntityModel<T>)lastBody.ToObjectWithPolymorphism(finalConfig.EntityModelType).NotNull()), cancellationToken);
    }

    internal static readonly MethodInfo PutOneAsyncMethodInfo =
        typeof(EntityRepositoryService)
            .GetMethod(
                PutOneAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance).NotNull();
    internal static readonly MethodInfo UpdateOneAsyncMethodInfo =
        typeof(EntityRepositoryService)
            .GetMethod(
                UpdateOneAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance).NotNull();
    internal static readonly MethodInfo DeleteOneAsyncMethodInfo =
        typeof(EntityRepositoryService)
            .GetMethod(
                DeleteOneAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance).NotNull();

    /// <summary>
    /// Checks if the entity is locked by a different user. Returns success if not locked or locked by the same user.
    /// </summary>
    private static async Task<OperationResult<bool>> CheckEntityNotLockedByAnotherUserAsync(
        string entityName, int entityId, int requestingUserId, CancellationToken cancellationToken)
    {
        var lockStatus = await EntityLockController.GetLockStatusAsync(entityName, entityId, cancellationToken);
        if (!lockStatus.IsSuccessful)
            return OperationResult<bool>.Success(true); // If we can't check, don't block the operation

        var state = lockStatus.Data;
        if (state != null && state.LockedByUserId != requestingUserId)
            return OperationResult<bool>.Failure(
                $"Entity is currently being edited by {state.LockedByUserName ?? "another user"}.",
                HttpStatusCode.Conflict);

        return OperationResult<bool>.Success(true);
    }
}

public enum EntityChangedEventType
{
    Created,
    Updated,
    Deleted
}

public record EntityChangedMessage<T>(
    // ReSharper disable once NotAccessedPositionalProperty.Global
    string EntityName,
    int EntityId,
    EntityChangedEventType EventType,
    // ReSharper disable once NotAccessedPositionalProperty.Global
    EntityModel<T>? OldEntityState,
    EntityModel<T>? NewEntityState
) where T : EntityFieldsModel, new();
