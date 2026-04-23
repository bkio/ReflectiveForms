// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace ReflectiveForms.Core;

public abstract class EntityFinalConfigurationBase
{
    internal EntityFinalConfigurationBase(Type entityFieldsModelType)
    {
        CrudMethodInfo = new CrudMethodInfo()
        {
            PutOneAsyncMethodInfo =
                EntityRepositoryService.PutOneAsyncMethodInfo.MakeGenericMethod(entityFieldsModelType),
            UpdateOneAsyncMethodInfo =
                EntityRepositoryService.UpdateOneAsyncMethodInfo.MakeGenericMethod(entityFieldsModelType),
            DeleteOneAsyncMethodInfo =
                EntityRepositoryService.DeleteOneAsyncMethodInfo.MakeGenericMethod(entityFieldsModelType)
        };
    }

    internal Type EntityModelType { get; init; } = null!;

    internal CrudMethodInfo CrudMethodInfo { get; private init; }

    internal Func<(JObject entityObject, CancellationToken cancellationToken), Task<OperationResult<bool>>> UpsertSanityCheck { get; init; } = null!;
    internal JObject DefaultJObject { get; init; } = null!;

    public EntityConfigurationBuilderBase EntityConfiguration { get; protected init; } = null!;
}

internal class CrudMethodInfo
{
    internal required MethodInfo PutOneAsyncMethodInfo { get; init; }
    internal required MethodInfo UpdateOneAsyncMethodInfo { get; init; }
    internal required MethodInfo DeleteOneAsyncMethodInfo { get; init; }
}

public sealed class EntityFinalConfiguration<T> : EntityFinalConfigurationBase where T : EntityFieldsModel, new()
{
    internal EntityFinalConfiguration(EntityConfigurationBuilder<T> config) : base(typeof(T))
    {
        EntityConfiguration = config;

        EntityModelType = config.ToEntityModelType();
        var defaultInstance = (EntityModel<T>)Activator.CreateInstance(EntityModelType, nonPublic: true).NotNull();

        // Pre-compute fields with [AISanityCheck] attributes for save-pipeline integration
        var fieldsWithAiSanityChecks = new List<(string JsonPropertyName, IReadOnlyList<AISanityCheck> Checks)>();
        foreach (var member in typeof(T).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m is FieldInfo or PropertyInfo))
        {
            var aiChecks = member.GetCustomAttributes<AISanityCheck>(true).ToList();
            if (aiChecks.Count == 0) continue;

            var jsonPropAttr = member.GetCustomAttribute<JsonPropertyAttribute>(true);
            var fieldName = jsonPropAttr?.PropertyName ?? member.Name;
            fieldsWithAiSanityChecks.Add((fieldName, aiChecks));
        }

        UpsertSanityCheck = async pair =>
        {
            var (entityObject, cancellationToken) = pair;

            if (!EntitySanityChecker.TitleFieldSanityCheck(entityObject, out var titleFieldSanityCheckError))
                return OperationResult<bool>.Failure(titleFieldSanityCheckError, HttpStatusCode.BadRequest);
            if (config.OptionalTitleSanityCheck != null
                && entityObject.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleJObject))
            {
                var titleSanityCheckResult = await config.OptionalTitleSanityCheck(titleJObject.NotNull().ToObjectWithPolymorphism<TitleRenderedModel>().NotNull());
                if (!titleSanityCheckResult)
                    return OperationResult<bool>.Failure($"Title field is not valid: {entityObject[EntityModelAttributes.Title]}", HttpStatusCode.BadRequest);
            }

            if (config.RequireGlobalTitleUniqueness)
            {
                var titleGlobalUniqueness = await EntitySanityChecker.TitleGlobalUniquenessSanityCheckAsync(config.EntityName, entityObject, cancellationToken);
                if (!titleGlobalUniqueness.IsSuccessful)
                    return titleGlobalUniqueness;
            }

            if (!EntitySanityChecker.DateFieldsSanityCheck(entityObject, out var dateFieldsSanityCheckError))
                return OperationResult<bool>.Failure(dateFieldsSanityCheckError, HttpStatusCode.BadRequest);

            if (config.HasParentChildRelationship)
            {
                var parentCheckResult = await EntitySanityChecker.ParentFieldSanityCheckAsync(config.EntityName, entityObject, cancellationToken);
                if (!parentCheckResult.IsSuccessful)
                    return parentCheckResult;
            }

            if (config.HasTags)
            {
                var tagsCheckResult = await EntitySanityChecker.TagsFieldSanityCheckAsync(entityObject, cancellationToken);
                if (!tagsCheckResult.IsSuccessful)
                    return tagsCheckResult;
            }

            if (config.HasCategories)
            {
                var categoriesCheckResult = await EntitySanityChecker.CategoriesFieldSanityCheckAsync(entityObject, cancellationToken);
                if (!categoriesCheckResult.IsSuccessful)
                    return categoriesCheckResult;
            }

            if (config.HasAuthor && !EntitySanityChecker.AuthorFieldSanityCheck(entityObject, out var authorFieldSanityCheckError))
                return OperationResult<bool>.Failure(authorFieldSanityCheckError, HttpStatusCode.BadRequest);

            var fieldsResult = await EntitySanityChecker.FieldsSanityCheckAsync(config.EntityName, entityObject, cancellationToken);
            if (!fieldsResult.IsSuccessful)
                return fieldsResult;

            // AI sanity checks — only if AI is configured and this entity has [AISanityCheck] fields
            if (RfConfiguration.AiServiceConfiguration != null && fieldsWithAiSanityChecks.Count > 0)
            {
                try
                {
                    foreach (var (fieldName, checks) in fieldsWithAiSanityChecks)
                    {
                        var fieldValue = entityObject.SelectToken($"fields.{fieldName}");
                        if (fieldValue == null) continue;

                        var aiResults = await AiSanityCheckHandler.CheckFieldAsync(
                            config.EntityName, fieldName, fieldValue, checks, cancellationToken);

                        foreach (var result in aiResults.Where(r => !r.Passed && r.Severity == AISanityCheckSeverity.Error))
                            return OperationResult<bool>.Failure(result.Message ?? result.Check, HttpStatusCode.BadRequest);
                    }
                }
                catch (Exception ex)
                {
                    // LLM failures must not block saves — log and continue
                    RfConfiguration.LogError(ex);
                }
            }

            return OperationResult<bool>.Success(true);
        };

        DefaultJObject = defaultInstance.FromObjectWithPolymorphism();
    }
}
