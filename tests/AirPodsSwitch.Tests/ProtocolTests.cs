// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using AirPodsSwitch.Core.Protocol;
using AirPodsSwitch.Core.State;

namespace AirPodsSwitch.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void SourceAddressIsReversedButHostListIsForward()
    {
        var source = Assert.IsType<SourceReceived>(AacpProtocol.Parse(Convert.FromHexString("040004000E0066554433221102")).Event);
        Assert.Equal("11:22:33:44:55:66", source.Source.Address.ToString());
        Assert.Equal(AudioSourceType.Media, source.Source.Type);
        var hosts = Assert.IsType<DevicesReceived>(AacpProtocol.Parse(Convert.FromHexString("040004002E000000011122334455660000")).Event);
        Assert.Equal(source.Source.Address, Assert.Single(hosts.Devices).Address);
    }
    [Theory]
    [InlineData("040004000E00665544332211")]
    [InlineData("040004000E00665544332211FF")]
    [InlineData("040004002E000000021122334455660000")]
    [InlineData("040004002E000000")]
    [InlineData("0400040009000602FFFFFF")]
    public void MalformedPacketIsRejected(string hex) => Assert.False(AacpProtocol.Parse(Convert.FromHexString(hex)).Success);

    [Fact]
    public void EveryTruncationOfSourceAndListIsRejected()
    {
        foreach (string fixture in new[] { "040004000E0066554433221101", "040004002E000000011122334455660000" })
        {
            var full = Convert.FromHexString(fixture);
            for (int length = 0; length < full.Length; length++) Assert.False(AacpProtocol.Parse(full.AsSpan(0, length)).Success);
        }
    }
    [Fact]
    public void HighOpcodeByteIsNotIgnored()
    {
        Assert.IsType<OtherPacket>(AacpProtocol.Parse(Convert.FromHexString("040004000E0166554433221102")).Event);
    }
    [Fact]
    public void BatteryDisconnectedIsUnknownNotStalePercentage()
    {
        var parsed = Assert.IsType<BatteryReceived>(AacpProtocol.Parse(Convert.FromHexString("04000400040002040150020108017F0401")).Event);
        Assert.Equal(80, parsed.Batteries[0].Percent);
        Assert.Null(parsed.Batteries[1].Percent);
    }
    [Fact]
    public void IdentityIsNotInferredFromFriendlyName()
    {
        Assert.Null(AirPodsModels.Find("AirPods 5"));
        var parsed = Assert.IsType<IdentityReceived>(AacpProtocol.Parse(AacpProtocol.Frame(0x1D,
            Encoding.UTF8.GetBytes("\0My buds\0A3531\0Apple Inc.\0PRIVATE-SERIAL\0F1\0"))).Event);
        Assert.Equal("AirPods 5", parsed.Identity.Model?.Name);
        Assert.Equal("F1", parsed.Identity.Firmware);
        Assert.Null(AirPodsModels.Find("A9999"));
    }
    [Fact]
    public void UnknownEarValueDoesNotBecomeWorn()
    {
        var parsed = Assert.IsType<WearReceived>(AacpProtocol.Parse(Convert.FromHexString("040004000600FF01")).Event);
        Assert.False(parsed.Wear.IsWorn);
    }
    [Fact]
    public void HandshakeAndHijackAreBoundedAndAddressed()
    {
        Assert.Equal(16, AacpProtocol.Handshake().Length);
        var packet = AacpProtocol.Hijack(BluetoothAddress.Parse("11:22:33:44:55:66"));
        Assert.Equal(112, packet.Length);
        Assert.Equal("665544332211", Convert.ToHexString(packet.AsSpan(6, 6)));
        Assert.Contains("Hijackv2", Encoding.ASCII.GetString(packet));
    }
    [Fact]
    public void OldEpochOrOlderSourceCannotOverwriteNewState()
    {
        var store = new AirPodsStateStore();
        var epoch = store.BeginConnection();
        var source = new SourceReceived(new(BluetoothAddress.Parse("11:22:33:44:55:66"), AudioSourceType.Call));
        Assert.True(store.Apply(source, 100, epoch));
        Assert.False(store.Apply(source, 99, epoch));
        store.Disconnect();
        Assert.False(store.Apply(source, 200, epoch));
        Assert.Null(store.Current.Source);
    }
    [Fact]
    public void ArbitraryBytesNeverCrashParser()
    {
        var random = new Random(4431);
        for (int i = 0; i < 4000; i++)
        {
            var buffer = new byte[random.Next(0, 100)];
            random.NextBytes(buffer);
            if (i % 2 == 0 && buffer.Length >= 6) new byte[] { 4, 0, 4, 0 }.CopyTo(buffer, 0);
            _ = AacpProtocol.Parse(buffer);
        }
    }
}
