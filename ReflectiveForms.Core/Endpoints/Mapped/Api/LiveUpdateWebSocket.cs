// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

/// <summary>
/// WebSocket relay for live entity updates.
/// Editor connects with role=editor, viewers connect with role=viewer.
/// Editor messages are relayed to all viewers of the same entity.
/// Route: /rf/api/live/{entityName}/{entityId}?role=editor|viewer
///
/// If a second editor window opens (lock failed), it should connect as
/// role=viewer so it also receives live updates from the active editor.
///
/// Late-joining viewers automatically receive the last editor snapshot
/// so they don't see stale data until the next keystroke.
/// </summary>
internal static class LiveUpdateWebSocket
{
    /// <summary>Maximum size (bytes) for a single assembled WebSocket message from the editor.</summary>
    internal const int MaxMessageSize = 512 * 1024; // 512 KB

    // Key: "entityName:entityId" → room
    private static readonly ConcurrentDictionary<string, LiveRoom> Rooms = new();

    internal static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync("WebSocket connection expected.");
            return;
        }

        // Authenticate via cookie/JWT (same as REST endpoints)
        if (context.User.Identity is not { IsAuthenticated: true })
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Authentication required.");
            return;
        }

        var userIdStr = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdStr == null || !int.TryParse(userIdStr, out var userId))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Invalid user identity.");
            return;
        }

        // Parse route values
        var entityName = context.Request.RouteValues["entityName"]?.ToString();
        var entityIdStr = context.Request.RouteValues["entityId"]?.ToString();
        var role = context.Request.Query["role"].ToString();

        if (string.IsNullOrWhiteSpace(entityName)
            || string.IsNullOrWhiteSpace(entityIdStr)
            || !int.TryParse(entityIdStr, out var entityId))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync("Invalid entity name or id.");
            return;
        }

        if (role is not ("editor" or "viewer"))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync("Query parameter 'role' must be 'editor' or 'viewer'.");
            return;
        }

        var roomKey = $"{entityName}:{entityId}";
        var ws = await context.WebSockets.AcceptWebSocketAsync();
        var participant = new LiveParticipant(ws, userId, role == "editor");

        // GetOrAdd may return a defunct room (being cleaned up concurrently).
        // In that case, retry — the defunct room will be removed and a fresh
        // one created by the next GetOrAdd call.
        LiveRoom room;
        while (true)
        {
            room = Rooms.GetOrAdd(roomKey, _ => new LiveRoom());
            var joined = role == "editor"
                ? room.TrySetEditor(participant)
                : room.TryAddViewer(participant);
            if (joined) break;
            // Room was defunct, spin and get/create a fresh one
        }

        if (role == "editor")
        {
            try
            {
                await RelayEditorMessages(room, participant, context.RequestAborted);
            }
            catch (OperationCanceledException) { /* Normal disconnect — suppress noisy logs */ }
            finally
            {
                room.RemoveEditor(participant);
                CleanupRoomIfEmpty(roomKey, room);
            }
        }
        else
        {
            try
            {
                // Send last editor snapshot so late-joining viewers don't see stale data
                var snapshot = room.LastSnapshot;
                if (snapshot != null && ws.State == WebSocketState.Open)
                {
                    await participant.SendAsync(snapshot, CancellationToken.None);
                }

                await WaitForClose(participant, context.RequestAborted);
            }
            catch (OperationCanceledException) { /* Normal disconnect — suppress noisy logs */ }
            finally
            {
                room.RemoveViewer(participant);
                CleanupRoomIfEmpty(roomKey, room);
            }
        }
    }

    private static async Task RelayEditorMessages(LiveRoom room, LiveParticipant editor, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024]; // 64 KB receive buffer
        while (!ct.IsCancellationRequested && editor.Socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            using var ms = new MemoryStream();
            do
            {
                result = await editor.Socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                ms.Write(buffer, 0, result.Count);

                // Guard against oversized messages
                if (ms.Length > MaxMessageSize)
                {
                    await editor.Socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Message exceeds size limit",
                        CancellationToken.None);
                    return;
                }
            } while (!result.EndOfMessage);

            var messageBytes = ms.ToArray();

            // Store snapshot for late-joining viewers
            room.LastSnapshot = messageBytes;

            // Broadcast to all viewers — failures on individual viewers are
            // swallowed so one bad connection doesn't kill the relay.
            var viewers = room.GetViewers();
            foreach (var viewer in viewers)
            {
                if (viewer.Socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await viewer.SendAsync(messageBytes, CancellationToken.None);
                    }
                    catch
                    {
                        // Viewer disconnected mid-send; it will be cleaned up
                        // when its WaitForClose loop exits.
                    }
                }
            }
        }
    }

    private static async Task WaitForClose(LiveParticipant viewer, CancellationToken ct)
    {
        var buffer = new byte[256];
        while (!ct.IsCancellationRequested && viewer.Socket.State == WebSocketState.Open)
        {
            var result = await viewer.Socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return;
            // Viewers don't send meaningful messages; just drain.
        }
    }

    private static void CleanupRoomIfEmpty(string key, LiveRoom room)
    {
        // Lock the room so no concurrent AddViewer/SetEditor can sneak in
        // between the emptiness check and the dictionary removal.
        lock (room.Lock)
        {
            if (!room.IsEmptyUnsafe) return;
            room.IsDefunct = true;
            Rooms.TryRemove(new KeyValuePair<string, LiveRoom>(key, room));
        }
    }
}

internal sealed class LiveRoom
{
    internal readonly object Lock = new();
    private LiveParticipant? _editor;
    private readonly List<LiveParticipant> _viewers = [];

    /// <summary>
    /// When true, the room has been removed from the Rooms dictionary.
    /// New participants should discard this room and create/get a fresh one.
    /// </summary>
    internal bool IsDefunct { get; set; }

    /// <summary>
    /// Last message broadcast by the editor. Sent to newly-connected viewers
    /// so they immediately see up-to-date data without waiting for the next edit.
    /// </summary>
    public byte[]? LastSnapshot { get; set; }

    /// <summary>Returns false if the room is defunct (already removed). Thread-safe.</summary>
    public bool TrySetEditor(LiveParticipant p)
    {
        lock (Lock)
        {
            if (IsDefunct) return false;
            _editor = p;
            return true;
        }
    }

    public void RemoveEditor(LiveParticipant p)
    {
        lock (Lock)
        {
            if (_editor == p) _editor = null;
        }
    }

    /// <summary>Returns false if the room is defunct (already removed). Thread-safe.</summary>
    public bool TryAddViewer(LiveParticipant p)
    {
        lock (Lock)
        {
            if (IsDefunct) return false;
            _viewers.Add(p);
            return true;
        }
    }

    public void RemoveViewer(LiveParticipant p)
    {
        lock (Lock) _viewers.Remove(p);
    }

    public List<LiveParticipant> GetViewers()
    {
        lock (Lock) return [.._viewers];
    }

    /// <summary>Check emptiness without acquiring the lock. Caller must already hold <see cref="Lock"/>.</summary>
    internal bool IsEmptyUnsafe => _editor == null && _viewers.Count == 0;

    public bool IsEmpty
    {
        get { lock (Lock) return IsEmptyUnsafe; }
    }
}

internal sealed class LiveParticipant(WebSocket socket, int userId, bool isEditor)
{
    public WebSocket Socket { get; } = socket;
    public int UserId { get; } = userId;
    public bool IsEditor { get; } = isEditor;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                await Socket.SendAsync(
                    data.AsMemory(),
                    WebSocketMessageType.Text,
                    true,
                    ct);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
