// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Jint;
using ReflectiveForms.Core.Models;

namespace ReflectiveForms.Core.Operation;

public class EntityOperationState
{
    public static EntityOperationState CreateStateForGeneralPurposes()
    {
        return new EntityOperationState();
    }
    public static EntityOperationState CreateStateForSanityCheck(JObject? reflectiveFieldObj)
    {
        ArgumentNullException.ThrowIfNull(reflectiveFieldObj);

        var instance = new EntityOperationState(new Engine().Execute($$"""

              var test_object = {{reflectiveFieldObj.ToString(Formatting.None)}};

              var visibility_check_map = new Map();
              function setup_test_map(path_including_this, obj) {

                  for (let key in obj) {
                      eval('visibility_check_map.set("test_object' + path_including_this + '.' + key + '", true);');

                      if (Array.isArray(obj[key])) {
                        for (let i = 0; i < obj[key].length; i++) {
                          setup_test_map(path_including_this + '.' + key + '[' + i + ']', obj[key][i]);
                        }
                      }
                      else if (typeof obj[key] === 'object' && obj[key] !== null) {
                          setup_test_map(path_including_this + '.' + key, obj[key]);
                      }
                  }
              }
              setup_test_map('', test_object);

              function feed_condition(path_including_this, condition) {
              let hierarchy = path_including_this.split('.');
              let parentPart = '';

              for (let i = 0; i < hierarchy.length - 1; i++) {
                  parentPart += hierarchy[i] + '.';
              }
              try {
                  eval('if (test_object.' + parentPart + condition + ') {} else visibility_check_map.set("test_object.' + path_including_this + '", false);');
              }
              catch (e) {}
              }

              function remove_invisible(path_including_this) {
              try {
                  eval('if (test_object.' + path_including_this + ' !== undefined) delete test_object.' + path_including_this + ';');
              }
              catch (e) {}
              }

              function test_visibility(path_including_this) {
                  let hierarchy = path_including_this.split('.');
                  let built = hierarchy[0];
                  for (let i = 1; i < hierarchy.length + 1; i++) {
                      if (eval('visibility_check_map.has("test_object.' + built + '")')
                          && !eval('visibility_check_map.get("test_object.' + built + '")'))
                          return false;

                      if (hierarchy.length === i) break;

                      built += '.' + hierarchy[i];
                  }
                  return true;
              }
              """));
        return instance;
    }
    private EntityOperationState(Engine jsEngineForSanityCheckIsElementDisplayedCheck)
    {
        _jsEngineForSanityCheckIsElementDisplayedCheck = jsEngineForSanityCheckIsElementDisplayedCheck;
    }
    private EntityOperationState()
    {
    }
    private readonly Engine? _jsEngineForSanityCheckIsElementDisplayedCheck;

    private bool IsForSanityCheck()
    {
        return _jsEngineForSanityCheckIsElementDisplayedCheck != null;
    }
    public void FeedConditionForSanityCheck(string jObjectPathIncludingThis, string? condition)
    {
        if (!IsForSanityCheck()) return;
        _jsEngineForSanityCheckIsElementDisplayedCheck?.Invoke("feed_condition", jObjectPathIncludingThis.TrimStart('.'), condition);
    }
    public bool TestVisibilityForSanityCheck(string jObjectPathIncludingThis)
    {
        if (!IsForSanityCheck()) return true;
        return (bool)(_jsEngineForSanityCheckIsElementDisplayedCheck?.Invoke("test_visibility", jObjectPathIncludingThis.TrimStart('.')).ToObject() ?? false);
    }
    public void RemoveInvisibleForSanityCheck(string jObjectPathIncludingThis)
    {
        if (!IsForSanityCheck()) return;
        _jsEngineForSanityCheckIsElementDisplayedCheck?.Invoke("remove_invisible", jObjectPathIncludingThis.TrimStart('.')).ToObject();
    }

    private class ReflectiveFieldFetchState
    {
        public readonly Dictionary<int, JObject?> EntityIdToEntityJObject = new();
        public bool HasScanBeenCalled;
        public JArray? AsJArrayIfScanCalled;
    }

    private readonly Dictionary<string, ReflectiveFieldFetchState> _operationEntityObjectCache = new();

    public async Task<OperationResult<JArray>> GetAllEntitiesInOperationAsync(string entityName, CancellationToken cancellationToken)
    {
        if (_operationEntityObjectCache.TryGetValue(entityName, out var level2)
            && level2.HasScanBeenCalled)
        {
            return OperationResult<JArray>.Success(level2.AsJArrayIfScanCalled.NotNull());
        }

        var getAllResult = await RfConfiguration.RepositoryService.GetAllAsync(entityName, cancellationToken);
        if (!getAllResult.IsSuccessful)
        {
            return getAllResult;
        }

        var result = getAllResult.Data;

        if (level2 == null
            && !_operationEntityObjectCache.TryGetValue(entityName, out level2))
        {
            level2 = new ReflectiveFieldFetchState();
            _operationEntityObjectCache.Add(entityName, level2);
        }
        level2.HasScanBeenCalled = true;
        level2.AsJArrayIfScanCalled = result;
        foreach (var current in result)
        {
            var casted = (JObject)current;
            level2.EntityIdToEntityJObject.TryAdd((int)casted[EntityModelAttributes.Id].NotNull(), casted);
        }
        return OperationResult<JArray>.Success(result);
    }
    public async Task<OperationResult<JObject>> GetEntityInOperationAsync(string entityName, int id, CancellationToken cancellationToken)
    {
        if (_operationEntityObjectCache.TryGetValue(entityName, out var level2))
        {
            if (level2.EntityIdToEntityJObject.TryGetValue(id, out var res))
            {
                return OperationResult<JObject>.Success(res.NotNull());
            }
            if (level2.HasScanBeenCalled)
            {
                return OperationResult<JObject>.Failure($"Entity not found.", HttpStatusCode.NotFound);
            }
        }

        var getOneResult = await RfConfiguration.RepositoryService.GetOneAsync(entityName, id, cancellationToken);
        if (!getOneResult.IsSuccessful)
        {
            return getOneResult;
        }

        var result = getOneResult.Data;

        if (level2 == null
            && !_operationEntityObjectCache.TryGetValue(entityName, out level2))
        {
            level2 = new ReflectiveFieldFetchState();
            _operationEntityObjectCache.Add(entityName, level2);
        }

        if (level2.EntityIdToEntityJObject.TryGetValue(id, out var level3))
            return OperationResult<JObject>.Success(result);

        level3 = result;
        level2.EntityIdToEntityJObject.Add(id, level3);
        return OperationResult<JObject>.Success(result);
    }
}
