using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.SelfCheck;

// Offline replay for the held-item packet layout and the starting-kit policy.
// No game, no network, no packages; the Terraria types are the local shim.
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

        CheckWireCompatibility();
        CheckClassicKit();
        CheckCollectorKit();
        CheckStartingInventoryApply();
        CheckStartingInventoryFailure();
        CheckStartingInventoryExtension();
        CheckSingleCallSite();

        Console.WriteLine(failures == 0
            ? $"SELF-CHECK OK ({checks} checks)"
            : $"SELF-CHECK FAILED ({failures}/{checks})");
        return failures == 0 ? 0 : 1;
    }

    private static void CheckWireCompatibility()
    {
        var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            new HeldItemWire(7, 3506, 12, 3).Write(writer);
            writer.Write((byte)0xAA);
        }
        Check("wire keeps slot/type/prefix/stack byte order",
            BitConverter.ToString(buffer.ToArray(), 0, 6).Replace("-", "") == "07B20D0C0300");
        buffer.Position = 0;
        using (var reader = new BinaryReader(buffer))
        {
            HeldItemWire read = HeldItemWire.Read(reader);
            Check("wire reads the original six bytes",
                read.Slot == 7 && read.Type == 3506 && read.Prefix == 12 && read.Stack == 3);
            Check("wire leaves appended bytes for the next field",
                buffer.Position == 6 && buffer.ReadByte() == 0xAA);
        }
        RoundTrip(255, short.MaxValue, 255, short.MaxValue);
    }

    private static void CheckClassicKit()
    {
        StartingKit.Entry[] entries = StartingKit.ClassicEntries(collectorsEdition: false);
        Check("classic kit has three items", entries.Length == 3);
        Check("classic kit types are copper shortsword, pickaxe, axe",
            string.Join(",", entries.Select(entry => entry.Type)) == "3507,3509,3506");
        Check("classic kit never grants Carrot", entries.All(entry => entry.Type != StartingKit.Carrot));
        Check("classic tools roll a prefix", entries.All(entry => entry.RollPrefix));
    }

    private static void CheckCollectorKit()
    {
        StartingKit.Entry[] entries = StartingKit.ClassicEntries(collectorsEdition: true);
        Check("collector kit appends Carrot to the classic three",
            entries.Length == 4 && entries[3].Type == StartingKit.Carrot &&
            string.Join(",", entries.Take(3).Select(entry => entry.Type)) == "3507,3509,3506");
        Check("collector Carrot is not prefixed", !entries[3].RollPrefix);
    }

    private static void CheckStartingInventoryApply()
    {
        ResetLoader();
        var player = new Player();
        player.inventory[0].SetDefaults(42);
        player.inventory[10].SetDefaults(99);
        player.selectedItem = 7;

        Exception? failure = StartingInventory.TryApply(player, collectorsEdition: false);
        Check("classic apply succeeds", failure == null);
        Check("classic apply writes three tools", OccupiedTypes(player) == "3507,3509,3506");
        Check("classic apply clears leftover slots", player.inventory[10].IsAir);
        Check("classic apply selects slot 0", player.selectedItem == 0);
        Check("classic apply never adds Carrot", !HasType(player, StartingKit.Carrot));
        Check("apply used GetStartingItems once", PlayerLoader.GetStartingItemsCalls == 1);
        Check("apply used SetStartInventory once", PlayerLoader.SetStartInventoryCalls == 1);
        Check("apply rolled -1 prefixes for the three tools",
            string.Join(";", PlayerLoader.PrefixCalls.Select(call => $"{call.Type}:{call.Prefix}")) ==
            "3507:-1;3509:-1;3506:-1");
    }

    private static void CheckStartingInventoryFailure()
    {
        ResetLoader();
        PlayerLoader.GetStartingItemsHook = (_, _, _) => throw new InvalidOperationException("simulated");
        var player = new Player();
        player.inventory[0].SetDefaults(42);
        player.inventory[1].SetDefaults(43);
        player.selectedItem = 5;

        Exception? failure = StartingInventory.TryApply(player, collectorsEdition: false);
        Check("failed apply reports the exception", failure is InvalidOperationException);
        Check("failed apply restores slot 0", player.inventory[0].type == 42);
        Check("failed apply restores slot 1", player.inventory[1].type == 43);
        Check("failed apply restores selected slot", player.selectedItem == 5);

        ResetLoader();
        PlayerLoader.SetStartInventoryHook = (p, items) =>
        {
            p.inventory[0] = items[0];
            throw new InvalidOperationException("partial placement");
        };
        var partial = new Player();
        partial.inventory[0].SetDefaults(42);
        partial.selectedItem = 5;

        Exception? partialFailure = StartingInventory.TryApply(partial, collectorsEdition: false);
        Check("partial placement reports the exception", partialFailure is InvalidOperationException);
        Check("partial placement restores slot 0", partial.inventory[0].type == 42);
        Check("partial placement restores selected slot", partial.selectedItem == 5);
    }

    private static void CheckStartingInventoryExtension()
    {
        ResetLoader();
        PlayerLoader.GetStartingItemsHook = (_, vanilla, _) =>
        {
            var items = new List<Item>(vanilla);
            var extra = new Item();
            extra.SetDefaults(24);
            items.Add(extra);
            return items;
        };
        var player = new Player();

        Exception? failure = StartingInventory.TryApply(player, collectorsEdition: false);
        Check("mod starting item is kept", failure == null && player.inventory[3].type == 24);
    }

    private static void CheckSingleCallSite()
    {
        string? root = FindRepoRoot();
        Check("repository root is reachable for the call-site check", root != null);
        if (root == null) return;
        string source = File.ReadAllText(Path.Combine(root, "Common", "DemoPlayerSystem.cs"));
        string spawn = MethodBody(source, "internal string Spawn(Player owner)");
        Check("Spawn applies the starting kit", spawn.Contains("StartingInventory.TryApply(", StringComparison.Ordinal));
        int total = Directory.EnumerateFiles(Path.Combine(root, "Common"), "*.cs", SearchOption.AllDirectories)
            .Sum(file => Count(File.ReadAllText(file), "StartingInventory.TryApply("));
        Check("starting kit has a single call site in Common", total == 1);
    }

    private static void ResetLoader()
    {
        PlayerLoader.GetStartingItemsHook = null;
        PlayerLoader.SetStartInventoryHook = null;
        PlayerLoader.PrefixCalls.Clear();
        PlayerLoader.GetStartingItemsCalls = 0;
        PlayerLoader.SetStartInventoryCalls = 0;
    }

    private static string OccupiedTypes(Player player) =>
        string.Join(",", player.inventory.Where(item => !item.IsAir).Select(item => item.type));

    private static bool HasType(Player player, int type) =>
        player.inventory.Any(item => item.type == type);

    private static string? FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "TerrariaPlayerAgent.csproj")))
                return dir.FullName;
        return null;
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return "";
        int open = source.IndexOf('{', start);
        if (open < 0) return "";
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..(i + 1)];
        }
        return "";
    }

    private static int Count(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
