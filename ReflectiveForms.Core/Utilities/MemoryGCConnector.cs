// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Utilities;

internal class MemoryGCConnector : IDisposable
{
    private readonly List<(object Source, WeakReference<object> Target)> _dependencies = [];

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _cleanupTask;

    // Thread-safe lazy singleton
    internal static MemoryGCConnector Instance { get; } = new();

    private MemoryGCConnector()
    {
        _cleanupTask = Task.Run(CleanupLoopAsync);
    }

    internal void Connect(object source, object target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        lock (_dependencies)
        {
            _dependencies.Add((source, new WeakReference<object>(target)));
        }
    }

    private async Task CleanupLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                CleanupDependencies();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal
        }
    }

    private void CleanupDependencies()
    {
        lock (_dependencies)
        {
            for (var i = _dependencies.Count - 1; i >= 0; i--)
            {
                if (!_dependencies[i].Target.TryGetTarget(out _))
                {
                    _dependencies.RemoveAt(i);
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cleanupTask.Wait();
        _cts.Dispose();
    }
}
