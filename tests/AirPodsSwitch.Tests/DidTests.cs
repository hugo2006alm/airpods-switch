// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using AirPodsSwitch.Core.Did;

namespace AirPodsSwitch.Tests;

public sealed class DidTests
{
    private sealed class FakeStore : IDidRegistryStore
    {
        public RegistrySnapshot Snapshot { get; private set; } = new(true,
            DidManager.Names.ToDictionary(n => n, _ => RegistryValue.Absent));
        public int Writes;
        public int FailAt;
        public RegistrySnapshot Read() => new(Snapshot.KeyExists, Snapshot.Values.ToDictionary(p => p.Key,
            p => p.Value with { Data = p.Value.Data.ToArray() }));
        public void Write(string name, RegistryValue value)
        {
            Writes++;
            if (Writes == FailAt) throw new IOException("simulated registry failure");
            Snapshot.Values[name] = value;
        }
    }
    [Fact]
    public void ApplyAndRestorePreserveAbsentValues()
    {
        var store = new FakeStore();
        var manager = new DidManager(store, "machine", () => true);
        var backup = manager.Capture();
        manager.ApplyApple(backup, "APPLY-APPLE");
        Assert.True(store.Read().Values[DidManager.Names[1]].Same(RegistryValue.Dword(76)));
        Assert.False(store.Read().Values[DidManager.Names[2]].Present);
        manager.Restore(backup, "RESTORE-DID");
        Assert.True(store.Read().Same(backup.Backup.Original));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void OriginalTypeAndRawDataRoundTrip(int kind)
    {
        var store = new FakeStore();
        var raw = kind is 4 or 5 ? new byte[] { 0x80, 0, 0, 0xFF } :
            kind == 11 ? BitConverter.GetBytes(long.MinValue) : Encoding.Unicode.GetBytes("%RAW%\0\0");
        store.Write(DidManager.Names[0], new(true, (uint)kind, raw));
        var manager = new DidManager(store, "machine", () => true);
        var backup = manager.Capture();
        manager.ApplyApple(backup, "APPLY-APPLE");
        manager.Restore(backup, "RESTORE-DID");
        Assert.True(store.Read().Same(backup.Backup.Original));
    }
    [Fact]
    public void RequiresConfirmationAdminAndMatchingMachine()
    {
        var store = new FakeStore();
        var manager = new DidManager(store, "machine", () => true);
        var backup = manager.Capture();
        Assert.Throws<InvalidOperationException>(() => manager.ApplyApple(backup, ""));
        Assert.Throws<UnauthorizedAccessException>(() => new DidManager(store, "machine", () => false).ApplyApple(backup, "APPLY-APPLE"));
        Assert.Throws<InvalidOperationException>(() => new DidManager(store, "other", () => true).ApplyApple(backup, "APPLY-APPLE"));
        Assert.Equal(0, store.Writes);
    }
    [Fact]
    public void TamperingAndUnexpectedEditsAreRejected()
    {
        var store = new FakeStore();
        var manager = new DidManager(store, "machine", () => true);
        var backup = manager.Capture();
        Assert.Throws<InvalidOperationException>(() => manager.ApplyApple(backup with { Sha256 = "wrong" }, "APPLY-APPLE"));
        store.Write(DidManager.Names[1], RegistryValue.Dword(9));
        Assert.Throws<InvalidOperationException>(() => manager.ApplyApple(backup, "APPLY-APPLE"));
        Assert.Throws<InvalidOperationException>(() => manager.Restore(backup, "RESTORE-DID"));
    }
    [Fact]
    public void PartialApplyRollsBackFirstWrite()
    {
        var store = new FakeStore { FailAt = 2 };
        var manager = new DidManager(store, "machine", () => true);
        var backup = manager.Capture();
        Assert.Throws<IOException>(() => manager.ApplyApple(backup, "APPLY-APPLE"));
        Assert.True(store.Read().Same(backup.Backup.Original));
    }
    [Fact]
    public void BackupFileCannotBeOverwritten()
    {
        string file = Path.GetTempFileName();
        try
        {
            var backup = new DidManager(new FakeStore(), "machine", () => false).Capture();
            Assert.Throws<IOException>(() => DidManager.SaveNew(file, backup));
        }
        finally { File.Delete(file); }
    }
}
