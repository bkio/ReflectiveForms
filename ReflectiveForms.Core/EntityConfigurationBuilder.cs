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
