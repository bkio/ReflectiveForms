// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Endpoints;

internal static class RootManager
{
    private const string OwnerRoleTitleConstant = "Owner";
    internal static string OwnerRoleTitle { get; private set; } = OwnerRoleTitleConstant;

    private const string RootUserTitleConstant = "Root User";
    internal static string RootUserTitle { get; private set; } = RootUserTitleConstant;

    private static int _ownerRoleId = -1;
    internal static int OwnerRoleId => _ownerRoleId;

    private static int _rootUserId = -1;
    internal static int RootUserId => _rootUserId;

    // ── Sharing admin roles (one per entity type with HasIndividualSharing) ──
    // Key: entity name, Value: role id
    private static readonly Dictionary<string, int> SharingAdminRoleIds = new();
    // Key: entity name, Value: role title constant (e.g. "Sheets Admin")
    private static readonly Dictionary<string, string> SharingAdminRoleTitles = new();

    /// <summary>
    /// Returns the admin role title for a sharing entity type, or null if not applicable.
    /// Used by the IAM role title-sanity-check to prevent renaming auto-generated admin roles.
    /// </summary>
    internal static bool IsSharingAdminRoleTitle(string title)
    {
        return SharingAdminRoleTitles.Values.Contains(title);
    }

    /// <summary>
    /// Returns true if the given entity (identified by entity type name and ID) is a system-managed
    /// entity created by the framework (root user, owner role, sharing admin roles).
    /// System-managed entities must not be updated or deleted by any user.
    /// </summary>
    internal static bool IsSystemManagedEntity(string entityName, int entityId)
    {
        if (entityName == RfReservedEntities.UsersEntityName && _rootUserId > 0 && entityId == _rootUserId)
            return true;

        if (entityName == RfReservedEntities.IamRoleEntityName)
        {
            if (_ownerRoleId > 0 && entityId == _ownerRoleId)
                return true;

            if (SharingAdminRoleIds.Values.Contains(entityId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the user has the Owner role or the sharing-admin role for the given entity type,
    /// granting them full admin access to all entities of that type.
    /// </summary>
    internal static bool HasEntityAdminRole(string entityName, UserEntityFieldsModel userFields)
    {
        var hasOwner = _ownerRoleId > 0 && userFields.Roles.Any(r => r.RoleId == _ownerRoleId);
        if (hasOwner) return true;

        if (SharingAdminRoleIds.TryGetValue(entityName, out var adminRoleId) && adminRoleId > 0)
            return userFields.Roles.Any(r => r.RoleId == adminRoleId);

        return false;
    }

    public static async Task EnsureOwnerRoleExistAsync(IamRoleEntitiesCache iamRoleEntitiesCache, CancellationToken cancellationToken = default)
    {
        var ownerRole = iamRoleEntitiesCache.FindEntityByFilterAndGetCopy(f => f.Title.Text == OwnerRoleTitleConstant);

        OwnerRoleTitle = StringUtilities.GenerateRandomString(32); // This is for temporarily disabling iam role title-sanity-check.
        try
        {
            if (ownerRole == null)
            {
                var putResult = await RfConfiguration.RepositoryService.PutOneAsync<IamRoleEntityFieldsModel>(
                    RfReservedEntities.IamRoleEntityName,
                    new EntityModel<IamRoleEntityFieldsModel>
                    {
                        Title = new TitleRenderedModel
                        {
                            Text = OwnerRoleTitleConstant
                        },
                        Fields = new IamRoleEntityFieldsModel
                        {
                            Capabilities = OwnerRoleCapabilities
                        }
                    }.FromObjectWithPolymorphism(),
                    cancellationToken);
                if (!putResult.IsSuccessful
                    || !putResult.Data.TryGetTypedValue(EntityModelAttributes.Id, out int roleId))
                    throw new Exception($"Failed to create owner role with title {OwnerRoleTitleConstant}. Error: {putResult.ErrorMessage}");

                _ownerRoleId = roleId;
            }
            else
            {
                _ownerRoleId = ownerRole.Id;

                var currentOwnerCapabilities = OwnerRoleCapabilities;

                var ownerRoleFields = ownerRole.Fields;

                if (currentOwnerCapabilities.Count != ownerRoleFields.Capabilities.Count
                    || currentOwnerCapabilities.Except(ownerRoleFields.Capabilities).Any()
                    || ownerRoleFields.Capabilities.Except(currentOwnerCapabilities).Any())
                {
                    ownerRoleFields.Capabilities = currentOwnerCapabilities;

                    //We cannot use UsersCache here because it is not initialized yet.
                    var rootUserGetResult = OperationResult<JObject>.Failure(
                        "Not found.",
                        HttpStatusCode.NotFound
                    );
                    await foreach (var result in RfConfiguration.RepositoryService
                                       .GetByFilterAsync(
                                           RfReservedEntities.UsersEntityName,
                                           ConditionBuilder.AttributeEquals(
                                               $"{EntityModelAttributes.Title}.{EntityModelAttributes.TitleRendered}",
                                               RootUserTitleConstant),
                                           1,
                                           cancellationToken))
                    {
                        rootUserGetResult = result;
                        break;
                    }

                    EntityUpdaterIdentity updaterIdentity;
                    if (rootUserGetResult.IsSuccessful)
                    {
                        var rootUser = rootUserGetResult.Data.ToObjectWithPolymorphism<EntityModel<UserEntityFieldsModel>>();
                        var rootUserFields = rootUser.NotNull().Fields;
                        updaterIdentity = EntityUpdaterIdentity.NormalUpdate(rootUser.NotNull().Id, rootUserFields.EmailAddress);
                    }
                    else
                    {
                        //System update it must be, because this is called potentially before any user is created. (During first startup)
                        //So the risk is that if there are other instances of the app running, they will not be able to receive this update to update their IAM role cache.
                        updaterIdentity = EntityUpdaterIdentity.DuringHookCallUpdate();
                    }

                    var updateResult = await RfConfiguration.RepositoryService.UpdateOneAsync<IamRoleEntityFieldsModel>(
                        RfReservedEntities.IamRoleEntityName,
                        ownerRole.Id,
                        ownerRole.FromObjectWithPolymorphism(),
                        updaterIdentity,
                        cancellationToken);
                    if (!updateResult.IsSuccessful)
                        throw new Exception($"Failed to update owner role with id {ownerRole.Id} with the new capabilities. Reason: {updateResult.ErrorMessage} ({updateResult.StatusCode})");
                }
            }
        }
        finally
        {
            OwnerRoleTitle = OwnerRoleTitleConstant; // Revert to the original value.
        }
    }

    internal static async Task EnsureRootUserExistsAsync(EntitiesCacheBase<UserEntityFieldsModel> usersCache, CancellationToken cancellationToken = default)
    {
        if (_ownerRoleId <= 0)
            throw new InvalidOperationException("Owner role id is not set.");

        var rootUserCredentials = RfConfiguration.RootUserCredentials;
        var newEmail = rootUserCredentials.Email.ToLowerInvariant();
        var newPasswordSha256 = CryptographyUtilities.CalculateStringSha256(rootUserCredentials.Password);

        var rootUser = usersCache.FindEntityByFilterAndGetCopy(f => f.Title.Text == RootUserTitleConstant);

        RootUserTitle = StringUtilities.GenerateRandomString(32); // This is for temporarily disabling root user title-sanity-check.
        try
        {
            if (rootUser != null)
            {
                _rootUserId = rootUser.Id;

                var fields = rootUser.Fields;
                if (fields.EmailAddress == newEmail
                    && fields.PasswordSha256 == newPasswordSha256)
                    return;

                fields.EmailAddress = newEmail;
                fields.PasswordSha256 = newPasswordSha256;

                var updateResult = await RfConfiguration.RepositoryService.UpdateOneAsync<UserEntityFieldsModel>(
                    RfReservedEntities.UsersEntityName,
                    rootUser.Id,
                    rootUser.FromObjectWithPolymorphism(),
                    EntityUpdaterIdentity.NormalUpdate(rootUser.Id, newEmail),
                    cancellationToken);
                if (!updateResult.IsSuccessful)
                    throw new Exception($"Failed to update root user with id {rootUser.Id} with the new credentials.");
            }
            else
            {
                var putResult = await RfConfiguration.RepositoryService.PutOneAsync<UserEntityFieldsModel>(
                    RfReservedEntities.UsersEntityName,
                    new EntityModel<UserEntityFieldsModel>
                    {
                        Title = new TitleRenderedModel
                        {
                            Text = RootUserTitleConstant
                        },
                        Fields = new UserEntityFieldsModel
                        {
                            EmailAddress = newEmail,
                            PasswordSha256 = newPasswordSha256,
                            Roles =
                            [
                                new UserRoleAssignmentModel
                                {
                                    RoleId = _ownerRoleId
                                }
                            ]
                        }
                    }.FromObjectWithPolymorphism(),
                    cancellationToken);
                if (!putResult.IsSuccessful
                    || !putResult.Data.TryGetTypedValue(EntityModelAttributes.Id, out int rootId))
                    throw new Exception($"Failed to create root user with email {newEmail}. Error: {putResult.ErrorMessage}");

                _rootUserId = rootId;
            }
        }
        finally
        {
            RootUserTitle = RootUserTitleConstant; // Revert to the original value.
        }
    }

    private static List<IamRoleCapabilitiesModel> OwnerRoleCapabilities => RfConfiguration.EntityNameToConfiguration.Keys
        .Select(e => new IamRoleCapabilitiesModel
        {
            EntityType = e,
            AllowCreate = true,
            AllowDelete = true,
            AllowPeekAll = true,
            AllowRead = true,
            AllowUpdate = true
        }).ToList();

    public static async Task EnsureSharingAdminRolesExistAsync(IamRoleEntitiesCache iamRoleEntitiesCache, CancellationToken cancellationToken = default)
    {
        // Find all entity types with HasIndividualSharing and ensure an admin role for each
        foreach (var (entityName, config) in RfConfiguration.EntityNameToConfiguration)
        {
            if (!config.EntityConfiguration.HasIndividualSharing) continue;

            var roleTitleConstant = $"{config.EntityConfiguration.EntityReadableNamePlural} Admin";

            var existingRole = iamRoleEntitiesCache.FindEntityByFilterAndGetCopy(f => f.Title.Text == roleTitleConstant);

            // Temporarily set the title to a random string to bypass iam role title-sanity-check
            // (same pattern as OwnerRoleTitle in EnsureOwnerRoleExistAsync)
            var tempTitle = StringUtilities.GenerateRandomString(32);
            SharingAdminRoleTitles[entityName] = tempTitle;

            try
            {
                var capabilities = GetSharingAdminCapabilities(entityName);

                if (existingRole == null)
                {
                    var putResult = await RfConfiguration.RepositoryService.PutOneAsync<IamRoleEntityFieldsModel>(
                        RfReservedEntities.IamRoleEntityName,
                        new EntityModel<IamRoleEntityFieldsModel>
                        {
                            Title = new TitleRenderedModel
                            {
                                Text = roleTitleConstant
                            },
                            Fields = new IamRoleEntityFieldsModel
                            {
                                Capabilities = capabilities
                            }
                        }.FromObjectWithPolymorphism(),
                        cancellationToken);
                    if (!putResult.IsSuccessful
                        || !putResult.Data.TryGetTypedValue(EntityModelAttributes.Id, out int roleId))
                        throw new Exception($"Failed to create {roleTitleConstant} role. Error: {putResult.ErrorMessage}");

                    SharingAdminRoleIds[entityName] = roleId;
                }
                else
                {
                    SharingAdminRoleIds[entityName] = existingRole.Id;

                    var existingCapabilities = existingRole.Fields.Capabilities;

                    if (capabilities.Count != existingCapabilities.Count
                        || capabilities.Except(existingCapabilities).Any()
                        || existingCapabilities.Except(capabilities).Any())
                    {
                        existingRole.Fields.Capabilities = capabilities;

                        var rootUserGetResult = OperationResult<JObject>.Failure("Not found.", HttpStatusCode.NotFound);
                        await foreach (var result in RfConfiguration.RepositoryService
                                           .GetByFilterAsync(
                                               RfReservedEntities.UsersEntityName,
                                               ConditionBuilder.AttributeEquals(
                                                   $"{EntityModelAttributes.Title}.{EntityModelAttributes.TitleRendered}",
                                                   RootUserTitleConstant),
                                               1,
                                               cancellationToken))
                        {
                            rootUserGetResult = result;
                            break;
                        }

                        EntityUpdaterIdentity updaterIdentity;
                        if (rootUserGetResult.IsSuccessful)
                        {
                            var rootUser = rootUserGetResult.Data.ToObjectWithPolymorphism<EntityModel<UserEntityFieldsModel>>();
                            var rootUserFields = rootUser.NotNull().Fields;
                            updaterIdentity = EntityUpdaterIdentity.NormalUpdate(rootUser.NotNull().Id, rootUserFields.EmailAddress);
                        }
                        else
                        {
                            updaterIdentity = EntityUpdaterIdentity.DuringHookCallUpdate();
                        }

                        var updateResult = await RfConfiguration.RepositoryService.UpdateOneAsync<IamRoleEntityFieldsModel>(
                            RfReservedEntities.IamRoleEntityName,
                            existingRole.Id,
                            existingRole.FromObjectWithPolymorphism(),
                            updaterIdentity,
                            cancellationToken);
                        if (!updateResult.IsSuccessful)
                            throw new Exception($"Failed to update {roleTitleConstant} role with id {existingRole.Id}. Reason: {updateResult.ErrorMessage} ({updateResult.StatusCode})");
                    }
                }
            }
            finally
            {
                SharingAdminRoleTitles[entityName] = roleTitleConstant; // Revert to the original value.
            }
        }
    }

    private static List<IamRoleCapabilitiesModel> GetSharingAdminCapabilities(string entityName) =>
    [
        new IamRoleCapabilitiesModel
        {
            EntityType = entityName,
            AllowCreate = true,
            AllowDelete = true,
            AllowPeekAll = true,
            AllowRead = true,
            AllowUpdate = true
        },
        // Sharing admin needs peek access to users and iam-role for the sharing dialog
        new IamRoleCapabilitiesModel
        {
            EntityType = RfReservedEntities.UsersEntityName,
            AllowPeekAll = true
        },
        new IamRoleCapabilitiesModel
        {
            EntityType = RfReservedEntities.IamRoleEntityName,
            AllowPeekAll = true
        }
    ];
}
