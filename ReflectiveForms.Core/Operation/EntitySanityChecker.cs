// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Operation;

internal static class EntitySanityChecker
{
    internal static async Task<OperationResult<bool>> JObjectStaticSanityCheckAsync(
        int entityId,
        Type groupFor,
        JObject? groupObject,
        string jsObjectPathIncludingThis,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        if (groupObject == null)
        {
            return OperationResult<bool>.Failure("Group object is null.", HttpStatusCode.BadRequest);
        }

        object? groupCastedForLogicCheck = null;

        var uniqueFieldIdFixCheck = !groupObject.ContainsKey(BaseModel.UniqueFieldIdPropertyName)
                                    || groupObject[BaseModel.UniqueFieldIdPropertyName].NotNull().Type == JTokenType.String
                                    && groupObject[BaseModel.UniqueFieldIdPropertyName]?.Value<string>() == "";

        if (uniqueFieldIdFixCheck)
        {
            groupObject[BaseModel.UniqueFieldIdPropertyName] = StringUtilities.GenerateRandomString(16, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
        }

        var fieldToFieldNameMap = new Dictionary<FieldInfo, string>();

        //The first pass for conditions and removing invisible
        var fields = groupFor.GetFields(BindingFlags.Instance | BindingFlags.Public);
        foreach (var field in fields)
        {
            if (!Attribute.IsDefined(field, typeof(Field), true)) continue;

            string? jFieldName;
            if (Attribute.IsDefined(field, typeof(JsonPropertyAttribute), true))
            {
                var jPropNameAttribute = field.GetCustomAttribute<JsonPropertyAttribute>(true);
                jFieldName = jPropNameAttribute?.PropertyName;
            }
            else jFieldName = field.Name;

            var jObjPathWithFieldSafe = (jsObjectPathIncludingThis + "." + jFieldName).TrimStart('.');

            if (jFieldName != null) fieldToFieldNameMap.Add(field, jFieldName);

            if (!Attribute.IsDefined(field, typeof(DisplayCondition), true)) continue;
            var fieldDisplayConditionAttribute = field.GetCustomAttribute<DisplayCondition>(true);
            operationState.FeedConditionForSanityCheck(jObjPathWithFieldSafe, fieldDisplayConditionAttribute?.Condition);
        }

        //Second pass is for actual sanity check
        foreach (var (field, jFieldName) in fieldToFieldNameMap)
        {
            var jObjPathWithFieldSafe = (jsObjectPathIncludingThis + "." + jFieldName).TrimStart('.');

            if (!operationState.TestVisibilityForSanityCheck(jObjPathWithFieldSafe))
            {
                groupObject.Remove(jFieldName);
                operationState.RemoveInvisibleForSanityCheck(jObjPathWithFieldSafe);
                continue;
            }

            var fieldAttribute = field.GetCustomAttribute<Field>(true);
            if (fieldAttribute is Select select)
            {
                var dynamicChoicesFunctionCompileTime = groupFor.GetMethod($"{field.Name}___DynamicChoicesCompileTimeAsync");
                if (dynamicChoicesFunctionCompileTime != null)
                {
                    groupCastedForLogicCheck ??= groupObject.ToObjectWithPolymorphism(groupFor);

                    var dynamicChoicesCTimeTask = (Task<string[]>)dynamicChoicesFunctionCompileTime.Invoke(
                        groupCastedForLogicCheck,
                        [cancellationToken]
                    ).NotNull();

                    select.Choices = await dynamicChoicesCTimeTask;
                }
            }


            if (fieldAttribute != null)
            {
                var sanityCheckResult = await fieldAttribute.SanityCheckAsync(
                    entityId,
                    groupObject,
                    jFieldName,
                    jObjPathWithFieldSafe,
                    operationState,
                    cancellationToken);
                if (!sanityCheckResult.IsSuccessful)
                    return OperationResult<bool>.Failure(sanityCheckResult.ErrorMessage, sanityCheckResult.StatusCode);
            }

            var logicCheckFunction = groupFor.GetMethod($"{field.Name}___LogicSanityCheckAsync");
            if (logicCheckFunction == null)
                continue;

            groupCastedForLogicCheck ??= groupObject.ToObjectWithPolymorphism(groupFor);

            var logicCheckTask = (Task<string?>)logicCheckFunction.Invoke(
                groupCastedForLogicCheck,
                [entityId, operationState, groupObject, cancellationToken]
            ).NotNull();

            var logicCheckFailure = await logicCheckTask;
            if (logicCheckFailure != null)
            {
                return OperationResult<bool>.Failure(logicCheckFailure, HttpStatusCode.BadRequest);
            }
        }

        return OperationResult<bool>.Success(true);
    }

    internal static bool TitleFieldSanityCheck(JObject obj, out string titleFieldSanityCheckError)
    {
        if (!obj.TryGetTypedValue(EntityModelAttributes.Title, out JObject? title))
        {
            titleFieldSanityCheckError = $"Field -{EntityModelAttributes.Title}- is missing or incorrect. (1)";
            return false;
        }
        if (!title.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? rendered))
        {
            titleFieldSanityCheckError = $"Field -{EntityModelAttributes.Title} (->{EntityModelAttributes.TitleRendered})- is missing or incorrect. (2) {title}";
            return false;
        }
        if (rendered.NotNull().Length == 0 || rendered.NotNull().Length > 256)
        {
            titleFieldSanityCheckError = $"Field -{EntityModelAttributes.Title} (->{EntityModelAttributes.TitleRendered})- has to be in length between 1 and 256. Field value: {rendered}";
            return false;
        }

        titleFieldSanityCheckError = "";
        return true;
    }

    internal static async Task<OperationResult<bool>> TitleGlobalUniquenessSanityCheckAsync(string entityName, JObject obj, CancellationToken cancellationToken)
    {
        var id = (int)obj[EntityModelAttributes.Id].NotNull();

        if (!obj.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleJObject)
                || !titleJObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRenderedNullable))
        {
            return OperationResult<bool>.Failure($"-{EntityModelAttributes.Title}- cannot be empty.", HttpStatusCode.BadRequest);
        }
        var titleRendered = titleRenderedNullable.NotNull();

        await foreach (var itemResult in RfConfiguration.RepositoryService.GetByFilterAsync(
            entityName,
            ConditionBuilder.AttributeEquals($"{EntityModelAttributes.Title}.{EntityModelAttributes.TitleRendered}", titleRendered)
                .And(ConditionBuilder.AttributeNotEquals(EntityModelAttributes.Id, id)),
            1,
            cancellationToken))
        {
            return !itemResult.IsSuccessful
                ? OperationResult<bool>.Failure($"GetByFilterAsync operation for entities has failed with {itemResult.ErrorMessage}", itemResult.StatusCode)
                : OperationResult<bool>.Failure($"-{EntityModelAttributes.Title}- of the entity must be globally unique.", HttpStatusCode.BadRequest);
        }
        return OperationResult<bool>.Success(true);
    }

    internal static bool DateFieldsSanityCheck(JObject obj, out string failureMessage)
    {
        failureMessage = "";

        if (!obj.ContainsKey(EntityModelAttributes.Date)
            || !obj.ContainsKey(EntityModelAttributes.DateGmt)
            || !obj.ContainsKey(EntityModelAttributes.Modified)
            || !obj.ContainsKey(EntityModelAttributes.ModifiedGmt))
        {
            failureMessage = $"Fields -{EntityModelAttributes.Date}- -{EntityModelAttributes.DateGmt}- -{EntityModelAttributes.Modified}- -{EntityModelAttributes.ModifiedGmt}- are mandatory.";
            return false;
        }

        if (obj[EntityModelAttributes.Date].NotNull().Type == JTokenType.Date)
            obj[EntityModelAttributes.Date] = DateUtility.DateTimeToDesiredString((DateTime)obj[EntityModelAttributes.Date].NotNull());
        if (obj[EntityModelAttributes.DateGmt].NotNull().Type == JTokenType.Date)
            obj[EntityModelAttributes.DateGmt] = DateUtility.DateTimeToDesiredString((DateTime)obj[EntityModelAttributes.DateGmt].NotNull());
        if (obj[EntityModelAttributes.Modified].NotNull().Type == JTokenType.Date)
            obj[EntityModelAttributes.Modified] = DateUtility.DateTimeToDesiredString((DateTime)obj[EntityModelAttributes.Modified].NotNull());
        if (obj[EntityModelAttributes.ModifiedGmt].NotNull().Type == JTokenType.Date)
            obj[EntityModelAttributes.ModifiedGmt] = DateUtility.DateTimeToDesiredString((DateTime)obj[EntityModelAttributes.ModifiedGmt].NotNull());

        if (!obj.TryGetTypedValue(EntityModelAttributes.Date, out string? dateString)
            || !DateUtility.FromDesiredStringToDateTime(dateString, out var date)
            || !obj.TryGetTypedValue(EntityModelAttributes.DateGmt, out string? dateGmtString)
            || !DateUtility.FromDesiredStringToDateTime(dateGmtString, out var dateGmt)
            || !obj.TryGetTypedValue(EntityModelAttributes.Modified, out string? modifiedString)
            || !DateUtility.FromDesiredStringToDateTime(modifiedString, out var modified)
            || !obj.TryGetTypedValue(EntityModelAttributes.ModifiedGmt, out string? modifiedGmtString)
            || !DateUtility.FromDesiredStringToDateTime(modifiedGmtString, out var modifiedGmt))
        {
            failureMessage = $"Fields -{EntityModelAttributes.Date}- -{EntityModelAttributes.DateGmt}- -{EntityModelAttributes.Modified}- -{EntityModelAttributes.ModifiedGmt}- are mandatory and should be strings.";
            return false;
        }

        if (modified >= date && modifiedGmt >= dateGmt) return true;
        failureMessage = $"Fields -{EntityModelAttributes.Date}- -{EntityModelAttributes.DateGmt}- should be earlier than -{EntityModelAttributes.Modified}- -{EntityModelAttributes.ModifiedGmt}-";
        return false;
    }

    internal static async Task<OperationResult<bool>> FieldsSanityCheckAsync(string entityName, JObject obj, CancellationToken cancellationToken)
    {
        if (!obj.TryGetTypedValue(EntityModelAttributes.Id, out int entityId))
        {
            return OperationResult<bool>.Failure($"Field -{EntityModelAttributes.Id}- is missing.", HttpStatusCode.BadRequest);
        }

        if (!obj.TryGetTypedValue(EntityModelAttributes.Fields, out JObject? fieldsObj))
        {
            if (obj.ContainsKey(EntityModelAttributes.Fields))
                obj[EntityModelAttributes.Fields] = new JObject();
            else
                obj.Add(EntityModelAttributes.Fields, new JObject());

            fieldsObj = (JObject)obj[EntityModelAttributes.Fields].NotNull();
        }

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var finalConfiguration))
            return OperationResult<bool>.Success(true);

        return await JObjectStaticSanityCheckAsync(
            entityId,
            finalConfiguration.EntityConfiguration.EntityFieldsModelType,
            fieldsObj,
            "",
            EntityOperationState.CreateStateForSanityCheck(fieldsObj),
            cancellationToken);
    }
    internal static bool AuthorFieldSanityCheck(
        JObject obj,
        out string failureMessage)
    {
        if (!obj.TryGetTypedValue(EntityModelAttributes.Author, out int _))
        {
            failureMessage = $"Field -{EntityModelAttributes.Author}- is missing.";
            return false;
        }
        failureMessage = "";
        return true;
    }
    internal static async Task<OperationResult<bool>> ParentFieldSanityCheckAsync(
        string entityNameIfParent,
        JObject obj,
        CancellationToken cancellationToken)
    {
        return await InternalVariousFieldSanityCheckAsync(
            InternalVariousFieldSanityCheck.Parent,
            entityNameIfParent,
            obj,
            cancellationToken);
    }
    internal static async Task<OperationResult<bool>> CategoriesFieldSanityCheckAsync(
        JObject obj,
        CancellationToken cancellationToken)
    {
        return await InternalVariousFieldSanityCheckAsync(
            InternalVariousFieldSanityCheck.Categories,
            null,
            obj,
            cancellationToken);
    }
    internal static async Task<OperationResult<bool>> TagsFieldSanityCheckAsync(
        JObject obj,
        CancellationToken cancellationToken)
    {
        return await InternalVariousFieldSanityCheckAsync(
            InternalVariousFieldSanityCheck.Tags,
            null,
            obj,
            cancellationToken);
    }
    private enum InternalVariousFieldSanityCheck
    {
        Parent,
        Categories,
        Tags
    }
    private static async Task<OperationResult<bool>> InternalVariousFieldSanityCheckAsync(
        InternalVariousFieldSanityCheck what,
        string? entityNameIfParent,
        JObject obj,
        CancellationToken cancellationToken)
    {
        string attr;
        string entityName;
        switch (what)
        {
            case InternalVariousFieldSanityCheck.Parent:
                if (entityNameIfParent == null) return OperationResult<bool>.Failure("Parent entity name is null.", HttpStatusCode.BadRequest);
                attr = EntityModelAttributes.Parent;
                entityName = entityNameIfParent;
                break;
            case InternalVariousFieldSanityCheck.Tags:
                attr = EntityModelAttributes.Tags;
                entityName = RfReservedEntities.TagsEntityName;
                break;
            //if (_What == InternalVariousFieldSanityCheck.Categories)
            case InternalVariousFieldSanityCheck.Categories:
            default:
                attr = EntityModelAttributes.Categories;
                entityName = RfReservedEntities.CategoriesEntityName;
                break;
        }

        if (obj.ContainsKey(attr))
        {
            if (what is InternalVariousFieldSanityCheck.Tags or InternalVariousFieldSanityCheck.Categories)
            {
                if (!obj.TryGetTypedValue(attr, out List<int> ids))
                {
                    return OperationResult<bool>.Failure($"Mandatory {attr} field must be an array. {obj[attr]}", HttpStatusCode.BadRequest);
                }
                foreach (var id in ids)
                {
                    var existenceCheckResult = await LocalIdExistenceCheck(id);
                    if (!existenceCheckResult.IsSuccessful)
                    {
                        return OperationResult<bool>.Failure(existenceCheckResult.ErrorMessage, existenceCheckResult.StatusCode);
                    }
                }
            }
            else
            {
                if (!obj.TryGetTypedValue(attr, out int id))
                {
                    return OperationResult<bool>.Failure($"Mandatory {attr} field must be an integer: {obj[attr]}", HttpStatusCode.BadRequest);
                }

                var existenceCheckResult = await LocalIdExistenceCheck(id);
                return existenceCheckResult.IsSuccessful
                    ? OperationResult<bool>.Success(true)
                    : OperationResult<bool>.Failure(existenceCheckResult.ErrorMessage, existenceCheckResult.StatusCode);
            }
        }
        else
        {
            return OperationResult<bool>.Failure($"Mandatory {attr} field does not exist.", HttpStatusCode.BadRequest);
        }

        return OperationResult<bool>.Success(true);

        async Task<OperationResult<bool>> LocalIdExistenceCheck(int id)
        {
            if (id < 1) return OperationResult<bool>.Success(true);
            var existResult = await RfConfiguration.RepositoryService.DoesExistAsync(entityName, id, cancellationToken);
            if (existResult.IsSuccessful) return OperationResult<bool>.Success(true);
            return OperationResult<bool>.Failure(existResult.StatusCode == HttpStatusCode.NotFound
                ? $"Field -{attr}- with id: {id} does not exist for entity type {entityName}"
                : $"Existence check for field -{attr}- with id: {id} has failed with {existResult.ErrorMessage}", existResult.StatusCode);
        }
    }
}
