// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints.Enums;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// POST /rf/api/ai/chat
///
/// Request:
/// {
///   "message": "Create a new objective about Q3 planning",
///   "context": { "current_page": "entity-list", "entity_type": "objective" },
///   "confirmed_actions": [{ "action_id": "action-1", "approved": true, "action": {...} }]
/// }
///
/// Response:
/// {
///   "response": "...",
///   "tool_calls_made": [...],
///   "proposed_actions": [{ "action_id": "action-1", "action_type": "create_entity", ... }],
///   "executed_actions": [{ "action_id": "action-1", "success": true, "result": {...} }]
/// }
/// </summary>
internal class AiAgentChatEndpoint : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods() => [RequestHttpVerb.Post];

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch() => RequestBodyType.JsonObject;

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (RfConfiguration.AiServiceConfiguration == null)
            return HttpStatusCode.NotImplemented.ToResult("AI features are not configured.");

        var body = RequestBodyJsonObject.NotNull();
        var message = body["message"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(message))
            return HttpStatusCode.BadRequest.ToResult("'message' is required.");

        // Parse context
        AgentContext? agentContext = null;
        if (body["context"] is JObject ctxObj)
        {
            agentContext = new AgentContext
            {
                CurrentPage = ctxObj.Value<string>("current_page"),
                EntityType = ctxObj.Value<string>("entity_type"),
                EntityId = ctxObj.Value<int?>("entity_id"),
                CurrentFields = ctxObj["current_fields"] as JObject,
                Errors = ctxObj["errors"]?.ToObject<List<string>>(),
                SelectedField = ctxObj.Value<string>("selected_field")
            };
        }

        // Parse and execute confirmed actions
        var executedActionsArray = new JArray();
        if (body["confirmed_actions"] is JArray confirmedArray)
        {
            foreach (var item in confirmedArray)
            {
                var actionId = item.Value<string>("action_id") ?? "";
                var approved = item.Value<bool>("approved");

                if (!approved)
                {
                    executedActionsArray.Add(new JObject
                    {
                        ["action_id"] = actionId,
                        ["success"] = true,
                        ["message"] = "Action rejected by user."
                    });
                    continue;
                }

                var action = item["action"] as JObject;
                if (action == null)
                {
                    executedActionsArray.Add(new JObject
                    {
                        ["action_id"] = actionId,
                        ["success"] = false,
                        ["message"] = "Missing action details."
                    });
                    continue;
                }

                var actionType = action.Value<string>("action_type") ?? "";
                var entityType = action.Value<string>("entity_type") ?? "";
                var entityId = action.Value<int?>("entity_id");
                var payload = action["payload"] as JObject ?? new JObject();

                var (execSuccess, execMessage, execData) = await ExecuteActionAsync(
                    actionType, entityType, entityId, payload, cancellationToken);

                var executedObj = new JObject
                {
                    ["action_id"] = actionId,
                    ["action_type"] = actionType,
                    ["entity_type"] = entityType,
                    ["entity_id"] = entityId,
                    ["success"] = execSuccess,
                    ["message"] = execMessage
                };
                if (execData != null) executedObj["result"] = execData;
                executedActionsArray.Add(executedObj);
            }
        }

        // Build confirmations list and execution results for the chat handler
        var confirmations = new List<ActionConfirmation>();
        var executionResults = new List<ActionExecutionResult>();
        if (body["confirmed_actions"] is JArray confirmedArr2)
        {
            foreach (var item in confirmedArr2)
            {
                var actionId = item.Value<string>("action_id") ?? "";
                confirmations.Add(new ActionConfirmation
                {
                    ActionId = actionId,
                    Approved = item.Value<bool>("approved")
                });
            }
        }
        // Translate the executed actions into ActionExecutionResult for ChatAsync
        foreach (var ea in executedActionsArray)
        {
            executionResults.Add(new ActionExecutionResult
            {
                ActionId = ea.Value<string>("action_id") ?? "",
                Success = ea.Value<bool>("success"),
                Message = ea.Value<string>("message") ?? ""
            });
        }

        // Parse conversation history
        List<ChatHistoryEntry>? history = null;
        if (body["history"] is JArray historyArr)
        {
            history = [];
            foreach (var item in historyArr)
            {
                var role = item.Value<string>("role");
                var content = item.Value<string>("content");
                if (!string.IsNullOrEmpty(role) && !string.IsNullOrEmpty(content))
                    history.Add(new ChatHistoryEntry { Role = role, Content = content });
            }
            if (history.Count == 0) history = null;
        }

        var chatRequest = new AgentChatRequest
        {
            Message = message,
            Context = agentContext,
            ConfirmedActions = confirmations.Count > 0 ? confirmations : null,
            ExecutedActionResults = executionResults.Count > 0 ? executionResults : null,
            History = history
        };

        AgentChatResult result;
        try
        {
            result = await AiAgentChatHandler.ChatAsync(chatRequest, RequesterUser.NotNull(), cancellationToken);
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError($"AiAgentChatEndpoint.ChatAsync failed: {ex.Message}");

            // Return executed actions (if any) with an error response instead of 500
            var errorResponse = new JObject
            {
                ["response"] = $"Sorry, the AI assistant encountered an error: {ex.Message}",
                ["tool_calls_made"] = new JArray(),
                ["proposed_actions"] = new JArray()
            };
            if (executedActionsArray.Count > 0)
                errorResponse["executed_actions"] = executedActionsArray;
            return Results.Content(JsonConvert.SerializeObject(errorResponse), "application/json");
        }

        var toolCallsArray = new JArray();
        foreach (var tc in result.ToolCallsMade)
        {
            toolCallsArray.Add(new JObject
            {
                ["tool"] = tc.ToolName,
                ["arguments"] = tc.Arguments,
                ["result_preview"] = tc.Result.Length > 500 ? tc.Result[..500] + "..." : tc.Result
            });
        }

        var proposedActionsArray = new JArray();
        foreach (var pa in result.ProposedActions)
        {
            var actionObj = new JObject
            {
                ["action_id"] = pa.ActionId,
                ["action_type"] = pa.ActionType,
                ["description"] = pa.Description,
                ["requires_approval"] = pa.RequiresApproval
            };
            if (pa.EntityType != null) actionObj["entity_type"] = pa.EntityType;
            if (pa.EntityId.HasValue) actionObj["entity_id"] = pa.EntityId;
            if (pa.Payload != null) actionObj["payload"] = pa.Payload;
            proposedActionsArray.Add(actionObj);
        }

        var response = new JObject
        {
            ["response"] = result.Response,
            ["tool_calls_made"] = toolCallsArray,
            ["proposed_actions"] = proposedActionsArray
        };

        if (executedActionsArray.Count > 0)
            response["executed_actions"] = executedActionsArray;

        return Results.Content(JsonConvert.SerializeObject(response), "application/json");
    }

    private async Task<(bool Success, string Message, JObject? Data)> ExecuteActionAsync(
        string actionType, string entityType, int? entityId, JObject payload, CancellationToken ct)
    {
        try
        {
            switch (actionType)
            {
                case "create_entity":
                {
                    var r = await AiAgentChatHandler.ExecuteApprovedCreateAsync(
                        entityType, payload, RequesterUser.NotNull(), ct);
                    return (r.IsSuccessful, r.IsSuccessful ? "Created successfully." : (r.ErrorMessage ?? "Create failed."), r.IsSuccessful ? r.Data : null);
                }
                case "update_entity":
                {
                    if (!entityId.HasValue)
                        return (false, "Missing entity_id for update.", null);
                    var r = await AiAgentChatHandler.ExecuteApprovedUpdateAsync(
                        entityType, entityId.Value, payload, RequesterUser.NotNull(), ct);
                    return (r.IsSuccessful, r.IsSuccessful ? "Updated successfully." : (r.ErrorMessage ?? "Update failed."), r.IsSuccessful ? r.Data : null);
                }
                case "delete_entity":
                {
                    if (!entityId.HasValue)
                        return (false, "Missing entity_id for delete.", null);
                    var r = await AiAgentChatHandler.ExecuteApprovedDeleteAsync(
                        entityType, entityId.Value, RequesterUser.NotNull(), ct);
                    return (r.IsSuccessful, r.IsSuccessful ? "Deleted successfully." : (r.ErrorMessage ?? "Delete failed."), r.IsSuccessful ? r.Data : null);
                }
                case "set_field":
                case "navigate":
                    // These actions are handled client-side; no backend execution needed
                    return (true, "Action handled client-side.", null);
                default:
                    return (false, $"Unknown action type: {actionType}", null);
            }
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError($"AiAgentChatEndpoint.ExecuteActionAsync [{actionType}]: {ex.Message}");
            return (false, $"Action execution failed: {ex.Message}", null);
        }
    }
}
