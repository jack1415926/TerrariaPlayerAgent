using System;
using System.IO;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.SelfCheck;

// Offline replay for the held-item packet layout. No game, no network, no packages.
internal static class Program
{
    private static int failures, checks;

    private static int Main()
    {
        RoundTrip(0, 3507, 0, 1);
        RoundTrip(49, 3509, 82, 999);
        RoundTrip(0, 0, 0, 0);

        var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            new HeldItemWire(7, 3506, 12, 3).Write(writer);
        Check("wire size is six bytes", buffer.Length == 6);

        var truncated = new MemoryStream(new byte[] { 7, 0 });
        using var reader = new BinaryReader(truncated);
        Check("truncated payload is rejected", ThrowsEndOfStream(() => HeldItemWire.Read(reader)));

        Console.WriteLine(failures == 0
            ? $"SELF-CHECK OK ({checks} checks)"
            : $"SELF-CHECK FAILED ({failures}/{checks})");
        return failures == 0 ? 0 : 1;
    }

    private static void RoundTrip(int slot, int type, int prefix, int stack)
    {
        var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            new HeldItemWire(slot, type, prefix, stack).Write(writer);
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);
        HeldItemWire read = HeldItemWire.Read(reader);
        Check($"roundtrip slot {slot}", read.Slot == slot);
        Check($"roundtrip type {type}", read.Type == type);
        Check($"roundtrip prefix {prefix}", read.Prefix == prefix);
        Check($"roundtrip stack {stack}", read.Stack == stack);
    }

    private static bool ThrowsEndOfStream(Action action)
    {
        try { action(); return false; }
        catch (EndOfStreamException) { return true; }
    }

    private static void Check(string name, bool ok)
    {
        checks++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) failures++;
    }
}
