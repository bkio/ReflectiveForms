// ReSharper disable once InvalidXmlDocComment
// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Microsoft.Extensions.Logging;
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
                _configuration = configurationBuilder;
                _initialized = true;

                _tagEntitiesCache = new TagEntitiesCache();
                _categoryEntitiesCache = new CategoryEntitiesCache();
                _iamRoleEntitiesCache = new IamRoleEntitiesCache(); //Iam cache must be initialized before user cache.
                _userEntitiesCache = new UserEntitiesCache();
            }
            catch (Exception e)
            {
                _initialized = false;
                return OperationResult<bool>.Failure(e.Message, HttpStatusCode.InternalServerError);
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

    public static EntityRepositoryService RepositoryService => GetRepositoryService();
    public static EndpointConfiguration EndpointConfiguration => GetEndpointConfiguration();
    public static IReadOnlyDictionary<string, EntityFinalConfigurationBase> EntityNameToConfiguration => GetEntityNameToConfiguration();

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
}
