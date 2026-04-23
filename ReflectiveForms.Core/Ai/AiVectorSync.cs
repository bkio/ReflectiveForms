// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Background timer that periodically syncs the vector index with the primary database.
/// Uses a distributed mutex to ensure only one instance runs per cycle.
/// </summary>
internal static class AiVectorSync
{
    private static Timer? _syncTimer;
    private static readonly string SettingsTable = "rf-internal-settings";
    private static readonly DbKey SyncTimestampKey = new("id", new Primitive("ai-vector-sync"));

    internal static void StartSyncTimer(TimeSpan interval)
    {
        // Check if the last sync is stale — if so, run after a short startup delay (30s)
        // instead of waiting a full interval. This handles the case where a previous sync
        // failed (e.g. embedding service crash) and never wrote a timestamp, or the server
        // has been down for longer than the sync interval.
        var firstTickDelay = interval;
        try
        {
            var lastSyncResult = AiConfiguration.DatabaseService.GetItemAsync(
                SettingsTable, SyncTimestampKey).GetAwaiter().GetResult();
            if (lastSyncResult.IsSuccessful && lastSyncResult.Data != null)
            {
                var lastSyncStr = lastSyncResult.Data["last_sync_gmt"]?.Value<string>();
                if (lastSyncStr != null && DateTime.TryParse(lastSyncStr, out var lastSync))
                {
                    if (DateTime.UtcNow - lastSync >= interval)
                        firstTickDelay = TimeSpan.FromSeconds(30); // Stale — run soon
                }
                else
                {
                    firstTickDelay = TimeSpan.FromSeconds(30); // Corrupt timestamp — run soon
                }
            }
            else
            {
                firstTickDelay = TimeSpan.FromSeconds(30); // No timestamp — first ever sync
            }
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError($"AiVectorSync: Failed to read sync timestamp during startup: {ex.Message}");
        }

        _syncTimer = new Timer(async _ =>
        {
            try { await RunSyncCycleAsync(); }
            catch (Exception ex) { RfConfiguration.LogError(ex); }
        }, null, firstTickDelay, interval);
    }

    internal static void StopSyncTimer()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
    }

    private static async Task RunSyncCycleAsync()
    {
        try
        {
            using var syncCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var mutex = await MemoryScopeMutex.CreateEntityScopeAsync(
                AiConfiguration.MemoryService,
                new MemoryScopeLambda("rf:ai:sync"),
                "hourly-sync",
                TimeSpan.FromMinutes(5),
                syncCts.Token);

            // Check persistent DB timestamp: was a sync already completed recently?
            var lastSyncResult = await AiConfiguration.DatabaseService.GetItemAsync(
                SettingsTable, SyncTimestampKey);
            if (lastSyncResult.IsSuccessful && lastSyncResult.Data != null)
            {
                var lastSyncStr = lastSyncResult.Data["last_sync_gmt"]?.Value<string>();
                if (lastSyncStr != null && DateTime.TryParse(lastSyncStr, out var lastSync))
                {
                    var syncInterval = RfConfiguration.AiServiceConfiguration!.SyncInterval;
                    if (DateTime.UtcNow - lastSync < syncInterval)
                        return; // Another instance already completed sync within this interval
                }
            }

            // Run incremental sync for each entity type with semantic search enabled
            var anySuccess = false;
            foreach (var (entityName, config) in RfConfiguration.EntityNameToConfiguration)
            {
                if (!config.EntityConfiguration.SupportsSemanticSearch) continue;

                try
                {
                    if (await SyncEntityTypeAsync(entityName))
                        anySuccess = true;
                }
                catch (Exception ex)
                {
                    RfConfiguration.LogError(ex);
                }
            }

            // Only update persistent DB timestamp if at least one entity type synced successfully.
            // If all failed (e.g. embedding service crash), leave the old timestamp so the next
            // cycle retries immediately instead of skipping for another full interval.
            if (anySuccess)
            {
                var timestampData = new JObject
                {
                    ["last_sync_gmt"] = DateTime.UtcNow.ToString("o")
                };
                await AiConfiguration.DatabaseService.PutItemAsync(
                    SettingsTable, SyncTimestampKey, timestampData, overwriteIfExists: true);
            }
        }
        catch (OperationCanceledException)
        {
            RfConfiguration.LogInfo("AiVectorSync: Could not acquire mutex — another instance is running sync. Skipping.");
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError(ex);
        }
    }

    /// <summary>
    /// Sync a single entity type. Returns true if at least one entity was indexed successfully.
    /// </summary>
    private static async Task<bool> SyncEntityTypeAsync(string entityName)
    {
        var collectionName = AiVectorIndexer.GetCollectionName(entityName);

        var allEntities = await AiConfiguration.DatabaseService.ScanTableAsync(
            EntityRepositoryService.GetEntityTableName(entityName));
        if (!allEntities.IsSuccessful) return false;

        if (allEntities.Data.Items.Count == 0) return true; // No entities to sync — not a failure

        var anyIndexed = false;

        foreach (var entity in allEntities.Data.Items)
        {
            var entityId = (int)(long)entity[EntityModelAttributes.Id].NotNull();

            var vectorPoint = await AiConfiguration.VectorService.GetAsync(
                collectionName, entityId.ToString(),
                includeVector: false, includeMetadata: true, CancellationToken.None);

            if (!vectorPoint.IsSuccessful || vectorPoint.Data == null)
            {
                // Missing vector — index it
                if (await AiVectorIndexer.IndexEntityAsync(
                    entityName, entityId, entity, CancellationToken.None))
                    anyIndexed = true;
            }
            else
            {
                // Check staleness: re-embed if entity was modified after last indexing
                var indexedAt = vectorPoint.Data.Metadata?["indexed_at"]?.Value<DateTime>();
                var modifiedGmt = entity[EntityModelAttributes.ModifiedGmt]?.Value<DateTime>();
                if (indexedAt == null || modifiedGmt == null || modifiedGmt > indexedAt)
                {
                    if (await AiVectorIndexer.IndexEntityAsync(
                        entityName, entityId, entity, CancellationToken.None))
                        anyIndexed = true;
                }
                else
                {
                    anyIndexed = true; // Already indexed and up-to-date counts as success
                }
            }
        }

        return anyIndexed;
    }
}
