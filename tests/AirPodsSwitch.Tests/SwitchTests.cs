// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Immutable;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;
using AirPodsSwitch.Core.Switching;

namespace AirPodsSwitch.Tests;

public sealed class SwitchTests
{
    private static readonly BluetoothAddress Local = BluetoothAddress.Parse("11:22:33:44:55:66");
    private static readonly BluetoothAddress Other = BluetoothAddress.Parse("AA:BB:CC:DD:EE:FF");
    public static SwitchInputs Input(long at = 100, AudioSourceType type = AudioSourceType.Media, bool worn = true)
    {
        var store = new AirPodsStateStore();
        long epoch = store.BeginConnection();
        store.Apply(new IdentityReceived(new("A3531", "TEST")), at, epoch);
        store.Apply(new WearReceived(new(worn ? BudWear.InEar : BudWear.OutOfEar, BudWear.OutOfEar)), at, epoch);
        store.Apply(new SourceReceived(new(Other, type)), at, epoch);
        store.Apply(new DevicesReceived(ImmutableArray.Create(new ConnectedDevice(Local, 0, 0), new ConnectedDevice(Other, 0, 0))), at, epoch);
        return new(store.Current, Local, new("session-1", true, false, at), new(false, at));
    }
    [Fact]
    public void OtherMediaAndWornOffersOnce()
    {
        var policy = new SwitchCoordinator();
        Assert.Equal(SwitchAction.Offer, policy.Update(Input(), 100).Action);
        Assert.Equal(SwitchAction.None, policy.Update(Input(), 100).Action);
    }
    [Fact]
    public void CallNeverPrompts() => Assert.Equal(SwitchAction.None, new SwitchCoordinator().Update(Input(type: AudioSourceType.Call), 100).Action);
    [Fact]
    public void NotWornNeverPrompts() => Assert.Equal(SwitchAction.None, new SwitchCoordinator().Update(Input(worn: false), 100).Action);
    [Fact]
    public void RejectSuppressesSameSessionAfterCooldown()
    {
        var policy = new SwitchCoordinator();
        var offer = policy.Update(Input(), 100);
        policy.Reject(offer.RequestId!.Value, 101);
        Assert.Equal(SwitchAction.None, policy.Update(Input(100000), 100000).Action);
    }
    [Fact]
    public void CallAfterNotificationInvalidatesAccept()
    {
        var policy = new SwitchCoordinator();
        var offer = policy.Update(Input(), 100);
        Assert.Equal(SwitchAction.Cancel, policy.Accept(offer.RequestId!.Value, Input(101, AudioSourceType.Call), 101).Action);
    }
    [Fact]
    public void NoFeedbackTimesOutAndNeverLoops()
    {
        var policy = new SwitchCoordinator { TimeoutMs = 50 };
        var input = Input();
        var offer = policy.Update(input, 100);
        Assert.Equal(SwitchAction.SendTakeover, policy.Accept(offer.RequestId!.Value, input, 100).Action);
        Assert.Equal(SwitchAction.Failed, policy.Update(input, 151).Action);
        Assert.Equal(SwitchAction.None, policy.Update(Input(100000), 100000).Action);
    }
    [Fact]
    public void WriteOrOwnershipOrDefaultRouteCannotConfirm()
    {
        var policy = new SwitchCoordinator();
        var input = Input();
        var offer = policy.Update(input, 100);
        policy.Accept(offer.RequestId!.Value, input, 100);
        var optimistic = input with
        {
            AirPods = input.AirPods with { OwnsConnection = new(true, 101, input.AirPods.Epoch) },
            Render = new(true, 101)
        };
        Assert.Equal(SwitchAction.None, policy.Update(optimistic, 101).Action);
    }
    [Fact]
    public void RequiresFreshPostRequestSourceAndCorrelatedRender()
    {
        var policy = new SwitchCoordinator();
        var input = Input();
        var offer = policy.Update(input, 100);
        policy.Accept(offer.RequestId!.Value, input, 100);
        var local = input with { AirPods = input.AirPods with { Source = new(new(Local, AudioSourceType.Media), 101, input.AirPods.Epoch) } };
        Assert.Equal(SwitchAction.None, policy.Update(local, 101).Action);
        Assert.Equal(SwitchAction.Confirmed, policy.Update(local with { Render = new(true, 102) }, 102).Action);
    }
    [Fact]
    public void ConflictingUpdateFailsAfterLocalSource()
    {
        var policy = new SwitchCoordinator();
        var input = Input();
        var offer = policy.Update(input, 100);
        policy.Accept(offer.RequestId!.Value, input, 100);
        policy.Update(input with { AirPods = input.AirPods with { Source = new(new(Local, AudioSourceType.Media), 101, input.AirPods.Epoch) } }, 101);
        Assert.Equal(SwitchAction.Failed, policy.Update(Input(102), 102).Action);
    }
    [Fact]
    public void DuplicateAcceptCannotSendTwice()
    {
        var policy = new SwitchCoordinator();
        var input = Input();
        var id = policy.Update(input, 100).RequestId!.Value;
        Assert.Equal(SwitchAction.SendTakeover, policy.Accept(id, input, 100).Action);
        Assert.Equal(SwitchAction.None, policy.Accept(id, input, 101).Action);
    }
    [Fact]
    public void SystemSoundsAndShortMediaDoNotOffer()
    {
        var input = Input();
        Assert.Equal(SwitchAction.None, new SwitchCoordinator().Update(input with { Media = input.Media with { Meaningful = false } }, 100).Action);
    }
    [Fact]
    public void StaleOrMissingSourceNeverOffers()
    {
        var input = Input(6001);
        Assert.Equal(SwitchAction.None, new SwitchCoordinator().Update(input with { AirPods = Input().AirPods }, 6001).Action);
        Assert.Equal(SwitchAction.None, new SwitchCoordinator().Update(input with { AirPods = input.AirPods with { Source = null } }, 6001).Action);
    }
    [Fact]
    public void StoppedMediaCancelsOffer()
    {
        var policy = new SwitchCoordinator();
        var input = Input();
        policy.Update(input, 100);
        Assert.Equal(SwitchAction.Cancel, policy.Update(input with { Media = input.Media with { Meaningful = false } }, 101).Action);
    }
    [Fact]
    public void TransportLossAndReverseFailWithoutReacquisition()
    {
        foreach (bool reverse in new[] { false, true })
        {
            var policy = new SwitchCoordinator();
            var input = Input();
            var id = policy.Update(input, 100).RequestId!.Value;
            policy.Accept(id, input, 100);
            var state = reverse ? input.AirPods with { ReleaseAt = 101 } : new AirPodsState { Epoch = 2 };
            Assert.Equal(SwitchAction.Failed, policy.Update(input with { AirPods = state }, 101).Action);
        }
    }
}
