using FluentAssertions;
using Newtonsoft.Json;
using ReflectiveForms.Core.Operation;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for the per-tab lock isolation model.
/// Verifies that EntityLockState correctly stores and evaluates tab_id,
/// and that the EntityLockOwnerStatus enum covers the same-user-different-tab case.
/// </summary>
public class EntityLockTabIsolationTests
{
    // ── EntityLockState serialization ────────────────────────────────

    [Fact]
    public void EntityLockState_ShouldSerializeTabId()
    {
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = "tab-abc-123"
        };

        var json = JsonConvert.SerializeObject(state);
        json.Should().Contain("\"locked_by_tab_id\":\"tab-abc-123\"");
    }

    [Fact]
    public void EntityLockState_ShouldDeserializeTabId()
    {
        var json = """
        {
            "entity_id": 1,
            "locked_by_user_id": 42,
            "locked_by_user_name": "Alice",
            "locked_by_tab_id": "tab-abc-123"
        }
        """;

        var state = JsonConvert.DeserializeObject<EntityLockState>(json)!;
        state.LockedByTabId.Should().Be("tab-abc-123");
    }

    [Fact]
    public void EntityLockState_ShouldDeserializeWithoutTabId_ForBackwardCompatibility()
    {
        // Legacy locks created before the tab_id field was added
        var json = """
        {
            "entity_id": 1,
            "locked_by_user_id": 42,
            "locked_by_user_name": "Alice"
        }
        """;

        var state = JsonConvert.DeserializeObject<EntityLockState>(json)!;
        state.LockedByTabId.Should().BeNull();
        state.LockedByUserId.Should().Be(42);
    }

    [Fact]
    public void EntityLockState_ShouldHandleNullTabId()
    {
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = null
        };

        var json = JsonConvert.SerializeObject(state);
        var deserialized = JsonConvert.DeserializeObject<EntityLockState>(json)!;
        deserialized.LockedByTabId.Should().BeNull();
    }

    // ── EntityLockOwnerStatus enum ───────────────────────────────────

    [Fact]
    public void EntityLockOwnerStatus_ShouldHaveDifferentTabVariant()
    {
        var status = EntityLockOwnerStatus.OwnedByUserDifferentTab;
        status.Should().NotBe(EntityLockOwnerStatus.OwnedByUser);
        status.Should().NotBe(EntityLockOwnerStatus.OwnedByOtherUser);
        status.Should().NotBe(EntityLockOwnerStatus.NotLocked);
    }

    // ── Tab isolation logic (pure logic, no I/O) ─────────────────────

    [Fact]
    public void SameUserSameTab_ShouldBeOwnedByUser()
    {
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = "tab-1"
        };

        var incomingUserId = 42;
        var incomingTabId = "tab-1";

        var result = EvaluateOwnerStatus(state, incomingUserId, incomingTabId);
        result.Should().Be(EntityLockOwnerStatus.OwnedByUser);
    }

    [Fact]
    public void SameUserDifferentTab_ShouldBeOwnedByUserDifferentTab()
    {
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = "tab-1"
        };

        var incomingUserId = 42;
        var incomingTabId = "tab-2";

        var result = EvaluateOwnerStatus(state, incomingUserId, incomingTabId);
        result.Should().Be(EntityLockOwnerStatus.OwnedByUserDifferentTab);
    }

    [Fact]
    public void DifferentUser_ShouldBeOwnedByOtherUser()
    {
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = "tab-1"
        };

        var incomingUserId = 99;
        var incomingTabId = "tab-3";

        var result = EvaluateOwnerStatus(state, incomingUserId, incomingTabId);
        result.Should().Be(EntityLockOwnerStatus.OwnedByOtherUser);
    }

    [Fact]
    public void SameUserNullIncomingTab_ShouldFallBackToOwnedByUser()
    {
        // When tab_id is not provided (legacy client or fallback), treat as
        // same-tab to avoid breaking backward compatibility.
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = "tab-1"
        };

        var incomingUserId = 42;
        string? incomingTabId = null;

        var result = EvaluateOwnerStatus(state, incomingUserId, incomingTabId);
        result.Should().Be(EntityLockOwnerStatus.OwnedByUser);
    }

    [Fact]
    public void SameUserNullStoredTab_ShouldFallBackToOwnedByUser()
    {
        // Legacy lock without tab_id stored — any same-user request should
        // be treated as the owner to avoid deadlocks.
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = null
        };

        var incomingUserId = 42;
        var incomingTabId = "tab-new";

        var result = EvaluateOwnerStatus(state, incomingUserId, incomingTabId);
        result.Should().Be(EntityLockOwnerStatus.OwnedByUser);
    }

    [Fact]
    public void SameUserBothTabsNull_ShouldBeOwnedByUser()
    {
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = null
        };

        var incomingUserId = 42;
        string? incomingTabId = null;

        var result = EvaluateOwnerStatus(state, incomingUserId, incomingTabId);
        result.Should().Be(EntityLockOwnerStatus.OwnedByUser);
    }

    [Fact]
    public void PageRefresh_SameTab_ShouldReacquireLock()
    {
        // sessionStorage persists across F5 refresh within the same tab,
        // so the tab_id is the same → lock should be re-acquirable.
        var state = new EntityLockState
        {
            EntityId = 1,
            LockedByUserId = 42,
            LockedByUserName = "Alice",
            LockedByTabId = "persistent-tab-id"
        };

        var result = EvaluateOwnerStatus(state, 42, "persistent-tab-id");
        result.Should().Be(EntityLockOwnerStatus.OwnedByUser);
    }

    // ── Helper: mirrors the logic in EntityLockController.CheckIfLockIsLockedByUserIdUnsafeAsync ──

    private static EntityLockOwnerStatus EvaluateOwnerStatus(
        EntityLockState state, int incomingUserId, string? incomingTabId)
    {
        if (state.LockedByUserId != incomingUserId)
            return EntityLockOwnerStatus.OwnedByOtherUser;

        // Same user — check tab_id
        if (!string.IsNullOrEmpty(incomingTabId)
            && !string.IsNullOrEmpty(state.LockedByTabId)
            && !string.Equals(incomingTabId, state.LockedByTabId, StringComparison.Ordinal))
        {
            return EntityLockOwnerStatus.OwnedByUserDifferentTab;
        }

        return EntityLockOwnerStatus.OwnedByUser;
    }
}
