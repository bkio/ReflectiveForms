using System.Reflection;
using FluentAssertions;
using ReflectiveForms.Core.Endpoints;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for system-managed entity immutability.
/// Root user, Owner role, and sharing admin roles are created by the framework
/// and must not be updated or deleted by any user.
/// </summary>
public class SystemManagedEntityImmutabilityTests : IDisposable
{
    // Store original values so we can restore them after each test
    private readonly int _originalOwnerRoleId;
    private readonly int _originalRootUserId;
    private readonly Dictionary<string, int> _originalSharingAdminRoleIds;

    public SystemManagedEntityImmutabilityTests()
    {
        _originalOwnerRoleId = GetStaticField<int>("_ownerRoleId");
        _originalRootUserId = GetStaticField<int>("_rootUserId");
        _originalSharingAdminRoleIds = new Dictionary<string, int>(GetStaticField<Dictionary<string, int>>("SharingAdminRoleIds"));
    }

    public void Dispose()
    {
        SetStaticField("_ownerRoleId", _originalOwnerRoleId);
        SetStaticField("_rootUserId", _originalRootUserId);

        var dict = GetStaticField<Dictionary<string, int>>("SharingAdminRoleIds");
        dict.Clear();
        foreach (var kvp in _originalSharingAdminRoleIds)
            dict[kvp.Key] = kvp.Value;
    }

    // ── RootManager.IsSystemManagedEntity Tests ──

    [Fact]
    public void IsSystemManagedEntity_RootUser_ReturnsTrue()
    {
        SetStaticField("_rootUserId", 42);

        RootManager.IsSystemManagedEntity(RfReservedEntities.UsersEntityName, 42).Should().BeTrue();
    }

    [Fact]
    public void IsSystemManagedEntity_NonRootUser_ReturnsFalse()
    {
        SetStaticField("_rootUserId", 42);

        RootManager.IsSystemManagedEntity(RfReservedEntities.UsersEntityName, 99).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_OwnerRole_ReturnsTrue()
    {
        SetStaticField("_ownerRoleId", 10);

        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 10).Should().BeTrue();
    }

    [Fact]
    public void IsSystemManagedEntity_NonOwnerRole_ReturnsFalse()
    {
        SetStaticField("_ownerRoleId", 10);

        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 99).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_SharingAdminRole_ReturnsTrue()
    {
        var dict = GetStaticField<Dictionary<string, int>>("SharingAdminRoleIds");
        dict["rf-sheets"] = 20;

        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 20).Should().BeTrue();
    }

    [Fact]
    public void IsSystemManagedEntity_MultipleSharingAdminRoles_AllReturnTrue()
    {
        var dict = GetStaticField<Dictionary<string, int>>("SharingAdminRoleIds");
        dict["rf-sheets"] = 20;
        dict["projects"] = 30;

        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 20).Should().BeTrue();
        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 30).Should().BeTrue();
    }

    [Fact]
    public void IsSystemManagedEntity_WrongEntityType_ReturnsFalse()
    {
        // Root user ID used against IAM role entity type should not match
        SetStaticField("_rootUserId", 42);

        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 42).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_OwnerRoleIdUsedForUsersEntity_ReturnsFalse()
    {
        // Owner role ID used against users entity type should not match
        SetStaticField("_ownerRoleId", 10);

        RootManager.IsSystemManagedEntity(RfReservedEntities.UsersEntityName, 10).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_NonReservedEntityType_ReturnsFalse()
    {
        SetStaticField("_rootUserId", 42);
        SetStaticField("_ownerRoleId", 10);

        RootManager.IsSystemManagedEntity("blog-post", 42).Should().BeFalse();
        RootManager.IsSystemManagedEntity("blog-post", 10).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_UninitializedIds_ReturnsFalse()
    {
        // Default IDs are -1, which should not match any valid entity ID
        SetStaticField("_rootUserId", -1);
        SetStaticField("_ownerRoleId", -1);

        RootManager.IsSystemManagedEntity(RfReservedEntities.UsersEntityName, 1).Should().BeFalse();
        RootManager.IsSystemManagedEntity(RfReservedEntities.IamRoleEntityName, 1).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_TagsEntity_NeverSystemManaged()
    {
        SetStaticField("_rootUserId", 1);
        SetStaticField("_ownerRoleId", 1);

        RootManager.IsSystemManagedEntity(RfReservedEntities.TagsEntityName, 1).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_CategoriesEntity_NeverSystemManaged()
    {
        SetStaticField("_rootUserId", 1);
        SetStaticField("_ownerRoleId", 1);

        RootManager.IsSystemManagedEntity(RfReservedEntities.CategoriesEntityName, 1).Should().BeFalse();
    }

    [Fact]
    public void IsSystemManagedEntity_MediaEntity_NeverSystemManaged()
    {
        SetStaticField("_rootUserId", 1);
        SetStaticField("_ownerRoleId", 1);

        RootManager.IsSystemManagedEntity(RfReservedEntities.MediaEntityName, 1).Should().BeFalse();
    }

    // ── RootUserId tracking Tests ──

    [Fact]
    public void RootUserId_DefaultsToNegativeOne()
    {
        SetStaticField("_rootUserId", -1);

        RootManager.RootUserId.Should().Be(-1);
    }

    [Fact]
    public void RootUserId_IsTrackedWhenSet()
    {
        SetStaticField("_rootUserId", 123);

        RootManager.RootUserId.Should().Be(123);
    }

    // ── Reflection helpers ──

    private static T GetStaticField<T>(string fieldName)
    {
        var field = typeof(RootManager).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        field.Should().NotBeNull($"Field '{fieldName}' should exist on RootManager");
        return (T)field!.GetValue(null)!;
    }

    private static void SetStaticField<T>(string fieldName, T value)
    {
        var field = typeof(RootManager).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        field.Should().NotBeNull($"Field '{fieldName}' should exist on RootManager");
        field!.SetValue(null, value);
    }
}
