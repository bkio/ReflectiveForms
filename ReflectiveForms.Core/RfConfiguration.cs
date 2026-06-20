// ReSharper disable once InvalidXmlDocComment
// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
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
                // When AiServiceConfiguration is null, AI features are silently disabled.
                // This matches the documented behavior on RfConfigurationBuilder.
                var aiConfig = configurationBuilder.AiServiceConfiguration;
                if (aiConfig != null)
                {
                    foreach (var entityConfig in configurationBuilder.EntityConfigurations!)
                    {
                        var ec = entityConfig.EntityConfiguration;
                        var hasAnyAiFeature = ec.SupportsSemanticSearch || ec.SupportsAiGeneration ||
                                              ec.SupportsAiDiffSummary || ec.SupportsNaturalLanguageFilter;

                        if (hasAnyAiFeature && string.IsNullOrWhiteSpace(ec.EntityDescription))
                        {
                            _initialized = false;
                            return OperationResult<bool>.Failure(
                                $"Entity '{ec.EntityName}' has AI features enabled but EntityDescription is not set. " +
                                "Provide a short description of what this entity type represents so the LLM has context.",
                                HttpStatusCode.BadRequest);
                        }
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

        // Byte-field safety scan — warns about byte-typed fields that may cause
        // silent truncation in consumers using System.Text.Json.
        ScanForByteFields(configurationBuilder);

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

    /// <summary>
    /// Reserved entity types to hide from the frontend sidebar navigation and dashboard.
    /// </summary>
    public static IReadOnlyList<ReservedEntityType>? ReservedEntityTypesToHideInNavigation => _configuration?.ReservedEntityTypesToHideInNavigation;

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

    /// <summary>
    /// Scan all registered entity models for byte-typed fields.
    /// System.Text.Json consumers may silently truncate byte values &gt; 127
    /// in nested arrays. This scan logs warnings so operators are aware.
    /// </summary>
    private static void ScanForByteFields(RfConfigurationBuilder configurationBuilder)
    {
        if (configurationBuilder.Logger == null) return;

        foreach (var entityConfig in configurationBuilder.EntityConfigurations!)
        {
            var fieldsType = entityConfig.EntityConfiguration.EntityFieldsModelType;
            ScanTypeForByteFields(fieldsType, entityConfig.EntityConfiguration.EntityName, "fields", configurationBuilder.Logger);
        }
    }

    private static void ScanTypeForByteFields(Type type, string entityName, string path, ILogger logger)
    {
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m is FieldInfo or PropertyInfo))
        {
            // Only scan members that carry an RF field attribute — matches the
            // proven pattern in EntityModelDefaultsBuilder.
            if (!Attribute.IsDefined(member, typeof(Field), true))
                continue;

            var memberType = member is FieldInfo fi ? fi.FieldType : ((PropertyInfo)member).PropertyType;
            var jsonProp = member.GetCustomAttribute<JsonPropertyAttribute>(true);
            var name = jsonProp?.PropertyName ?? member.Name;
            var fullPath = $"{path}.{name}";

            if (memberType == typeof(byte))
            {
                var context = type.GetCustomAttribute<Repeater>(true) != null ||
                              type.GetCustomAttribute<Group>() != null
                    ? " (inside nested type — higher risk)"
                    : "";
                logger.LogWarning(
                    "Byte-typed field '{Path}' on entity '{Entity}'{Context}. " +
                    "System.Text.Json consumers may silently truncate values > 127. " +
                    "Consider using int instead.",
                    fullPath, entityName, context);
            }

            // Recurse into nested model types, matching the structural pattern
            // used by EntityModelDefaultsBuilder:
            //   List<T> → recurse into element type T
            //   Class (not string) → recurse into the type itself
            if (memberType.IsGenericType &&
                memberType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = memberType.GetGenericArguments()[0];
                if (elementType.IsClass && elementType != typeof(string))
                    ScanTypeForByteFields(elementType, entityName, fullPath, logger);
            }
            else if (memberType.IsClass && memberType != typeof(string))
            {
                ScanTypeForByteFields(memberType, entityName, fullPath, logger);
            }
        }
    }
}
