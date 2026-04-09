// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core;

public sealed class TagEntitiesCache() : EntitiesCacheBase<TaxonomyEntityFieldsModel>(RfReservedEntities.TagsEntityName);
public sealed class CategoryEntitiesCache() : EntitiesCacheBase<TaxonomyEntityFieldsModel>(RfReservedEntities.CategoriesEntityName);
public sealed class IamRoleEntitiesCache : EntitiesCacheBase<IamRoleEntityFieldsModel>
{
    public IamRoleEntitiesCache() : base(RfReservedEntities.IamRoleEntityName)
    {
        RootManager.EnsureOwnerRoleExistAsync(this).GetAwaiter().GetResult();
        RootManager.EnsureSheetsEditorRoleExistAsync(this).GetAwaiter().GetResult();
    }
}
public sealed class UserEntitiesCache : EntitiesCacheBase<UserEntityFieldsModel>
{
    public UserEntitiesCache() : base(RfReservedEntities.UsersEntityName) => RootManager.EnsureRootUserExistsAsync(this).GetAwaiter().GetResult();
}
public sealed class SheetEntitiesCache() : EntitiesCacheBase<RfSheetEntityFieldsModel>(RfReservedEntities.SheetsEntityName);

public static class RfReservedEntities
{
    public const string UsersEntityName = "users";
    public const string IamRoleEntityName = "iam-role";
    public const string TagsEntityName = "tags";
    public const string CategoriesEntityName = "categories";
    public const string MediaEntityName = "media";
    public const string SheetsEntityName = "rf-sheets";
    public static readonly IReadOnlySet<string> ReservedEntityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        UsersEntityName,
        IamRoleEntityName,
        TagsEntityName,
        CategoriesEntityName,
        MediaEntityName,
        SheetsEntityName
    };

    public static readonly IReadOnlyList<EntityFinalConfigurationBase> ReservedEntityTypes = new List<EntityFinalConfigurationBase>
    {
        new EntityFinalConfiguration<UserEntityFieldsModel>(new EntityConfigurationBuilder<UserEntityFieldsModel>
        {
            EntityName = UsersEntityName,
            EntityReadableNameSingular = "User",
            EntityReadableNamePlural = "Users",
            SupportsFrontendEdit = true,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            HasParentChildRelationship = false,
            RequireGlobalTitleUniqueness = true,
            OptionalTitleSanityCheck = title => Task.FromResult(title.Text != RootManager.RootUserTitle),
            HooksSetup = new EntityOnChangedHooksSetup<UserEntityFieldsModel>
            {
                PostCreateHook = async (p, ctx) => await UserEntityHookOnChanged.OnUserCreated(p, ctx),
                PostUpdateHook = async (p, ctx) => await UserEntityHookOnChanged.OnUserUpdated(p, ctx),
                PostDeleteHook = async (p, ctx) => await UserEntityHookOnChanged.OnUserDeleted(p, ctx)
            }
        }),
        new EntityFinalConfiguration<IamRoleEntityFieldsModel>(new EntityConfigurationBuilder<IamRoleEntityFieldsModel>
        {
            EntityName = IamRoleEntityName,
            EntityReadableNameSingular = "IAM Role",
            EntityReadableNamePlural = "IAM Roles",
            SupportsFrontendEdit = true,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            HasParentChildRelationship = false,
            RequireGlobalTitleUniqueness = true,
            OptionalTitleSanityCheck = title => Task.FromResult(title.Text != RootManager.OwnerRoleTitle && title.Text != RootManager.SheetsEditorRoleTitle),
        }),
        new EntityFinalConfiguration<TaxonomyEntityFieldsModel>(new EntityConfigurationBuilder<TaxonomyEntityFieldsModel>
        {
            EntityName = TagsEntityName,
            EntityReadableNameSingular = "Tag",
            EntityReadableNamePlural = "Tags",
            SupportsFrontendEdit = true,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            HasParentChildRelationship = true,
            RequireGlobalTitleUniqueness = true,
            OptionalTitleSanityCheck = null
        }),
        new EntityFinalConfiguration<TaxonomyEntityFieldsModel>(new EntityConfigurationBuilder<TaxonomyEntityFieldsModel>
        {
            EntityName = CategoriesEntityName,
            EntityReadableNameSingular = "Category",
            EntityReadableNamePlural = "Categories",
            SupportsFrontendEdit = true,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            HasParentChildRelationship = true,
            RequireGlobalTitleUniqueness = true,
            OptionalTitleSanityCheck = null
        }),
        new EntityFinalConfiguration<MediaEntityFieldsModel>(new EntityConfigurationBuilder<MediaEntityFieldsModel>
        {
            EntityName = MediaEntityName,
            EntityReadableNameSingular = "Media",
            EntityReadableNamePlural = "Media",
            SupportsFrontendEdit = true,
            HasAuthor = true,
            HasTags = true,
            HasCategories = true,
            HasParentChildRelationship = false,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = null,
            HooksSetup = new EntityOnChangedHooksSetup<MediaEntityFieldsModel>
            {
                PostCreateHook = async (p, ctx) => await MediaEntityHookOnChanged.OnMediaCreated(p, ctx),
                PostUpdateHook = async (p, ctx) => await MediaEntityHookOnChanged.OnMediaUpdated(p, ctx),
                PostDeleteHook = async (p, ctx) => await MediaEntityHookOnChanged.OnMediaDeleted(p, ctx)
            }
        }),
        new EntityFinalConfiguration<RfSheetEntityFieldsModel>(new EntityConfigurationBuilder<RfSheetEntityFieldsModel>
        {
            EntityName = SheetsEntityName,
            EntityReadableNameSingular = "Sheet",
            EntityReadableNamePlural = "Sheets",
            SupportsFrontendEdit = false,
            HasAuthor = true,
            HasTags = false,
            HasCategories = false,
            HasParentChildRelationship = false,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = null
        })
    };
}
