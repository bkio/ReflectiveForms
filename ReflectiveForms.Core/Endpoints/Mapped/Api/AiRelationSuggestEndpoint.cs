// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Schema.Models;
using static ReflectiveForms.Core.Endpoints.Mapped.Api.Crud;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// POST /rf/api/ai/relation_suggest
///
/// AI-powered relation suggestions using semantic search on the target entity type.
/// </summary>
internal class AiRelationSuggestEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods() => [RequestHttpVerb.Post];

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch() => RequestBodyType.JsonObject;

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (RfConfiguration.AiServiceConfiguration == null)
            return HttpStatusCode.NotImplemented.ToResult("AI features are not configured.");

        if (!context.Request.TryGetTypeParameter(out var entityName, out var failedResult))
            return failedResult!;

        var body = RequestBodyJsonObject.NotNull();
        var relationField = body["relation_field"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(relationField))
            return HttpStatusCode.BadRequest.ToResult("'relation_field' is required.");

        var currentText = body["current_text"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(currentText))
            return HttpStatusCode.BadRequest.ToResult("'current_text' is required.");

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        var userFields = RequesterUser.NotNull().Fields;

        // Source entity: require CREATE or UPDATE
        if (!userFields.CanUserDo("CREATE", entityName) && !userFields.CanUserDo("UPDATE", entityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

        // Find the relation field and determine the target entity type
        var (targetEntityName, topK) = FindRelationTarget(configBase.EntityConfiguration.EntityFieldsModelType, relationField);
        if (string.IsNullOrEmpty(targetEntityName))
            return HttpStatusCode.BadRequest.ToResult($"Field '{relationField}' is not a valid Relation field or target entity not found.");

        // Target entity: require PEEK_ALL
        if (!userFields.CanUserDo("PEEK_ALL", targetEntityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to view the target entity type.");

        var suggestions = await AiRelationSuggestionHandler.SuggestAsync(
            targetEntityName, currentText, topK, cancellationToken);

        // Filter by sharing if target has individual sharing
        if (RfConfiguration.EntityNameToConfiguration.TryGetValue(targetEntityName, out var targetConfig) &&
            targetConfig.EntityConfiguration.HasIndividualSharing)
        {
            suggestions = suggestions.Where(s =>
            {
                var entityResult = AiConfiguration.DatabaseService.GetItemAsync(
                    targetEntityName,
                    new CrossCloudKit.Interfaces.Classes.DbKey(Models.EntityModelAttributes.Id, s.Id),
                    null, cancellationToken).GetAwaiter().GetResult();

                if (!entityResult.IsSuccessful || entityResult.Data == null)
                    return false;

                return GetEntitySharingAccessLevel(targetEntityName, entityResult.Data, RequesterUser.NotNull()) != SharingAccessLevel.None;
            }).ToList();
        }

        var suggestionsArray = new JArray();
        foreach (var s in suggestions)
        {
            suggestionsArray.Add(new JObject
            {
                ["id"] = s.Id,
                ["title"] = s.Title,
                ["score"] = s.Score
            });
        }

        var response = new JObject { ["suggestions"] = suggestionsArray };
        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }

    private static (string? targetEntityName, int topK) FindRelationTarget(Type fieldsModelType, string fieldName)
    {
        foreach (var member in fieldsModelType.GetMembers(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var jsonProp = member.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>();
            if (jsonProp?.PropertyName != fieldName) continue;

            var fieldAttr = member.GetCustomAttribute<Attributes.Field>();
            if (fieldAttr == null) continue;

            // Check if it's a Relation field type
            var fieldType = GetPrivateField<ReflectiveForms.Core.Enums.FieldType>(fieldAttr, "_fieldType");
            if (fieldType != ReflectiveForms.Core.Enums.FieldType.Relation) return (null, 5);

            // Get relation target entity name
            var relatedEntity = GetPrivateField<string?>(fieldAttr, "_relatedEntityName");
            if (string.IsNullOrEmpty(relatedEntity)) return (null, 5);

            // Check for [AIRelationSuggestion] attribute
            var suggestionAttr = member.GetCustomAttribute<AIRelationSuggestion>();
            var topK = suggestionAttr?.TopK ?? 5;

            return (relatedEntity, topK);
        }

        return (null, 5);
    }

    private static T? GetPrivateField<T>(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        return field != null ? (T?)field.GetValue(obj) : default;
    }
}
