// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854UsbFailureRelayTests
{
    [Fact]
    public void Failure_during_UsbHci_constructor_is_replayed_to_late_owner()
    {
        var relay = new A2854UsbFailureRelay();
        relay.Fail("events read failed before RemoteSession subscribed");
        Assert.True(relay.HasFailed);

        var messages = new List<string>();
        relay.Subscribe(messages.Add);
        relay.Fail("ACL reader also failed");
        Assert.Equal(new[] { "events read failed before RemoteSession subscribed" }, messages);
    }

    [Fact]
    public void Failure_after_registration_is_delivered_exactly_once()
    {
        var relay = new A2854UsbFailureRelay();
        var messages = new List<string>();
        relay.Subscribe(messages.Add);
        relay.Fail("ACL read failed");
        relay.Fail("event read failed too");
        Assert.Single(messages);
        Assert.Equal("ACL read failed", messages[0]);
    }

    [Fact]
    public async Task Concurrent_reader_failure_and_subscription_cannot_lose_fault()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var relay = new A2854UsbFailureRelay();
            int delivered = 0;
            Task[] tasks =
            [
                Task.Run(() => relay.Subscribe(_ => Interlocked.Increment(ref delivered))),
                Task.Run(() => relay.Fail("reader died"))
            ];
            await Task.WhenAll(tasks);
            Assert.True(relay.HasFailed);
            Assert.Equal(1, delivered);
        }
    }

    [Fact]
    public void Duplicate_subscriber_is_refused_and_empty_failure_is_invalid()
    {
        var relay = new A2854UsbFailureRelay();
        relay.Subscribe(_ => { });
        Assert.Throws<InvalidOperationException>(() => relay.Subscribe(_ => { }));
        Assert.Throws<ArgumentException>(() => relay.Fail(" "));
    }
}
