// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes;

namespace ReflectiveForms.Core.Ai;

internal record AiSanityCheckResult(string Check, bool Passed, AISanityCheckSeverity Severity, string? Message);

/// <summary>
/// Handles AI-powered sanity checks. Given a field value and [AISanityCheck] attributes,
/// asks the LLM whether the content passes each check.
/// </summary>
internal static class AiSanityCheckHandler
{
    internal static async Task<List<AiSanityCheckResult>> CheckFieldAsync(
        string entityName,
        string fieldName,
        JToken fieldValue,
        IReadOnlyList<AISanityCheck> checks,
        CancellationToken cancellationToken)
    {
        var results = new List<AiSanityCheckResult>();
        var valueStr = fieldValue.ToString();

        if (string.IsNullOrWhiteSpace(valueStr))
            return results; // Nothing to check

        foreach (var check in checks)
        {
            var systemPrompt = RfConfiguration.AiServiceConfiguration!.SystemPromptPrefix + "\n" +
                               "You are a content quality checker. " +
                               "You will be given a field value and a quality check to perform. " +
                               "Respond with ONLY a JSON object: {\"passed\": true/false, \"message\": \"reason\"}. " +
                               "If the check passes, set passed=true and message can be empty. " +
                               "If it fails, set passed=false and explain why briefly.";

            var userPrompt = $"Entity type: {entityName}\nField: {fieldName}\n" +
                             $"Value: {valueStr}\n\nCheck: {check.CheckPrompt}";

            var request = new LLMRequest
            {
                Messages =
                [
                    new LLMMessage { Role = LLMRole.System, Content = systemPrompt },
                    new LLMMessage { Role = LLMRole.User, Content = userPrompt }
                ],
                MaxTokens = RfConfiguration.AiServiceConfiguration!.MaxLightCompletionTokens,
                Temperature = RfConfiguration.AiServiceConfiguration.LightTemperature
            };

            var result = await AiConfiguration.LightLlmService.CompleteAsync(request, cancellationToken);
            if (!result.IsSuccessful)
            {
                RfConfiguration.LogError($"AiSanityCheckHandler: LLM call failed for {entityName}/{fieldName}: {result.ErrorMessage}");
                // LLM failure → skip this check entirely (don't block saves due to LLM outages)
                continue;
            }

            var content = result.Data.Content?.Trim();
            if (string.IsNullOrEmpty(content))
            {
                results.Add(new AiSanityCheckResult(check.CheckPrompt, true, check.Severity, null));
                continue;
            }

            // Strip markdown fences (```json ... ``` or ``` ... ```) that some models wrap around JSON
            if (content.StartsWith("```"))
            {
                var firstNewline = content.IndexOf('\n');
                var lastFence = content.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    content = content[(firstNewline + 1)..lastFence].Trim();
            }

            try
            {
                var parsed = JObject.Parse(content);
                var passed = parsed["passed"]?.Value<bool>() ?? true;
                var message = parsed["message"]?.Value<string>();
                results.Add(new AiSanityCheckResult(check.CheckPrompt, passed, check.Severity, message));
            }
            catch
            {
                // Couldn't parse LLM response as JSON — treat as pass
                results.Add(new AiSanityCheckResult(check.CheckPrompt, true, check.Severity, null));
            }
        }

        return results;
    }
}
