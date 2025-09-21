// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

// ReSharper disable MemberCanBePrivate.Global

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

public record EntityTypeToCrudOperation(string EntityType, string CrudOperation);

public static class UserEntityExtensions
{
    public static bool CanUserDo(
        this UserEntityFieldsModel entityFields,
        string crudOperation,
        string entityType)
    {
        return entityFields.CanUserDo(crudOperation, [new EntityTypeToCrudOperation(entityType, crudOperation)]);
    }

    public static bool CanUserDo(
        this UserEntityFieldsModel entityFields,
        string crudOperation,
        IReadOnlyList<EntityTypeToCrudOperation> list)
    {
        return list.All(op => CanListOfRolesDo(op.CrudOperation, op.EntityType, entityFields.Roles));
    }

    public static bool CanListOfRolesDo(
        string crudOperation,
        string entityType,
        IReadOnlyList<UserRoleAssignmentModel> userRoles)
    {
        foreach (var role in userRoles)
        {
            if (RfConfiguration.IamRoleEntitiesCache.FindEntityByFilterAndGetCopy(e =>
                    e.Id == role.RoleId && e.Fields.CanDo(entityType, crudOperation)) != null)
                return true;
        }
        return false;
    }
}
