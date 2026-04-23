// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using ReflectiveForms.Core.Models;
// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace ReflectiveForms.Core;

public abstract class EntityConfigurationBuilderBase
{
    internal EntityConfigurationBuilderBase(Type entityFieldsModelType)
    {
        EntityFieldsModelType = entityFieldsModelType;
    }
    internal Type EntityFieldsModelType { get; init; }

    /// <summary>
    /// Specifies the unique name of the entity, used for identification and configuration purpose.
    /// Example: "post", "page", "product", "category", "tag", etc.
    /// Should be unique.
    /// Should follow "slug" naming convention. (xxx, xxx-yyy-zzz - all lower-case)
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Example: Analysis, Mouse, Foot, Criterion
    /// </summary>
    public required string EntityReadableNameSingular { get; init; }

    /// <summary>
    /// Example: Analyses, Mice, Feet, Criteria
    /// </summary>
    public required string EntityReadableNamePlural { get; init; }

    /// <summary>
    /// Should this entity type support frontend editing?
    /// When false the frontend should display the entity in read-only / view mode.
    /// Access-control for individual CRUD operations is handled by the IAM role system.
    /// </summary>
    public required bool SupportsFrontendEdit { get; init; }

    /// <summary>
    /// Should this entity type have a parent-child relationship?
    /// </summary>
    public required bool HasParentChildRelationship { get; init; }

    /// <summary>
    /// Should this entity type have an author field?
    /// </summary>
    public required bool HasAuthor { get; init; }

    /// <summary>
    /// Should this entity type support tags?
    /// </summary>
    public required bool HasTags { get; init; }

    /// <summary>
    /// Should this entity type support categories?
    /// </summary>
    public required bool HasCategories { get; init; }

    /// <summary>
    /// Indicates whether entity titles must be unique across all entities of this entity type, globally
    /// </summary>
    public required bool RequireGlobalTitleUniqueness { get; init; }

    /// <summary>
    /// An optional delegate that defines a custom sanity check for an entity's title during update/create operations.
    /// </summary>
    public required Func<TitleRenderedModel, Task<bool>>? OptionalTitleSanityCheck { get; init; }

    /// <summary>
    /// Should this entity type support individual sharing (per-entity access control)?
    /// When true, each entity instance can be shared with specific users and roles,
    /// and the framework automatically enforces per-entity access checks on READ, UPDATE, DELETE, PEEK_ALL, and entity locking.
    /// Requires HasAuthor = true and the entity's fields model to inherit from SharableEntityFieldsModel.
    /// An admin role for this entity type is automatically created and maintained at startup.
    /// </summary>
    public bool HasIndividualSharing { get; init; }

    /// <summary>
    /// Optional custom frontend route for the entity's list page.
    /// When set, the sidebar and navigation use this route instead of the generic /entities/{entityName} path.
    /// Required when HasIndividualSharing is true (sharing entities need custom pages).
    /// Example: "/sheets" for rf-sheets.
    /// </summary>
    public string? CustomFrontendListRoute { get; init; }

    /// <summary>
    /// A short plain-text description of what this entity type represents.
    /// Used by the LLM as context during AI entity generation, semantic search,
    /// and other AI features. Required when any AI feature is enabled for this entity type.
    /// Example: "A blog post with rich-text content, SEO metadata, and publication workflow."
    /// </summary>
    public string? EntityDescription { get; init; }

    /// <summary>
    /// Enable semantic search for this entity type.
    /// Embeds text-bearing fields on every save and indexes them in the vector DB.
    /// Requires <see cref="Ai.AiServiceConfiguration"/> to be set on <see cref="RfConfigurationBuilder"/>.
    /// </summary>
    public bool SupportsSemanticSearch { get; init; }

    /// <summary>
    /// Enable natural-language entity creation for this entity type.
    /// Adds a "Create with AI" capability to the frontend.
    /// Requires <see cref="Ai.AiServiceConfiguration"/> to be set on <see cref="RfConfigurationBuilder"/>.
    /// </summary>
    public bool SupportsAiGeneration { get; init; }

    /// <summary>
    /// Enable AI-powered revision diff summaries for this entity type.
    /// Adds an "AI Summary" section to the revision diff page.
    /// Requires <see cref="Ai.AiServiceConfiguration"/> to be set on <see cref="RfConfigurationBuilder"/>.
    /// </summary>
    public bool SupportsAiDiffSummary { get; init; }

    /// <summary>
    /// Enable natural-language filtering for this entity type.
    /// Allows users to type filters in plain English on the entity list page.
    /// Requires <see cref="Ai.AiServiceConfiguration"/> to be set on <see cref="RfConfigurationBuilder"/>.
    /// </summary>
    public bool SupportsNaturalLanguageFilter { get; init; }
}

public sealed class EntityConfigurationBuilder<T>() : EntityConfigurationBuilderBase(typeof(T))
    where T : EntityFieldsModel, new()
{
    /// <summary>
    /// Optional configuration for entity lifecycle hooks, allowing custom actions
    /// to be executed right after create, update, or delete operations on this entity; just before publishing the change to the pub/sub service.
    /// These are good for updating the entity for post-entity-change tweaks, and auto-calculations.
    /// </summary>
    public EntityOnChangedHooksSetup<T>? HooksSetup { get; init; }
}

public class EntityOnChangedHooksSetup<T> where T : EntityFieldsModel, new()
{
    /// <summary>
    /// This hook will be called after an entity is successfully created, all other relevant entities referencing this entity are accordingly updated, but before publishing the change to the pub/sub service.
    /// Therefore, it is a good place to update the entity for post-entity-change tweaks, and auto-calculations.
    /// However, it is essential to update the entity with UpdaterIdentity.DuringHookCallUpdate(), otherwise it will cause infinite-loop.
    /// </summary>
    public Func<PostCreateHookModel<T>, CancellationToken, Task>? PostCreateHook { get; init; }

    /// <summary>
    /// This hook will be called after an entity is successfully updated, all other relevant entities referencing this entity are accordingly updated, but before publishing the change to the pub/sub service.
    /// Therefore, it is a good place to update the entity for post-entity-change tweaks, and auto-calculations.
    /// However, it is essential to update the entity with UpdaterIdentity.DuringHookCallUpdate(), otherwise it will cause infinite-loop.
    /// </summary>
    public Func<PostUpdateHookModel<T>, CancellationToken, Task>? PostUpdateHook { get; init; }

    /// <summary>
    /// This hook will be called after an entity is successfully deleted, all other relevant entities referencing this entity are accordingly updated, but before publishing the change to the pub/sub service.
    /// </summary>
    public Func<PostDeleteHookModel<T>, CancellationToken, Task>? PostDeleteHook { get; init; }
}

public record PostCreateHookModel<T>(string EntityName, int NewId, EntityModel<T> FinalBody) where T : EntityFieldsModel, new();
public record PostUpdateHookModel<T>(string EntityName, int Id, EntityModel<T> OldBody, EntityModel<T> NewFinalBody) where T : EntityFieldsModel, new();
public record PostDeleteHookModel<T>(string EntityName, int Id, EntityModel<T> LastBody) where T : EntityFieldsModel, new();
