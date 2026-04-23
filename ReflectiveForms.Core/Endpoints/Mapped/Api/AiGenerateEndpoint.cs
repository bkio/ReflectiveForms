// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// POST /rf/api/ai/generate
///
/// Natural language → entity creation. Returns a draft JObject, NOT saved.
/// </summary>
internal class AiGenerateEndpoint : BaseEndpoint
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
        var prompt = body["prompt"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(prompt))
            return HttpStatusCode.BadRequest.ToResult("'prompt' is required.");

        if (!RfConfiguration.EntityNameToConfiguration.TryGetValue(entityName, out var configBase))
            return HttpStatusCode.NotFound.ToResult($"Entity type '{entityName}' not found.");

        if (!configBase.EntityConfiguration.SupportsAiGeneration)
            return HttpStatusCode.BadRequest.ToResult($"Entity type '{entityName}' does not support AI generation.");

        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("CREATE", entityName))
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");

        var (result, conversation) = await AiEntityGenerator.GenerateAsync(entityName, prompt, cancellationToken);
        if (result == null)
            return HttpStatusCode.InternalServerError.ToResult("AI generation failed. Please try again.");

        // Build conversation array for frontend debug/chat UI (exclude system prompt)
        var conversationArray = new JArray();
        foreach (var msg in conversation)
        {
            if (msg.Role == LLMRole.System) continue;
            conversationArray.Add(new JObject
            {
                ["role"] = msg.Role.ToString().ToLowerInvariant(),
                ["content"] = msg.Content ?? ""
            });
        }

        var response = new JObject { ["fields"] = result, ["conversation"] = conversationArray };
        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }
}
