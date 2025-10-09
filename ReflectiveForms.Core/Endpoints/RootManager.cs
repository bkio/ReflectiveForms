// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
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
                    throw new Exception($"Failed to create owner role with title {OwnerRoleTitleConstant}.");

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

                    var updateResult = await RfConfiguration.RepositoryService.UpdateOneAsync<UserEntityFieldsModel>(
                        RfReservedEntities.IamRoleEntityName,
                        ownerRole.Id,
                        ownerRole.FromObjectWithPolymorphism(),
                        updaterIdentity,
                        cancellationToken);
                    if (!updateResult.IsSuccessful)
                        throw new Exception($"Failed to update owner role with id {ownerRole.Id} with the new capabilities.");
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
                if (!putResult.IsSuccessful)
                    throw new Exception($"Failed to create root user with email {newEmail}.");
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
}
