// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Utilities.Common;
using Microsoft.Extensions.Logging;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;
// ReSharper disable UnusedAutoPropertyAccessor.Global

// ReSharper disable UnassignedField.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace ReflectiveForms.Core;

public record EntityRepositoryServiceConfiguration(
    IDatabaseService DatabaseService,
    IMemoryService MemoryService,
    IPubSubService PubSubService,
    FileServiceConfiguration FileServiceConfiguration);
public record FileServiceConfiguration(
    IFileService FileService,
    string MediaBucketName);

public record RootUserCredentials(string Email, string Password);

public class RfConfigurationBuilder
{
    public required EntityRepositoryServiceConfiguration RepositoryServiceConfiguration
    {
        // Ensures internal access never sees null
        // ReSharper disable once UnusedMember.Local
        private get => _repositoryServiceConfiguration.NotNull();

        init
        {
            _repositoryService = new EntityRepositoryService(value.NotNull());
            _repositoryServiceConfiguration = value;
        }
    }
    private readonly EntityRepositoryServiceConfiguration? _repositoryServiceConfiguration;

    public required RootUserCredentials RootUserCredentials { get; init; }

    public required ILogger Logger { get; init; }

    public EntityRepositoryService RepositoryService => _repositoryService.NotNull();
    private readonly EntityRepositoryService? _repositoryService;

    public required EndpointConfiguration EndpointConfiguration { get; init; }

    public required IReadOnlyList<EntityConfigurationBuilderBase> EntityTypes
    {
        get => _entityTypes.NotNull();
        init
        {
            if (value.Count == 0)
            {
                throw new ArgumentException("EntityTypes cannot be empty");
            }

            var configList = value.ToList();

            EntityConfigurations = SetupEntityTypes(configList);
            _entityNameToConfiguration = EntityConfigurations.ToDictionary(
                entityConfiguration => entityConfiguration.EntityConfiguration.NotNull().EntityName
            );

            _entityTypes = configList;
        }
    }
    private readonly IReadOnlyList<EntityConfigurationBuilderBase>? _entityTypes;

    // ReSharper disable once MemberCanBePrivate.Global
    internal readonly IReadOnlyList<EntityFinalConfigurationBase>? EntityConfigurations;

    private static List<EntityFinalConfigurationBase> SetupEntityTypes(IList<EntityConfigurationBuilderBase> configList)
    {
        foreach (var config in configList)
        {
            var type = config.EntityFieldsModelType;

            if (type == null)
                throw new ArgumentException(
                    $"EntityFieldsModelType in the configuration for '{config.EntityName}' must not be null.");

            if (!typeof(EntityFieldsModel).IsAssignableFrom(type))
                throw new ArgumentException(
                    $"EntityFieldsModelType '{type.FullName}' in the configuration for '{config.EntityName}' must derive from EntityFieldsModel.");

            if (!EntityFieldsModelValidation.Validate(type, out var error))
                throw new ArgumentException(
                    $"'{type.FullName}' in the configuration for '{config.EntityName}' is invalid: {error}");
        }

        var errors = new List<string>();
        var entityNamesSeen = new HashSet<string>();
        foreach (var config in configList)
        {
            if (string.IsNullOrWhiteSpace(config.EntityName))
            {
                errors.Add("Entity name in the configuration cannot be empty");
                continue;
            }
            if (RfReservedEntities.ReservedEntityNames.Contains(config.EntityName))
            {
                errors.Add($"Entity name '{config.EntityName}' in the configuration is reserved");
            }
            if (!entityNamesSeen.Add(config.EntityName))
            {
                errors.Add($"Entity name '{config.EntityName}' in the configuration is not unique");
            }
            if (config.EntityName != config.EntityName.ToLowerInvariant())
            {
                errors.Add($"Entity name '{config.EntityName}' in the configuration must be lowercase");
            }
            if (config.EntityName.SanitizeToSlug() != config.EntityName)
            {
                errors.Add($"Entity name '{config.EntityName}' in the configuration must be a valid {EntityModelAttributes.Slug} type like 'entity-name'");
            }

            if (string.IsNullOrWhiteSpace(config.EntityReadableNameSingular))
            {
                errors.Add($"Entity readable name (singular) in the configuration cannot be empty (for '{config.EntityName}')");
            }
            if (string.IsNullOrWhiteSpace(config.EntityReadableNamePlural))
            {
                errors.Add($"Entity readable name (plural) in the configuration cannot be empty (for '{config.EntityName}')");
            }

            if (config.HasIndividualSharing)
            {
                if (!config.HasAuthor)
                {
                    errors.Add($"Entity '{config.EntityName}' has HasIndividualSharing=true but HasAuthor=false. Individual sharing requires an author field (the author is the owner).");
                }

                if (!typeof(SharableEntityFieldsModel).IsAssignableFrom(config.EntityFieldsModelType))
                {
                    errors.Add($"Entity '{config.EntityName}' has HasIndividualSharing=true but its fields model '{config.EntityFieldsModelType.FullName}' does not inherit from SharableEntityFieldsModel.");
                }
            }
        }
        if (errors.Count != 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors));

        return configList
            .Select(config =>
            {
                // Get the generic type T at runtime
                var genericType = typeof(EntityFinalConfiguration<>).MakeGenericType(config.EntityFieldsModelType);

                // Get the constructor that takes EntityConfigurationBuilder<T>
                var ctor = genericType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    [typeof(EntityConfigurationBuilder<>).MakeGenericType(config.EntityFieldsModelType)],
                    null
                );

                if (ctor == null)
                    throw new ArgumentException($"Could not find a constructor for {genericType.FullName}");

                // Invoke the constructor
                return (EntityFinalConfigurationBase)ctor.Invoke([config]).NotNull();
            })
            .Concat(RfReservedEntities.ReservedEntityTypes)
            .ToList();

    }

    private readonly IReadOnlyDictionary<string, EntityFinalConfigurationBase>? _entityNameToConfiguration;
    internal IReadOnlyDictionary<string, EntityFinalConfigurationBase> EntityNameToConfiguration =>
        _entityNameToConfiguration.NotNull();
}

