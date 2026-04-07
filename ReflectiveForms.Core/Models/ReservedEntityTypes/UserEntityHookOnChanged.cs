// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

internal static class UserEntityHookOnChanged
{
    private static async Task OnUserUpsert(int id, EntityModel<UserEntityFieldsModel> newEntity, bool isCreate, CancellationToken cancellationToken)
    {
        var isUpdateNeeded = false;

        var fields = newEntity.Fields;

        if (fields.GeneratePassword)
        {
            fields.PasswordSha256 = CryptographyUtilities.CalculateStringSha256(StringUtilities.GenerateRandomString(32));

            isUpdateNeeded = true;

            fields.GeneratePassword = false;
            fields.OptionalCustomPassword = "";
        }
        else if (!string.IsNullOrEmpty(fields.OptionalCustomPassword))
        {
            fields.PasswordSha256 = CryptographyUtilities.CalculateStringSha256(fields.OptionalCustomPassword);

            isUpdateNeeded = true;

            fields.OptionalCustomPassword = "";
        }

        var loweredEmail = fields.EmailAddress.ToLower();
        if (loweredEmail != fields.EmailAddress)
        {
            isUpdateNeeded = true;

            fields.EmailAddress = loweredEmail;
        }

        // Only fix author references across entity types on UPDATE —
        // a brand-new user cannot be referenced by any existing entity.
        if (!isCreate)
        {
            var relevantEntityTypes
                = RfConfiguration.EntityNameToConfiguration.Keys.Where(eName => eName is not
                    (RfReservedEntities.CategoriesEntityName
                    or RfReservedEntities.TagsEntityName
                    or RfReservedEntities.UsersEntityName)).ToList();

            var fixRelevantTypesResult = await RfConfiguration.RepositoryService.FixTheUpdateForRelevantPostTypesAsync(
                relevantEntityTypes,
                $"{EntityModelAttributes.Author}_{EntityModelAttributes.Id}",
                false,
                id,
                EntityModelAttributes.Author,
                newEntity.Title.Text,
                cancellationToken);
            if (!fixRelevantTypesResult.IsSuccessful)
            {
                RfConfiguration.LogError(new Exception($"HookOnUserEntityChanged: FixTheUpdateForRelevantPostTypesAsync failed: {fixRelevantTypesResult.ErrorMessage}"));
                return;
            }
        }

        if (isUpdateNeeded)
        {
            var updateResult = await RfConfiguration.RepositoryService.UpdateOneAsync<UserEntityFieldsModel>(
                RfReservedEntities.UsersEntityName,
                id,
                newEntity.FromObjectWithPolymorphism(),
                EntityUpdaterIdentity.DuringHookCallUpdate(),
                cancellationToken);
            if (!updateResult.IsSuccessful)
            {
                RfConfiguration.LogError(new Exception($"HookOnUserEntityChanged: UpdateOneAsync failed: {updateResult.ErrorMessage}"));
            }
        }
    }

    internal static async Task OnUserDeleted(PostDeleteHookModel<UserEntityFieldsModel> hookModel, CancellationToken cancellationToken)
    {
        var relevantEntityTypes
            = RfConfiguration.EntityNameToConfiguration.Keys.Where(eName => eName is not
                (RfReservedEntities.CategoriesEntityName
                or RfReservedEntities.TagsEntityName
                or RfReservedEntities.UsersEntityName)).ToList();

        var fixRelevantTypesResult = await RfConfiguration.RepositoryService.FixTheDeleteForRelevantPostTypesAsync(
            relevantEntityTypes,
            $"{EntityModelAttributes.Author}_{EntityModelAttributes.Id}",
            EntityModelAttributes.Author,
            false,
            hookModel.Id,
            EntityModelAttributes.Author,
            cancellationToken);
        if (!fixRelevantTypesResult.IsSuccessful)
        {
            RfConfiguration.LogError(new Exception($"HookOnUserEntityChanged: FixTheDeleteForRelevantPostTypesAsync failed: {fixRelevantTypesResult.ErrorMessage}"));
        }
    }

    internal static async Task OnUserUpdated(PostUpdateHookModel<UserEntityFieldsModel> hookModel, CancellationToken cancellationToken) => await OnUserUpsert(hookModel.Id, hookModel.NewFinalBody, false, cancellationToken);
    internal static async Task OnUserCreated(PostCreateHookModel<UserEntityFieldsModel> hookModel, CancellationToken cancellationToken) => await OnUserUpsert(hookModel.NewId, hookModel.FinalBody, true, cancellationToken);
}
