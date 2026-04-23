// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Handles AI-powered revision diff summaries.
/// Accepts entity ID + revision index, fetches revisions server-side, and summarizes changes.
/// </summary>
internal static class AiDiffSummaryHandler
{
    internal static async Task<string?> SummarizeAsync(
        string entityName,
        int entityId,
        int revisionIndex,
        CancellationToken cancellationToken)
    {
        // Fetch revisions via the repository service (normalized format)
        var revisionsResult = await RfConfiguration.RepositoryService.GetEntityRevisionsAsync(
            entityName, entityId, cancellationToken);

        if (!revisionsResult.IsSuccessful || revisionsResult.Data == null)
            return null;

        var revisionsCount = revisionsResult.Data["revisions_count"]?.Value<int>() ?? 0;
        if (revisionIndex < 1 || revisionIndex > revisionsCount)
            return null;

        var revisionsArray = revisionsResult.Data["revisions"] as JArray;
        if (revisionsArray == null || revisionsArray.Count < 1)
            return null;

        // Find the old revision by revision_number
        JToken? oldRevision = null;
        JToken? newRevision = null;
        foreach (var rev in revisionsArray)
        {
            var revNum = rev["revision_number"]?.Value<int>();
            if (revNum == revisionIndex) oldRevision = rev;
            else if (revNum == revisionIndex + 1) newRevision = rev;
        }

        if (oldRevision == null)
            return null;

        var oldFields = oldRevision["object"]?["fields"];
        JToken? newFields;

        if (newRevision != null)
        {
            newFields = newRevision["object"]?["fields"];
        }
        else if (revisionIndex == revisionsCount)
        {
            // Last old revision → compare with the current (live) entity
            var currentResult = await AiConfiguration.DatabaseService.GetItemAsync(
                entityName,
                new DbKey(EntityModelAttributes.Id, entityId),
                null, cancellationToken);
            if (!currentResult.IsSuccessful || currentResult.Data == null)
                return null;
            newFields = currentResult.Data[EntityModelAttributes.Fields];
        }
        else
        {
            return null;
        }

        if (oldFields == null || newFields == null)
            return null;

        // Compute field-by-field diff
        var diffParts = ComputeDiff(oldFields as JObject, newFields as JObject);
        if (diffParts.Count == 0)
            return "No meaningful changes were detected between these revisions.";

        var diffText = string.Join("\n", diffParts);

        // Truncate if too long
        if (diffText.Length > 3000)
            diffText = diffText[..1500] + "\n[...truncated...]\n" + diffText[^1500..];

        var config = RfConfiguration.EntityNameToConfiguration[entityName];
        var readableName = config.EntityConfiguration.EntityReadableNameSingular;

        var systemPrompt = RfConfiguration.AiServiceConfiguration!.SystemPromptPrefix + "\n" +
                           "You summarize changes between entity revisions. " +
                           "Given field-by-field diffs, write a clear 2-4 sentence summary in plain English. " +
                           "Focus on WHAT changed and its significance, not technical field names or JSON structure. " +
                           "Group related changes together. Use past tense (e.g. 'Updated the title to...', 'Added two new sections').";

        var userPrompt = $"Changes between revision {revisionIndex} and {revisionIndex + 1} of a {readableName}:\n\n{diffText}";

        var request = new LLMRequest
        {
            Messages =
            [
                new LLMMessage { Role = LLMRole.System, Content = systemPrompt },
                new LLMMessage { Role = LLMRole.User, Content = userPrompt }
            ],
            MaxTokens = RfConfiguration.AiServiceConfiguration!.MaxCompletionTokens,
            Temperature = RfConfiguration.AiServiceConfiguration.Temperature
        };

        var result = await AiConfiguration.HeavyLlmService.CompleteAsync(request, cancellationToken);
        if (!result.IsSuccessful)
        {
            RfConfiguration.LogError($"AiDiffSummaryHandler: LLM call failed: {result.ErrorMessage}");
            return null;
        }

        return result.Data.Content?.Trim();
    }

    private static List<string> ComputeDiff(JObject? oldFields, JObject? newFields)
    {
        var parts = new List<string>();
        if (oldFields == null || newFields == null)
            return parts;

        var allKeys = new HashSet<string>();
        foreach (var prop in oldFields.Properties()) allKeys.Add(prop.Name);
        foreach (var prop in newFields.Properties()) allKeys.Add(prop.Name);

        foreach (var key in allKeys)
        {
            var oldVal = oldFields[key];
            var newVal = newFields[key];

            if (oldVal == null && newVal != null)
            {
                parts.Add($"Added '{key}': {TruncateValue(newVal)}");
            }
            else if (oldVal != null && newVal == null)
            {
                parts.Add($"Removed '{key}': was {TruncateValue(oldVal)}");
            }
            else if (oldVal != null && newVal != null && !JToken.DeepEquals(oldVal, newVal))
            {
                parts.Add($"Changed '{key}': from {TruncateValue(oldVal)} to {TruncateValue(newVal)}");
            }
        }

        return parts;
    }

    private static string TruncateValue(JToken value)
    {
        var str = value.ToString();
        return str.Length > 500
            ? str[..250] + " [...] " + str[^250..]
            : str;
    }
}
