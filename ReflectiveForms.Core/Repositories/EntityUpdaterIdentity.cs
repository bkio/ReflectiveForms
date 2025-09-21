// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Repositories;

public class EntityUpdaterIdentity
{
    public bool IsDuringHookUpdate;

    public int UserId = -1;
    public string? UserEmail;

    private EntityUpdaterIdentity() { }
    public static EntityUpdaterIdentity DuringHookCallUpdate()
    {
        return new EntityUpdaterIdentity()
        {
            IsDuringHookUpdate = true
        };
    }
    public static EntityUpdaterIdentity NormalUpdate(int userId, string userEmail)
    {
        if (userId <= 0)
            return new EntityUpdaterIdentity()
            {
                IsDuringHookUpdate = true
            };
        return new EntityUpdaterIdentity()
        {
            UserId = userId,
            UserEmail = userEmail
        };
    }
}
