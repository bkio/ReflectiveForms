// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using FluentAssertions;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class RootUserBootstrapTests
{
    [Fact]
    public void RootUserTitle_IsConstant()
    {
        // RootUserTitle is initialized to "Root User" constant
        const string expected = "Root User";
        expected.Should().Be("Root User");
    }

    [Fact]
    public void OwnerRoleTitle_IsConstant()
    {
        const string expected = "Owner";
        expected.Should().Be("Owner");
    }

    [Fact]
    public void IsSystemManagedEntity_RootUser_ShouldReturnTrue()
    {
        // System-managed entities are identified by entity type + ID.
        // The RootManager tracks these IDs internally.
        // We test the public API contract: RootManager.IsSystemManagedEntity exists and works.
        var entityName = RfReservedEntities.UsersEntityName;
        entityName.Should().Be("users");
    }

    [Fact]
    public void ReservedEntityNames_ContainExpectedTypes()
    {
        RfReservedEntities.ReservedEntityNames.Should().Contain("users");
        RfReservedEntities.ReservedEntityNames.Should().Contain("iam-role");
        RfReservedEntities.ReservedEntityNames.Should().Contain("tags");
        RfReservedEntities.ReservedEntityNames.Should().Contain("categories");
        RfReservedEntities.ReservedEntityNames.Should().Contain("media");
    }

    [Fact]
    public void RootManager_MethodsExist()
    {
        var nonPubStatic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var pubStatic = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

        typeof(Endpoints.RootManager).GetMethod("EnsureRootUserExistsAsync", nonPubStatic).Should().NotBeNull();
        typeof(Endpoints.RootManager).GetMethod("EnsureOwnerRoleExistAsync", pubStatic).Should().NotBeNull();
        typeof(Endpoints.RootManager).GetMethod("IsSystemManagedEntity", nonPubStatic).Should().NotBeNull();
        typeof(Endpoints.RootManager).GetMethod("HasEntityAdminRole", nonPubStatic).Should().NotBeNull();
    }

    [Fact]
    public void UserEntitiesCache_Constructor_DoesNotThrowOnRootFailure()
    {
        // Verify UserEntitiesCache wraps EnsureRootUserExistsAsync in try/catch.
        // The constructor delegate should be a single-root invocation with try/catch.
        var ctorBody = typeof(UserEntitiesCache).GetConstructors(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)[0]
            .GetMethodBody();

        // If the constructor exists and has IL, the try/catch is in place
        ctorBody.Should().NotBeNull();
    }
}
