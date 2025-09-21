// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Repositories;

public class EntityRepositoryService
{
    internal EntityRepositoryService(EntityRepositoryServiceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration.DatabaseService);
        if (!configuration.DatabaseService.IsInitialized)
            throw new InvalidOperationException("DatabaseService is not initialized.");

        ArgumentNullException.ThrowIfNull(configuration.MemoryService);
        if (!configuration.MemoryService.IsInitialized)
            throw new InvalidOperationException("MemoryService is not initialized.");

        _databaseService = configuration.DatabaseService;
        _pubSubService = configuration.PubSubService;
        MemoryServiceInstance = configuration.MemoryService;
        FileServiceConfiguration = configuration.FileServiceConfiguration;

        // ReSharper disable once SuspiciousTypeConversion.Global
        (_databaseService as DatabaseServiceBase)?.SetOptions(new DbOptions
        {
            AutoSortArrays = DbAutoSortArrays.No,
            AutoConvertRoundableFloatToInt = DbAutoConvertRoundableFloatToInt.Yes
        });
    }
    private readonly IDatabaseService _databaseService;
    private readonly IPubSubService _pubSubService;
    internal IMemoryService MemoryServiceInstance { get; }
    internal FileServiceConfiguration FileServiceConfiguration { get; }

    private readonly IMemoryScope _mutexScope = new MemoryScopeLambda("ReflectiveForms.Core.Repositories.EntityRepositoryService");
    private const string PubSubEntityChangedTopicPrefix = "ReflectiveForms.Core.Repositories.EntityRepositoryService.OnEntityChanged";

    private static readonly string[] PossibleKeyNames = [EntityModelAttributes.Id];

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

    private static string GetEntityTableName(string entityName) => entityName;
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

    public async Task<OperationResult<JArray>> GetAllAsync(string entityName, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<JArray>.Failure($"GetAllAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(result.Data));
    }

    public async Task<OperationResult<JArray>> GetAllByAuthorIdAsync(string entityName, int authorId, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableWithFilterAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            _databaseService.BuildAttributeEqualsCondition(EntityModelAttributes.Author, new PrimitiveType(authorId)),
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<JArray>.Failure($"GetAllByAuthorIdAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(result.Data));
    }

    public async Task<OperationResult<JArray>> GetAllByFilterAsync(string entityName, Func<JToken, bool> filter, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<JArray>.Failure($"GetAllByFilterAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(result.Data, filter));
    }

    public async Task<OperationResult<JArray>> GetAllByFilterByAuthorIdAsync(string entityName, int authorId, Func<JToken, bool> filter, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableWithFilterAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            _databaseService.BuildAttributeEqualsCondition(EntityModelAttributes.Author, new PrimitiveType(authorId)),
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<JArray>.Failure($"GetAllByFilterByAuthorIdAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(result.Data, filter));
    }

    public async Task<OperationResult<JArray>> GetAllByFilterByTagIdAsync(string entityName, int tagId, Func<JToken, bool> filter, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableWithFilterAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            _databaseService.BuildArrayElementExistsCondition(EntityModelAttributes.Tags, new PrimitiveType(tagId)),
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<JArray>.Failure($"GetAllByFilterByTagIdAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(result.Data, filter));
    }

    public async Task<OperationResult<JArray>> GetAllByTagIdAsync(string entityName, int tagId, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableWithFilterAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            _databaseService.BuildArrayElementExistsCondition(EntityModelAttributes.Tags, new PrimitiveType(tagId)),
            cancellationToken);
        return !result.IsSuccessful
            ? OperationResult<JArray>.Failure($"GetAllByTagIdAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(result.Data));
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public async Task<OperationResult<JObject>> GetOneAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var result = await _databaseService.GetItemAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames[0],
            new PrimitiveType(id),
            null,
            cancellationToken);
        return !result.IsSuccessful
            ?
            OperationResult<JObject>.Failure($"GetOneAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : result.Data == null
                ? OperationResult<JObject>.Failure("Not found.", HttpStatusCode.NotFound)
                : OperationResult<JObject>.Success(result.Data);
    }
    public async Task<OperationResult<bool>> DoesExistAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ItemExistsAsync(
            GetEntityPeekOverviewTableName(entityName),
            PossibleKeyNames[0],
            new PrimitiveType(id),
            null,
            cancellationToken);
        return !result.IsSuccessful
            ?
            OperationResult<bool>.Failure($"DoesExistAsync has failed with: {result.ErrorMessage}", result.StatusCode)
            : !result.Data
                ? OperationResult<bool>.Failure($"Not found.", HttpStatusCode.NotFound)
                : OperationResult<bool>.Success(true);
    }
    public async Task<OperationResult<JObject>> GetOneFromAllByAuthorIdByFilter(string entityName, int authorId, Func<JToken, bool> filter, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableWithFilterAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            _databaseService.BuildAttributeEqualsCondition(EntityModelAttributes.Author, new PrimitiveType(authorId)),
            cancellationToken);

        if (!result.IsSuccessful)
            return OperationResult<JObject>.Failure($"GetOneFromAllByAuthorIdByFilter has failed with: {result.ErrorMessage}", result.StatusCode);

        var filtered = InternalGetOneFromAll(result.Data, filter);
        return filtered == null
            ? OperationResult<JObject>.Failure($"Not found.", HttpStatusCode.NotFound)
            : OperationResult<JObject>.Success(filtered);
    }

    public async Task<OperationResult<JObject>> GetOneFromAllByFilter(string entityName, Func<JToken, bool> filter, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            cancellationToken);

        if (!result.IsSuccessful)
            return OperationResult<JObject>.Failure($"GetOneFromAllByFilter has failed with: {result.ErrorMessage}", result.StatusCode);

        var filtered = InternalGetOneFromAll(result.Data, filter);
        return filtered == null
            ? OperationResult<JObject>.Failure("Not found.", HttpStatusCode.NotFound)
            : OperationResult<JObject>.Success(filtered);
    }

    public async Task<OperationResult<JObject>> GetOneFromAllByTagIdByFilter(string entityName, int tagId, Func<JToken, bool> filter, CancellationToken cancellationToken)
    {
        var result = await _databaseService.ScanTableWithFilterAsync(
            GetEntityTableName(entityName),
            PossibleKeyNames,
            _databaseService.BuildArrayElementExistsCondition(EntityModelAttributes.Tags, new PrimitiveType(tagId)),
            cancellationToken);

        if (!result.IsSuccessful)
            return OperationResult<JObject>.Failure($"GetOneFromAllByTagIdByFilter has failed with: {result.ErrorMessage}", result.StatusCode);

        var filtered = InternalGetOneFromAll(result.Data, filter);
        return filtered == null
            ? OperationResult<JObject>.Failure("Not found.", HttpStatusCode.NotFound)
            : OperationResult<JObject>.Success(filtered);
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
            var incrementResult = await _databaseService.IncrementAttributeAsync(GlobalIndexesTableName,
                PossibleKeyNames[0],
                new PrimitiveType(GlobalIndexesKeyName),
                GlobalIndexesLastValueAttributeName,
                1.0,
                null,
                cancellationToken);
            if (!incrementResult.IsSuccessful)
                return OperationResult<JObject>.Failure($"PutOneAsync has failed with: {incrementResult.ErrorMessage}", incrementResult.StatusCode);

            newId = (int)incrementResult.Data;

            await using var mutex = await MemoryScopeMutex.CreateScopeAsync(
                MemoryServiceInstance,
                _mutexScope,
                $"{PossibleKeyNames[0]}:{newId}",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            var idAsPrimitive = new PrimitiveType(newId);

            body[PossibleKeyNames[0]] = newId;
            body[EntityModelAttributes.Link] = $"{RfConfiguration.EndpointConfiguration.FinalEntitiesBaseRoute}?type={entityName}&id={newId}";

            var configuration = RfConfiguration.EntityNameToConfiguration[entityName];

            var newBody = (JObject)configuration.DefaultJObject.DeepClone();
            newBody.Merge(body, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace
            });

            var sanityCheck = await configuration.UpsertSanityCheck((newBody, cancellationToken));
            if (!sanityCheck.IsSuccessful)
                return OperationResult<JObject>.Failure($"Sanity check for {entityName}, entity {PossibleKeyNames[0]}: {newId} has failed with {sanityCheck.ErrorMessage}", HttpStatusCode.BadRequest);

            var putItemResult = await _databaseService.PutItemAsync(
                GetEntityTableName(entityName),
                PossibleKeyNames[0],
                idAsPrimitive,
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

                var updateResult = await _databaseService.UpdateItemAsync(
                    GetEntityPeekOverviewTableName(entityName),
                    PossibleKeyNames[0],
                    idAsPrimitive,
                    peekOverview,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationToken);
                if (!updateResult.IsSuccessful)
                {
                    var error = $"Error: EntityRepository->PutOneAsync: PutItemAsync has succeeded, but UpdateItemAsync has failed. Id: {idAsPrimitive.AsInteger} Peek Overview: {peekOverview} ({updateResult.StatusCode})";
                    var deleteResult = await _databaseService.DeleteItemAsync(
                        GetEntityTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);
                    if (!deleteResult.IsSuccessful)
                    {
                        return OperationResult<JObject>.Failure(
                            $"FATAL ERROR: EntityRepository->PutOneAsync: DeleteItemAsync has failed. (Failed: Remove back {entityName}->{newId}) ({deleteResult.StatusCode})" +
                            $"{Environment.NewLine}{error}", HttpStatusCode.InternalServerError);
                    }
                    return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);
                }
            }

            await PostCreateHook<T>(entityName, newId, result, cancellationToken);

            break;
        }
        if (!success)
        {
            return OperationResult<JObject>.Failure($"EntityRepository->PutOneAsync: PutItemAsync has failed for entity type {entityName}.", HttpStatusCode.InternalServerError);
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

        body[PossibleKeyNames[0]] = id;

        JObject? result;
        JObject? oldObject;

        FixBodyForMustHaveFields(entityName, body);
        {
            await using var mutex = await MemoryScopeMutex.CreateScopeAsync(
                MemoryServiceInstance,
                _mutexScope,
                $"{PossibleKeyNames[0]}:{id}",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            var idAsPrimitive = new PrimitiveType(id);

            var getItemResult = await _databaseService.GetItemAsync(
                GetEntityTableName(entityName),
                PossibleKeyNames[0],
                idAsPrimitive,
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
                var revertBackUpdateResult = await _databaseService.UpdateItemAsync(
                    GetEntityTableName(entityName),
                    PossibleKeyNames[0],
                    idAsPrimitive,
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

            var newBody = (JObject)oldObject.DeepClone();
            newBody.Merge(body, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace
            });

            var sanityCheckResult = await RfConfiguration.EntityNameToConfiguration[entityName].UpsertSanityCheck((newBody, cancellationToken));
            if (!sanityCheckResult.IsSuccessful)
            {
                return OperationResult<JObject>.Failure($"Sanity check for {entityName}, entity {PossibleKeyNames[0]}: {id} has failed with {sanityCheckResult.ErrorMessage}", HttpStatusCode.BadRequest);
            }

            var updateResult = await _databaseService.UpdateItemAsync(
                GetEntityTableName(entityName),
                PossibleKeyNames[0],
                idAsPrimitive,
                newBody,
                DbReturnItemBehavior.ReturnNewValues,
                _databaseService.BuildAttributeEqualsCondition(PossibleKeyNames[0], idAsPrimitive),
                cancellationToken);
            if (!updateResult.IsSuccessful)
            {
                return OperationResult<JObject>.Failure($"EntityRepository->UpdateOneAsync: UpdateItemAsync has failed. Id: {id} Entity Name: {entityName}", updateResult.StatusCode);
            }

            result = updateResult.Data;

            JObject? previousOldRevisionsState = null;
            if (!entityUpdaterIdentity.IsDuringHookUpdate)
            {
                var historyGetItemResult = await _databaseService.GetItemAsync(
                    GetEntityHistoryTableName(entityName),
                    PossibleKeyNames[0],
                    idAsPrimitive,
                    null,
                    cancellationToken);
                if (!historyGetItemResult.IsSuccessful)
                {
                    return await RevertBackUpdate($"Error: EntityRepository->UpdateOneAsync: GetItemAsync for {entityName}-history has failed. Id: {idAsPrimitive.AsInteger} ({historyGetItemResult.StatusCode})");
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

                    var historyPutItemResult = await _databaseService.PutItemAsync(
                        GetEntityHistoryTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
                        oldRevisions,
                        DbReturnItemBehavior.DoNotReturn,
                        false,
                        cancellationToken);
                    if (!historyPutItemResult.IsSuccessful)
                    {
                        return await RevertBackUpdate($"Error: EntityRepository->UpdateOneAsync: PutItemAsync for {entityName}-history has failed. Id: {idAsPrimitive.AsInteger} ({historyPutItemResult.StatusCode})");
                    }
                }
                else
                {
                    previousOldRevisionsState = (JObject)oldRevisions.DeepClone();

                    var oldRevisionsCount = (int)oldRevisions[HistoryTableOldRevisionsCountAttributeName].NotNull() + 1;
                    oldRevisions[HistoryTableOldRevisionsCountAttributeName] = oldRevisionsCount;
                    oldRevisions.Add($"{HistoryTableOldRevisionContainerAttributeNamePrefix}{oldRevisionsCount}", oldRevisionsNewObject);

                    var updateHistoryResult = await _databaseService.UpdateItemAsync(
                        GetEntityHistoryTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
                        oldRevisions,
                        DbReturnItemBehavior.DoNotReturn,
                        null,
                        cancellationToken);
                    if (!updateHistoryResult.IsSuccessful)
                    {
                        return await RevertBackUpdate($"Error: EntityRepository->UpdateOneAsync: UpdateItemAsync for {entityName}-history has failed. Id: {idAsPrimitive.AsInteger} ({updateHistoryResult.StatusCode})");
                    }
                }
            }

            async Task<OperationResult<JObject>> RevertBackRevisionsUpdate(string error)
            {
                if (entityUpdaterIdentity.IsDuringHookUpdate)
                    return OperationResult<JObject>.Failure(error, HttpStatusCode.InternalServerError);

                if (previousOldRevisionsState != null)
                {
                    var revertBackRevisionsUpdateResult = await _databaseService.UpdateItemAsync(
                        GetEntityHistoryTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
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
                    var revertBackRevisionsDeleteResult = await _databaseService.DeleteItemAsync(
                        GetEntityHistoryTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
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

                var updatePeekOverviewResult = await _databaseService.UpdateItemAsync(
                    GetEntityPeekOverviewTableName(entityName),
                    PossibleKeyNames[0],
                    idAsPrimitive,
                    peekOverview,
                    DbReturnItemBehavior.ReturnOldValues,
                    null,
                    cancellationToken);
                if (!updatePeekOverviewResult.IsSuccessful)
                {
                    return
                        await RevertBackRevisionsUpdate(
                        (await RevertBackUpdate(
                            $"Error: EntityRepository->UpdateOneAsync: UpdateItemAsync for {entityName}-peek-overview has failed. Id: {idAsPrimitive.AsInteger} Peek Overview: {peekOverview} ({updatePeekOverviewResult.StatusCode})"))
                                .ErrorMessage);
                }
                oldPeekOverviewExtractSucceeded = true;
                oldPeekOverview = updatePeekOverviewResult.Data; //Might be null
            }

            async Task<OperationResult<JObject>> RevertBackPeekOverviewUpdate(string error)
            {
                if (oldPeekOverview != null)
                {
                    var revertBackPeekOverviewUpdateResult = await _databaseService.UpdateItemAsync(
                        GetEntityPeekOverviewTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
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
                    var revertBackPeekOverviewDeleteResult = await _databaseService.DeleteItemAsync(
                        GetEntityPeekOverviewTableName(entityName),
                        PossibleKeyNames[0],
                        idAsPrimitive,
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

            await PostUpdateHook<T>(entityName, id, oldObject, result.NotNull(), cancellationToken);
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
        var getItemResult = await _databaseService.GetItemAsync(
            GetEntityHistoryTableName(entityName),
            PossibleKeyNames[0],
            new PrimitiveType(id),
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

            var scanResult = await _databaseService.ScanTableWithFilterAsync(
                tableName,
                PossibleKeyNames,
                conditionIsArray
                    ? _databaseService.BuildArrayElementExistsCondition(conditionIdAttribute,
                        new PrimitiveType(conditionIdValue))
                    : _databaseService.BuildAttributeEqualsCondition(conditionIdAttribute,
                        new PrimitiveType(conditionIdValue)),
                cancellationTokenForEach);
            if (!scanResult.IsSuccessful)
            {
                if (hasFailed.Value) return;
                hasFailed.Value = true;
                errors.Add($"Error: FixTheUpdateForRelevantPostTypesAsync: ScanTableWithFilterAsync for table {tableName} has failed.");
                return;
            }

            if (hasFailed.Value) return;

            var relevantPosts = scanResult.Data;

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

                var updateResult = await _databaseService.UpdateItemAsync(
                    tableName,
                    PossibleKeyNames[0],
                    new PrimitiveType((long)relevantPost[PossibleKeyNames[0]].NotNull()),
                    relevantPost,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationTokenForEach);
                if (updateResult.IsSuccessful) continue;
                if (hasFailed.Value) return;
                hasFailed.Value = true;
                errors.Add($"Error: FixTheUpdateForRelevantPostTypesAsync: UpdateItem for table {tableName} has failed for id {(long)relevantPost[PossibleKeyNames[0]].NotNull()}.");
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

            var scanTableResult = await _databaseService.ScanTableWithFilterAsync(
                actualTableName,
                PossibleKeyNames,
                conditionIsArray
                    ? _databaseService.BuildArrayElementExistsCondition(conditionIdAttributeActualTable,
                        new PrimitiveType(oldConditionIdValue))
                    : _databaseService.BuildAttributeEqualsCondition(conditionIdAttributeActualTable,
                        new PrimitiveType(oldConditionIdValue)),
                cancellationTokenForEach);
            if (!scanTableResult.IsSuccessful)
            {
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: ScanTableWithFilterAsync for table {actualTableName} has failed.");
                return;
            }
            var actualRelevantPosts = scanTableResult.Data;

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

                var updateResult = await _databaseService.UpdateItemAsync(
                    actualTableName,
                    PossibleKeyNames[0],
                    new PrimitiveType((long)actualRelevantPost[PossibleKeyNames[0]].NotNull()),
                    actualRelevantPost,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationTokenForEach);
                if (updateResult.IsSuccessful) continue;
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: UpdateItemAsync for table {actualTableName} has failed for id {(long)actualRelevantPost[PossibleKeyNames[0]].NotNull()}.");
                return;
            }

            //
            //
            //

            var peekAllTableName = $"{actualTableName}-peek-overview";

            if (failure.Value) return;

            scanTableResult = await _databaseService.ScanTableWithFilterAsync(
                peekAllTableName,
                PossibleKeyNames,
                conditionIsArray
                    ? _databaseService.BuildArrayElementExistsCondition(conditionIdAttributePeekAllTable,
                        new PrimitiveType(oldConditionIdValue))
                    : _databaseService.BuildAttributeEqualsCondition(conditionIdAttributePeekAllTable,
                        new PrimitiveType(oldConditionIdValue)),
                cancellationTokenForEach);
            if (!scanTableResult.IsSuccessful)
            {
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: ScanTableWithFilterAsync for table {peekAllTableName} has failed.");
                return;
            }
            var peekAllRelevantPosts = scanTableResult.Data;

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

                var updateResult = await _databaseService.UpdateItemAsync(
                    peekAllTableName,
                    PossibleKeyNames[0],
                    new PrimitiveType((long)peekAllRelevantPost[PossibleKeyNames[0]].NotNull()),
                    peekAllRelevantPost,
                    DbReturnItemBehavior.DoNotReturn,
                    null,
                    cancellationTokenForEach);
                if (updateResult.IsSuccessful) continue;
                if (failure.Value) return;
                failure.Value = true;
                errors.Add($"Error: FixTheDeleteForRelevantPostTypesAsync: UpdateItemAsync for table {peekAllTableName} has failed for id {(long)peekAllRelevantPost[PossibleKeyNames[0]].NotNull()}.");
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
        if (!body.TryGetTypedValue(PossibleKeyNames[0], out int bodyId))
        {
            bodyId = -1;
            //In case this is creating operation, we will create the link later in the flow.
        }
        else
        {
            body[EntityModelAttributes.Link] = $"{RfConfiguration.EndpointConfiguration.FinalEntitiesBaseRoute}?type={entityName}&id={bodyId}";
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
                    && DateUtility.FromDesiredStringToDateTime(modifiedGmtString, out var modifiedGmt))
                {
                    body[EntityModelAttributes.DateGmt] = modifiedGmtString;
                    body[EntityModelAttributes.Date] = DateUtility.DateTimeToDesiredString(modifiedGmt.AddHours(2));
                }
                else
                {
                    RfConfiguration.LogError(new Exception($"Warning: FixBodyForMustHaveFields-> Failed to parse {modifiedGmtString} into DateTime. Fallen back to current date. Entity: {entityName} entity {PossibleKeyNames[0]}: {bodyId}"));

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
            if (body[EntityModelAttributes.Date] is { Type: JTokenType.Date })
            {
                var date = (DateTime)body[EntityModelAttributes.Date].NotNull();
                body[EntityModelAttributes.Date] = DateUtility.DateTimeToDesiredString(date);
            }
        }

        body[EntityModelAttributes.ModifiedGmt] = gmtNowTimeString;
        body[EntityModelAttributes.Modified] = localNowTimeString;
    }

    private const string DeleteOneAsyncMethodName = "DeleteOneAsync";
    public async Task<OperationResult<JObject>> DeleteOneAsync<T>(string entityName, int id, CancellationToken cancellationToken) where T : EntityFieldsModel, new()
    {
        // Note: If parameters of this method change, remember to update Crud.cs as well. Reflection is used there.

        JObject? lastBody;
        {
            await using var mutex = await MemoryScopeMutex.CreateScopeAsync(
                MemoryServiceInstance,
                _mutexScope,
                $"{PossibleKeyNames[0]}:{id}",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            var deleteResult = await _databaseService.DeleteItemAsync(
                GetEntityTableName(entityName),
                PossibleKeyNames[0],
                new PrimitiveType(id),
                DbReturnItemBehavior.ReturnOldValues,
                null,
                cancellationToken);
            if (!deleteResult.IsSuccessful)
            {
                var existsResult = await _databaseService.ItemExistsAsync(
                    GetEntityTableName(entityName),
                    PossibleKeyNames[0],
                    new PrimitiveType(id),
                    null,
                    cancellationToken);
                if (existsResult is { IsSuccessful: true, Data: true })
                {
                    return OperationResult<JObject>.Failure($"Error: EntityRepository->DeleteOneAsync: DeleteItemAsync failed. Item exists. (Item-Self) Id: {id}", existsResult.StatusCode);
                }
            }
            lastBody = deleteResult.Data;

            var deletePeekOverviewResult = await _databaseService.DeleteItemAsync(
                GetEntityPeekOverviewTableName(entityName),
                PossibleKeyNames[0],
                new PrimitiveType(id),
                DbReturnItemBehavior.DoNotReturn,
                null,
                cancellationToken);
            if (!deletePeekOverviewResult.IsSuccessful)
            {
                var existsResult = await _databaseService.ItemExistsAsync(
                    GetEntityPeekOverviewTableName(entityName),
                    PossibleKeyNames[0],
                    new PrimitiveType(id),
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

            await PostDeleteHook<T>(entityName, id, lastBody.NotNull(), cancellationToken);
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
        var scanResult = await _databaseService.ScanTableAsync(
            GetEntityPeekOverviewTableName(entityName),
            PossibleKeyNames,
            cancellationToken);
        return !scanResult.IsSuccessful
            ? OperationResult<JArray>.Failure($"Error: EntityRepository->PeekAllAsync: ScanTableAsync has failed with: {scanResult.ErrorMessage}", scanResult.StatusCode)
            : OperationResult<JArray>.Success(ListOfJObjectToJArray(scanResult.Data));
    }

    private async Task<OperationResult<JObject>> TryExtractingPeekOverviewFromBodyAsync(string entityName, JObject? body, CancellationToken cancellationToken)
    {
        if (body == null)
            return OperationResult<JObject>.Failure("Body is null", HttpStatusCode.InternalServerError);
        if (!body.TryGetTypedValue(PossibleKeyNames[0], out int id))
            return OperationResult<JObject>.Failure($"Body does not have {PossibleKeyNames[0]}", HttpStatusCode.InternalServerError);

        var result = new JObject
        {
            [PossibleKeyNames[0]] = id
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

    private static JObject? InternalGetOneFromAll(IReadOnlyList<JObject> scanResult, Func<JToken, bool>? filter)
    {
        JObject? result = null;

        if (filter != null)
        {
            foreach (var item in scanResult.Where(item => filter(item)))
            {
                result = item;
                break;
            }
        }
        else if (scanResult.Count > 0)
        {
            result = scanResult[0];
        }
        return result;
    }

    private static JArray ListOfJObjectToJArray(IReadOnlyList<JObject> input, Func<JToken, bool>? filter = null)
    {
        var output = new JArray();
        foreach (var item in input)
        {
            if (filter != null)
            {
                if (filter(item))
                    output.Add(item);
            }
            else
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
