// ReSharper disable once InvalidXmlDocComment
// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Microsoft.Extensions.Logging;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Repositories;

namespace ReflectiveForms.Core;

public static class RfConfiguration
{
    internal static OperationResult<bool> Initialize(RfConfigurationBuilder configurationBuilder)
    {
        if (_initialized)
            return OperationResult<bool>.Failure("Already initialized.", HttpStatusCode.Conflict);
        lock (InitializerLock)
        {
            try
            {
                // Validate AI flags: if any entity has AI features enabled, AiServiceConfiguration must be set
                var aiConfig = configurationBuilder.AiServiceConfiguration;
                foreach (var entityConfig in configurationBuilder.EntityConfigurations!)
                {
                    var ec = entityConfig.EntityConfiguration;
                    var hasAnyAiFeature = ec.SupportsSemanticSearch || ec.SupportsAiGeneration ||
                                          ec.SupportsAiDiffSummary || ec.SupportsNaturalLanguageFilter;

                    if (hasAnyAiFeature && aiConfig == null)
                    {
                        _initialized = false;
                        return OperationResult<bool>.Failure(
                            $"Entity '{ec.EntityName}' has AI features enabled but AiServiceConfiguration is null on RfConfigurationBuilder. " +
                            "Either disable AI features on this entity or provide an AiServiceConfiguration.",
                            HttpStatusCode.BadRequest);
                    }

                    if (hasAnyAiFeature && string.IsNullOrWhiteSpace(ec.EntityDescription))
                    {
                        _initialized = false;
                        return OperationResult<bool>.Failure(
                            $"Entity '{ec.EntityName}' has AI features enabled but EntityDescription is not set. " +
                            "Provide a short description of what this entity type represents so the LLM has context.",
                            HttpStatusCode.BadRequest);
                    }
                }

                _configuration = configurationBuilder;
                _initialized = true;

                _tagEntitiesCache = new TagEntitiesCache();
                _categoryEntitiesCache = new CategoryEntitiesCache();
                _iamRoleEntitiesCache = new IamRoleEntitiesCache(); //Iam cache must be initialized before user cache.
                _userEntitiesCache = new UserEntitiesCache();
                if (configurationBuilder.SheetsEnabled)
                    _sheetEntitiesCache = new SheetEntitiesCache();
            }
            catch (Exception e)
            {
                _initialized = false;
                var baseEx = e.GetBaseException();
                return OperationResult<bool>.Failure(baseEx.Message, HttpStatusCode.InternalServerError);
            }
        }

        // AI initialization — OUTSIDE the lock block to avoid deadlock on async calls
        if (configurationBuilder.AiServiceConfiguration != null)
        {
            try
            {
                var aiInitResult = InitializeAiInternalAsync(configurationBuilder.AiServiceConfiguration).GetAwaiter().GetResult();
                if (!aiInitResult.IsSuccessful)
                {
                    _initialized = false; // rollback so Initialize() can be retried after fixing config
                    return aiInitResult;
                }
            }
            catch (Exception e)
            {
                _initialized = false;
                return OperationResult<bool>.Failure($"AI initialization failed: {e.GetBaseException().Message}", HttpStatusCode.InternalServerError);
            }
        }

        return OperationResult<bool>.Success(true);
    }

    private static bool _initialized;

    private static readonly object InitializerLock = new();

    private static RfConfigurationBuilder? _configuration;

    public static TagEntitiesCache TagEntitiesCache => _tagEntitiesCache ?? throw new InvalidOperationException("Not initialized");
    private static TagEntitiesCache? _tagEntitiesCache;

    public static CategoryEntitiesCache CategoryEntitiesCache => _categoryEntitiesCache ?? throw new InvalidOperationException("Not initialized");
    private static CategoryEntitiesCache? _categoryEntitiesCache;

    public static IamRoleEntitiesCache IamRoleEntitiesCache => _iamRoleEntitiesCache ?? throw new InvalidOperationException("Not initialized");
    private static IamRoleEntitiesCache? _iamRoleEntitiesCache;

    public static UserEntitiesCache UserEntitiesCache => _userEntitiesCache ?? throw new InvalidOperationException("Not initialized");
    private static UserEntitiesCache? _userEntitiesCache;

    public static SheetEntitiesCache SheetEntitiesCache => _sheetEntitiesCache ?? throw new InvalidOperationException("Sheets are not enabled or not initialized");
    private static SheetEntitiesCache? _sheetEntitiesCache;

    /// <summary>
    /// Whether the RF Sheets spreadsheet system is enabled.
    /// </summary>
    public static bool SheetsEnabled => _configuration?.SheetsEnabled ?? true;

    public static EntityRepositoryService RepositoryService => GetRepositoryService();
    public static EndpointConfiguration EndpointConfiguration => GetEndpointConfiguration();
    public static IReadOnlyDictionary<string, EntityFinalConfigurationBase> EntityNameToConfiguration => GetEntityNameToConfiguration();

    /// <summary>
    /// Returns the AI service configuration if set, or null if AI is disabled.
    /// </summary>
    public static AiServiceConfiguration? AiServiceConfiguration => _configuration?.AiServiceConfiguration;

    /// <summary>
    /// Inactivity timeout in milliseconds before a user's edit lock is released.
    /// </summary>
    public static int EditInactivityTimeoutMs => _configuration?.EditInactivityTimeoutMs ?? 600_000;

    internal static RootUserCredentials RootUserCredentials => _configuration.NotNull().RootUserCredentials;

    internal static void LogInfo(string message)
    {
        if (!_initialized)
            throw new InvalidOperationException("Not initialized");

        _configuration?.Logger.LogInformation(message);
    }

    internal static void LogError(Exception e)
    {
        if (!_initialized)
            throw new InvalidOperationException("Not initialized");

        _configuration?.Logger.LogError(e, e.Message);
    }
    internal static void LogError(string message)
    {
        LogError(new Exception(message));
    }
    internal static void LogError(string message, Exception e)
    {
        LogError(new AggregateException(new Exception(message), e));
    }

    private static EntityRepositoryService GetRepositoryService()
    {
        if (!_initialized)
            throw new InvalidOperationException("Not initialized");

        return _configuration != null
            ? _configuration.RepositoryService
            : throw new InvalidOperationException("Configuration is not set");
    }

    private static EndpointConfiguration GetEndpointConfiguration()
    {
        if (!_initialized)
            throw new InvalidOperationException("Not initialized");

        return _configuration != null
            ? _configuration.EndpointConfiguration
            : throw new InvalidOperationException("Configuration is not set");
    }
    private static IReadOnlyDictionary<string, EntityFinalConfigurationBase> GetEntityNameToConfiguration()
    {
        if (!_initialized)
            throw new InvalidOperationException("Not initialized");

        return _configuration != null
            ? _configuration.EntityNameToConfiguration
            : throw new InvalidOperationException("Configuration is not set");
    }

    private static async Task<OperationResult<bool>> InitializeAiInternalAsync(AiServiceConfiguration aiConfig)
    {
        // Detect embedding dimensions using the light LLM (it handles embeddings)
        var embeddingLlm = aiConfig.EmbeddingLlmService ?? aiConfig.LightLlmService;
        var probe = await embeddingLlm.CreateEmbeddingAsync("dimension probe");
        if (!probe.IsSuccessful)
            return OperationResult<bool>.Failure($"Failed to probe embedding dimensions: {probe.ErrorMessage}", HttpStatusCode.InternalServerError);

        var dimensions = probe.Data.Length;

        // Populate AiConfiguration static singleton with all service references
        var repoService = _configuration.NotNull().RepositoryService;
        AiConfiguration.Initialize(
            db: repoService.DatabaseServiceInstance,
            memory: repoService.MemoryServiceInstance,
            vector: aiConfig.VectorService,
            heavyLlm: aiConfig.HeavyLlmService,
            lightLlm: aiConfig.LightLlmService,
            embeddingDimensions: dimensions,
            embeddingLlm: aiConfig.EmbeddingLlmService);

        // Create vector collections for entities that opt in
        foreach (var (entityName, config) in EntityNameToConfiguration)
        {
            if (config.EntityConfiguration.SupportsSemanticSearch)
            {
                var result = await aiConfig.VectorService.EnsureCollectionExistsAsync(
                    $"rf_semantic_{entityName}", dimensions, CrossCloudKit.Interfaces.Enums.VectorDistanceMetric.Cosine);
                if (!result.IsSuccessful)
                    return OperationResult<bool>.Failure(
                        $"Failed to create vector collection for '{entityName}': {result.ErrorMessage}", HttpStatusCode.InternalServerError);
            }
        }

        // Start hourly sync timer
        AiVectorSync.StartSyncTimer(aiConfig.SyncInterval);

        return OperationResult<bool>.Success(true);
    }
}
