// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace AirPodsSwitch.Tray;

/// <summary>Three circular 10 MiB segments per connection session.</summary>
internal sealed class SessionLogWriter(string path) : TextWriter
{
    private StreamWriter? writer;
    private long size;
    private int segment;
    public override Encoding Encoding => Encoding.UTF8;
    public override void WriteLine(string? value)
    {
        int bytes = Encoding.UTF8.GetByteCount(value ?? "") + 2;
        if (writer is null || size + bytes > 10 * 1024 * 1024)
        {
            writer?.Dispose();
            string target = segment == 0 ? path : path + "." + segment;
            writer = new StreamWriter(new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read));
            size = 0;
            segment = (segment + 1) % 3;
        }
        writer.WriteLine(value);
        size += bytes;
    }
    public override void Flush() => writer?.Flush();
    protected override void Dispose(bool disposing) { if (disposing) writer?.Dispose(); base.Dispose(disposing); }
    public override async ValueTask DisposeAsync()
    {
        if (writer is not null) await writer.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
