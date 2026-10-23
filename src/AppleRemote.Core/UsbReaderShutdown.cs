// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;

namespace AppleRemote;

/// <summary>
/// Coordinates once-only shutdown of USB reader threads. Native handles are
/// never freed while a started reader is still running, even after a failed
/// native abort, Join exception or timeout.
/// </summary>
public sealed class UsbReaderShutdown(
    Func<bool> readersAlive,
    Action abortReaders,
    Action waitForReaders,
    Action releaseHandles)
{
    readonly object _gate = new();
    Task? _completion;

    public Task Begin()
    {
        lock (_gate)
            return _completion ??= Task.Run(() =>
            {
                while (readersAlive())
                {
                    try { abortReaders(); }
                    catch (Exception e)
                    {
                        Trace.TraceWarning($"USB abort retry: {e.GetType().Name}");
                    }

                    try { waitForReaders(); }
                    catch (Exception e)
                    {
                        Trace.TraceWarning($"USB reader wait retry: {e.GetType().Name}");
                    }

                    // A failed/expired join does not prove a reader is gone.
                    // Preserve ownership and retry without busy-spinning.
                    if (readersAlive()) Thread.Sleep(50);
                }

                // Exactly one worker reaches native release. Any exception
                // faults Completion so the caller can block adapter restart.
                releaseHandles();
            });
    }
}

