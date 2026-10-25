// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

/// <summary>
/// Exercises a physical KeyBinder and the exact shortcut-key ledger shape
/// used by Windows KeyboardInjector, sharing one acknowledged OS sink.
/// No Windows SendInput or BLE operation is performed.
/// </summary>
public sealed class SharedKeyOwnershipSinkTests
{
    sealed class FakeWindows : IConfirmedKeySink
    {
        public readonly List<(KeyCode Key, bool Down)> Log = [];
        public readonly Dictionary<KeyCode, int> RefuseUps = new();
        public readonly Dictionary<KeyCode, int> RefuseDowns = new();
        public void Key(KeyCode key, bool down) => _ = TryKey(key, down);
        public bool TryKey(KeyCode key, bool down)
        {
            var failures = down ? RefuseDowns : RefuseUps;
            if (failures.TryGetValue(key, out var n) && n > 0)
            {
                failures[key] = n - 1;
                return false;
            }
            Log.Add((key, down));
            return true;
        }
    }

    static readonly KeyCode C = Keys.Names["C"];
    static readonly KeyCode Tab = Keys.Names["Tab"];
    static readonly KeyCode Ctrl = new(0x11);
    static readonly KeyCode Alt = new(0x12);
    static ButtonsEvent Button(string[] pressed, string[] released) =>
        new(0, pressed, released, Array.Empty<byte>());

    static (KeyBinder Binder, RetryableShortcutSink Shortcuts, FakeWindows Windows)
        Setup(string physicalKey)
    {
        var win = new FakeWindows();
        var shared = new SharedKeyOwnershipSink(win);
        var binder = new KeyBinder(KeyMap.FromJson(
            "{\"click\":\"" + physicalKey + "\"}"), shared.Physical);
        var shortcuts = new RetryableShortcutSink(shared.Shortcuts);
        return (binder, shortcuts, win);
    }

    [Fact]
    public void Physical_C_remains_down_after_Ctrl_C_finishes()
    {
        var (binder, shortcut, win) = Setup("C");
        binder.OnEvent(Button(["click"], [])); // physical C down
        shortcut.Key(Ctrl, true);
        shortcut.Key(C, true);                 // second owner, no OS C-down
        shortcut.Key(C, false);                // must NOT lift physical C
        shortcut.Key(Ctrl, false);
        Assert.Equal(new[] {
            (C, true), (Ctrl, true), (Ctrl, false)
        }, win.Log);
        binder.OnEvent(Button([], ["click"]));
        Assert.Equal((C, false), win.Log[^1]);
        Assert.Equal(1, win.Log.Count(e => e == (C, true)));
        Assert.Equal(1, win.Log.Count(e => e == (C, false)));
    }

    [Fact]
    public void Physical_Tab_remains_down_after_Alt_Tab_finishes()
    {
        var (binder, shortcut, win) = Setup("Tab");
        binder.OnEvent(Button(["click"], []));
        shortcut.Key(Alt, true);
        shortcut.Key(Tab, true);
        shortcut.Key(Tab, false);
        shortcut.Key(Alt, false);
        Assert.DoesNotContain((Tab, false), win.Log);
        binder.OnEvent(Button([], ["click"]));
        Assert.Equal(new[] {
            (Tab, true), (Alt, true), (Alt, false), (Tab, false)
        }, win.Log);
    }

    [Fact]
    public void Physical_release_during_shortcut_keeps_key_until_shortcut_finishes()
    {
        var (binder, shortcut, win) = Setup("C");
        binder.OnEvent(Button(["click"], []));
        shortcut.Key(Ctrl, true);
        shortcut.Key(C, true);
        binder.OnEvent(Button([], ["click"]));
        Assert.DoesNotContain((C, false), win.Log);
        shortcut.Key(C, false);
        shortcut.Key(Ctrl, false);
        Assert.Equal(new[] {
            (C, true), (Ctrl, true), (C, false), (Ctrl, false)
        }, win.Log);
    }

    [Fact]
    public void Shared_final_shortcut_up_rejection_is_retained_for_repeat_Stop()
    {
        var (binder, shortcut, win) = Setup("C");
        win.RefuseUps[C] = 3;
        binder.OnEvent(Button(["click"], []));
        shortcut.Key(Ctrl, true);
        shortcut.Key(C, true);
        binder.OnEvent(Button([], ["click"]));
        shortcut.Key(C, false); // refused as last owner
        shortcut.Key(Ctrl, false);
        Assert.True(shortcut.HasPendingReleases);
        Assert.False(shortcut.ReleaseAll()); // Stop #1
        Assert.False(shortcut.ReleaseAll()); // stopped-session Stop #2
        Assert.True(shortcut.ReleaseAll());  // Stop #3
        Assert.True(binder.ReleaseAll());
        Assert.Equal(1, win.Log.Count(e => e == (C, true)));
        Assert.Equal(1, win.Log.Count(e => e == (C, false)));
        for (int i = 0; i < 5; i++) Assert.True(shortcut.ReleaseAll());
        Assert.Equal(1, win.Log.Count(e => e == (C, false)));
    }

    [Fact]
    public void Physical_last_up_refusal_is_retryable_without_lifting_shortcut()
    {
        var (binder, shortcut, win) = Setup("C");
        win.RefuseUps[C] = 1;
        shortcut.Key(C, true);
        binder.OnEvent(Button(["click"], [])); // shares existing down
        shortcut.Key(C, false); // cannot send OS up yet
        Assert.DoesNotContain((C, false), win.Log);
        binder.OnEvent(Button([], ["click"])); // reject OS-up
        Assert.True(binder.HasPendingReleases);
        Assert.True(binder.ReleaseAll());
        Assert.Equal(new[] { (C, true), (C, false) }, win.Log);
    }

    [Fact]
    public void Rejected_OS_keydown_is_not_claimed_by_either_producer()
    {
        var (binder, shortcut, win) = Setup("C");
        win.RefuseDowns[C] = 1;
        binder.OnEvent(Button(["click"], [])); // rejected, no physical ownership
        shortcut.Key(C, true);                 // sends actual down
        binder.OnEvent(Button([], ["click"])); // no phantom key-up
        shortcut.Key(C, false);
        Assert.Equal(new[] { (C, true), (C, false) }, win.Log);
    }

    [Fact]
    public void Interrupted_shortcut_and_physical_Stop_each_release_only_their_keys()
    {
        var (binder, shortcut, win) = Setup("C");
        binder.OnEvent(Button(["click"], []));
        shortcut.Key(Ctrl, true);
        shortcut.Key(C, true);
        Assert.True(binder.ReleaseAll()); // Ctrl+C still owns C
        Assert.DoesNotContain((C, false), win.Log);
        Assert.True(shortcut.ReleaseAll());
        Assert.Equal(new[] {
            (C, true), (Ctrl, true), (C, false), (Ctrl, false)
        }, win.Log);
        Assert.True(shortcut.ReleaseAll());
        Assert.True(binder.ReleaseAll());
        Assert.Equal(4, win.Log.Count);
    }

    [Fact]
    public void Two_distinct_producers_maintain_original_VK_after_map_change()
    {
        var (binder, shortcut, win) = Setup("C");
        binder.OnEvent(Button(["click"], []));
        shortcut.Key(C, true);
        binder.Map = KeyMap.FromJson("""{"click":"Tab"}""");
        Assert.DoesNotContain((C, false), win.Log);
        shortcut.Key(C, false);
        Assert.Contains((C, false), win.Log);
        binder.OnEvent(Button(["click"], []));
        binder.OnEvent(Button([], ["click"]));
        Assert.Equal(1, win.Log.Count(e => e == (C, false)));
        Assert.Contains((Tab, true), win.Log);
        Assert.Contains((Tab, false), win.Log);
    }
}
