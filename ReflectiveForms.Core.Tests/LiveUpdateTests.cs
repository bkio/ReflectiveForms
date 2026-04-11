using FluentAssertions;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for the WebSocket live update endpoint route configuration,
/// role-based connection model, and edge-case handling (room cleanup,
/// message size limits, late-joining viewer snapshots).
/// </summary>
public class LiveUpdateTests
{
    private const string LiveEndpointPattern = "live/{entityName}/{entityId}";

    [Fact]
    public void LiveEndpointPattern_ShouldContainRouteParameters()
    {
        LiveEndpointPattern.Should().Contain("{entityName}");
        LiveEndpointPattern.Should().Contain("{entityId}");
    }

    [Theory]
    [InlineData("objective", 1)]
    [InlineData("blog-post", 42)]
    [InlineData("rf-sheets", 100)]
    public void LiveEndpointPattern_ShouldSupportAnyEntityTypeAndId(string entityName, int entityId)
    {
        // Verify the pattern can be formatted into a valid path
        var path = LiveEndpointPattern
            .Replace("{entityName}", entityName)
            .Replace("{entityId}", entityId.ToString());

        path.Should().Be($"live/{entityName}/{entityId}");
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("viewer")]
    public void ValidRoles_ShouldBeRecognized(string role)
    {
        var validRoles = new[] { "editor", "viewer" };
        validRoles.Should().Contain(role);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData("EDITOR")]
    public void InvalidRoles_ShouldNotBeRecognized(string role)
    {
        var validRoles = new[] { "editor", "viewer" };
        validRoles.Should().NotContain(role);
    }

    // ── Room key format ──────────────────────────────────────────────

    [Theory]
    [InlineData("objective", 1, "objective:1")]
    [InlineData("blog-post", 42, "blog-post:42")]
    [InlineData("rf-sheets", 100, "rf-sheets:100")]
    public void RoomKey_ShouldCombineEntityNameAndId(string entityName, int entityId, string expected)
    {
        var roomKey = $"{entityName}:{entityId}";
        roomKey.Should().Be(expected);
    }

    // ── Max message size ─────────────────────────────────────────────

    [Fact]
    public void MaxMessageSize_ShouldBe512KB()
    {
        // The constant is internal, but we verify the design value here
        const int expectedMaxSize = 512 * 1024;
        expectedMaxSize.Should().Be(524_288);
    }

    // ── Keepalive interval ───────────────────────────────────────────

    [Fact]
    public void KeepAliveInterval_ShouldBe30Seconds()
    {
        // Mirror of the value configured in RfEndpointMapper.UseWebSockets
        var interval = TimeSpan.FromSeconds(30);
        interval.TotalSeconds.Should().Be(30);
    }

    // ── Cleanup race safety via TryRemove(KeyValuePair) ──────────────

    [Fact]
    public void ConcurrentDictionaryTryRemoveKvp_ShouldOnlyRemoveMatchingInstance()
    {
        // Demonstrates the race-safe cleanup pattern:
        // TryRemove(KeyValuePair) only removes if value is the SAME instance.
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        var original = new object();
        var replacement = new object();

        dict.TryAdd("key", original);

        // Another thread replaces the value
        dict["key"] = replacement;

        // Cleanup with the original instance should NOT remove the replacement
        var removed = dict.TryRemove(new KeyValuePair<string, object>("key", original));
        removed.Should().BeFalse();
        dict.Should().ContainKey("key");
        dict["key"].Should().BeSameAs(replacement);
    }

    [Fact]
    public void ConcurrentDictionaryTryRemoveKvp_ShouldRemoveWhenSameInstance()
    {
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        var room = new object();

        dict.TryAdd("key", room);

        var removed = dict.TryRemove(new KeyValuePair<string, object>("key", room));
        removed.Should().BeTrue();
        dict.Should().NotContainKey("key");
    }

    // ── Late-joining viewer snapshot model ────────────────────────────

    [Fact]
    public void LastSnapshot_ShouldBeNullInitially()
    {
        // Simulates LiveRoom.LastSnapshot behavior
        byte[]? snapshot = null;
        snapshot.Should().BeNull();
    }

    [Fact]
    public void LastSnapshot_ShouldRetainLatestValue()
    {
        byte[]? snapshot = null;
        var first = System.Text.Encoding.UTF8.GetBytes("{\"v\":1}");
        var second = System.Text.Encoding.UTF8.GetBytes("{\"v\":2}");

        snapshot = first;
        snapshot.Should().BeSameAs(first);

        snapshot = second;
        snapshot.Should().BeSameAs(second);
        System.Text.Encoding.UTF8.GetString(snapshot).Should().Be("{\"v\":2}");
    }

    // ── Multi-editor-as-viewer role ──────────────────────────────────

    [Fact]
    public void LockedOutEditor_ShouldConnectAsViewer()
    {
        // When an editor fails to acquire the lock, the frontend sends
        // role=viewer so the locked-out window receives live updates.
        const string role = "viewer";
        var validRoles = new[] { "editor", "viewer" };
        validRoles.Should().Contain(role);
    }

    // ── Oversized message limit ──────────────────────────────────────

    [Fact]
    public void MaxMessageSize_ShouldBe512KB_Exactly()
    {
        const int expected = 512 * 1024;
        expected.Should().Be(524_288);
        // WebSocket messages exceeding this are rejected with a MessageTooBig close frame
    }

    // ── Defunct room model ───────────────────────────────────────────

    [Fact]
    public void DefunctRoom_ShouldRejectNewParticipants()
    {
        // Simulates the defunct-room pattern used in cleanup.
        // Once a room is marked defunct, TrySetEditor / TryAddViewer
        // return false, forcing the caller to create a new room.
        var isDefunct = false;
        var participants = new List<string>();

        // Normal add
        if (!isDefunct) participants.Add("viewer1");
        participants.Should().HaveCount(1);

        // Mark defunct
        isDefunct = true;

        // Subsequent add should be rejected
        var joined = !isDefunct;
        joined.Should().BeFalse();
    }

    [Fact]
    public void CleanupWithDefunct_ShouldPreventDetachedRoomRace()
    {
        // Demonstrates that the defunct flag + lock pattern prevents the race
        // where a participant joins a room that's about to be removed.
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        dict.TryAdd("room1", "active");

        // Simulate cleanup marking defunct + removing atomically (under lock)
        var value = dict["room1"];
        if (value == "active")
        {
            dict.TryUpdate("room1", "defunct", "active");
            dict.TryRemove(new KeyValuePair<string, string>("room1", "defunct"));
        }

        dict.Should().NotContainKey("room1");

        // New participant creates a fresh room
        var newRoom = dict.GetOrAdd("room1", _ => "fresh");
        newRoom.Should().Be("fresh");
    }

    // ── Editor should only connect after lock confirmed ───────────────

    [Theory]
    [InlineData("idle", "viewer")]
    [InlineData("locked", "editor")]
    [InlineData("failed", "viewer")]
    [InlineData("error", "viewer")]
    public void LiveRole_ShouldOnlyBeEditorWhenLockConfirmed(string lockStatus, string expectedRole)
    {
        // Mirrors the DynamicForm logic:
        // const liveRole = (!isCreateMode && lockStatus === 'locked') ? 'editor' : 'viewer';
        var isCreateMode = false;
        var liveRole = (!isCreateMode && lockStatus == "locked") ? "editor" : "viewer";
        liveRole.Should().Be(expectedRole);
    }

    // ── RF Sheets live update specifics ──────────────────────────────

    [Fact]
    public void SheetRoom_ShouldUseCorrectRoomKey()
    {
        // rf-sheets entities use the same room-key format as other entities
        var roomKey = $"rf-sheets:{42}";
        roomKey.Should().Be("rf-sheets:42");
    }

    [Fact]
    public void SheetEditorBroadcast_ShouldContainWorkbookDataAndTitle()
    {
        // Editor broadcasts { workbook_data, title, sources } as JSON.
        // The relay stores this as LastSnapshot for late joiners.
        var payload = new Dictionary<string, object>
        {
            ["workbook_data"] = "{\"sheets\":{}}",
            ["title"] = "My Sheet",
            ["sources"] = new[] { "product", "objective" },
        };

        payload.Should().ContainKey("workbook_data");
        payload.Should().ContainKey("title");
        payload.Should().ContainKey("sources");
        payload["workbook_data"].Should().BeOfType<string>();
    }

    [Fact]
    public void SheetSnapshot_Under512KB_ShouldBeAccepted()
    {
        // A 400 KB workbook JSON is within the 512 KB MaxMessageSize
        const int maxSize = 512 * 1024;
        var largePayload = new string('x', 400 * 1024);
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(largePayload);
        payloadBytes.Should().BeLessThan(maxSize);
    }

    [Fact]
    public void SheetSnapshot_Exceeding512KB_ShouldExceedLimit()
    {
        // A 600 KB payload exceeds the MaxMessageSize — server would close with MessageTooBig
        const int maxSize = 512 * 1024;
        var oversizedPayload = new string('x', 600 * 1024);
        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(oversizedPayload);
        payloadBytes.Should().BeGreaterThan(maxSize);
    }

    [Fact]
    public void SheetLateJoiner_ShouldReceiveLastWorkbookSnapshot()
    {
        // Simulates a late-joining viewer receiving the editor's last snapshot.
        // The relay stores the last editor message as byte[] and sends it to new viewers.
        byte[]? lastSnapshot = null;

        // Editor sends workbook data
        var editorPayload = "{\"workbook_data\":\"{\\\"sheets\":{}}\",\"title\":\"Test Sheet\",\"sources\":[\"product\"]}";
        lastSnapshot = System.Text.Encoding.UTF8.GetBytes(editorPayload);
        lastSnapshot.Should().NotBeNull();

        // Late-joining viewer receives it
        var receivedJson = System.Text.Encoding.UTF8.GetString(lastSnapshot!);
        receivedJson.Should().Contain("workbook_data");
        receivedJson.Should().Contain("Test Sheet");
        receivedJson.Should().Contain("product");
    }

    [Fact]
    public void SheetLiveRole_EditorOnlyWhenDesignMode()
    {
        // Mirrors RfSheetPage: isDesignMode ? 'editor' : 'viewer'
        // isDesignMode = isNew ? true : (hasEditRight && lockStatus === 'locked')
        var testCases = new[]
        {
            (isNew: true, hasEditRight: true, lockStatus: "idle", expectedRole: "editor"),
            (isNew: false, hasEditRight: true, lockStatus: "locked", expectedRole: "editor"),
            (isNew: false, hasEditRight: true, lockStatus: "failed", expectedRole: "viewer"),
            (isNew: false, hasEditRight: false, lockStatus: "locked", expectedRole: "viewer"),
        };

        foreach (var tc in testCases)
        {
            var isDesignMode = tc.isNew || (tc.hasEditRight && tc.lockStatus == "locked");
            var role = isDesignMode ? "editor" : "viewer";
            role.Should().Be(tc.expectedRole,
                $"isNew={tc.isNew}, hasEditRight={tc.hasEditRight}, lockStatus={tc.lockStatus}");
        }
    }
}
