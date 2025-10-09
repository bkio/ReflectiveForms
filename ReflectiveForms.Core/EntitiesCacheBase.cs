// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core;

public class EntitiesCacheBase<T> where T : EntityFieldsModel, new()
{
    protected EntitiesCacheBase(string entityType, CancellationToken cancellationToken = default)
    {
        RfConfiguration.RepositoryService.SubscribeToOnEntitiesChangedAsync<T>(
            entityType,
            message =>
            {
                var (_, entityId, entityChangedEventType, _, newEntityState) = message;
                lock (_entitiesLock)
                {
                    EntityChanged(entityId, entityChangedEventType, newEntityState);
                }
            }, cancellationToken).GetAwaiter().GetResult();

        var enumerator = RfConfiguration.RepositoryService
            .GetAllAsync(entityType, null, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        // Loop synchronously
        var entities = new List<EntityModel<T>>();
        while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
        {
            if (!enumerator.Current.IsSuccessful)
                throw new Exception($"GetAllAsync operation for {entityType} entities has failed with {enumerator.Current.ErrorMessage} ({enumerator.Current.StatusCode})");
            entities.Add(enumerator.Current.Data.ToObjectWithPolymorphism<EntityModel<T>>().NotNull());
        }

        lock (_entitiesLock)
        {
            foreach (var entity in entities)
            {
                _entities[entity.Id] = entity;
            }
        }
    }

    public EntityModel<T>? GetEntityCopy(int entityId)
    {
        EntityModel<T>? entityValue;
        lock (_entitiesLock)
        {
            entityValue = _entities.GetValueOrDefault(entityId);
        }

        return entityValue?.FromObjectWithPolymorphism().ToObjectWithPolymorphism<EntityModel<T>>();
    }

    public IReadOnlyList<EntityModel<T>> FindEntitiesAndGetCopies(Func<EntityModel<T>, bool>? predicate = null)
    {
        List<EntityModel<T>> tmp;
        lock (_entitiesLock)
        {
            tmp = (predicate != null
                ? _entities.Values.Where(predicate)
                : _entities.Values).ToList();
        }
        return tmp.
            Select(e => e.FromObjectWithPolymorphism().ToObjectWithPolymorphism<EntityModel<T>>().NotNull())
            .ToList();
    }

    public JArray FindEntitiesAndGetCopiesAsJArray(Func<EntityModel<T>, bool>? predicate = null)
    {
        List<EntityModel<T>> tmp;
        lock (_entitiesLock)
        {
            tmp = (predicate != null
                ? _entities.Values.Where(predicate)
                : _entities.Values).ToList();
        }

        var filtered = tmp.Select(JObject.FromObject);

        var result = new JArray();
        foreach (var item in filtered)
        {
            result.Add(item);
        }
        return result;
    }

    public EntityModel<T>? FindEntityByFilterAndGetCopy(Func<EntityModel<T>, bool> filter)
    {
        EntityModel<T>? entityValue;
        lock (_entitiesLock)
        {
            entityValue = _entities.Values.FirstOrDefault(filter);
        }
        return entityValue?.FromObjectWithPolymorphism().ToObjectWithPolymorphism<EntityModel<T>>();
    }

    public JObject? FindEntityByFilterAndGetCopyAsJObject(Func<EntityModel<T>, bool> filter)
    {
        EntityModel<T>? entityValue;
        lock (_entitiesLock)
        {
            entityValue = _entities.Values.FirstOrDefault(filter);
        }
        return entityValue?.FromObjectWithPolymorphism();
    }

    private void EntityChanged(int entityId, EntityChangedEventType entityChangedEventType, EntityModel<T>? newEntityState)
    {
        if (entityChangedEventType == EntityChangedEventType.Deleted)
        {
            _entities.Remove(entityId);
        }
        else
        {
            _entities[entityId] = newEntityState.NotNull();
        }
    }
    private readonly object _entitiesLock = new();

    private readonly Dictionary<int, EntityModel<T>> _entities = new();
}
