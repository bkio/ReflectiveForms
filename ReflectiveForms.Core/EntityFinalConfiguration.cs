// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
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

            return await EntitySanityChecker.FieldsSanityCheckAsync(config.EntityName, entityObject, cancellationToken);
        };

        DefaultJObject = defaultInstance.FromObjectWithPolymorphism();
    }
}
