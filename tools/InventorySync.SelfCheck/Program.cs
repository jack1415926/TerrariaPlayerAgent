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
        int[] types = StartingKit.ClassicItemTypes;
        Check("classic kit has three items", types.Length == 3);
        Check("classic kit is the copper shortsword, pickaxe and axe",
            string.Join(",", types) == "3507,3509,3506");
    }

    private static void CheckStartingInventoryApply()
    {
        ResetLoader();
        var player = new Player();

        Exception? failure = StartingInventory.TryApply(player);
        Check("classic apply succeeds", failure == null);
        Check("classic apply writes three tools", OccupiedTypes(player) == "3507,3509,3506");
        Check("classic apply selects slot 0", player.selectedItem == 0);
        Check("apply used GetStartingItems once", PlayerLoader.GetStartingItemsCalls == 1);
        Check("apply used SetStartInventory once", PlayerLoader.SetStartInventoryCalls == 1);
        Check("apply rolled -1 prefixes for the three tools",
            string.Join(";", PlayerLoader.PrefixCalls.Select(call => $"{call.Type}:{call.Prefix}")) ==
            "3507:-1;3509:-1;3506:-1");
    }

    private static void CheckStartingInventoryFailure()
    {
        ResetLoader();
        PlayerLoader.GetStartingItemsHook = (_, _, _) => throw new InvalidOperationException("injected");
        var player = new Player();

        Exception? failure = StartingInventory.TryApply(player);
        Check("failed apply reports the exception", failure is InvalidOperationException);
        Check("failed apply writes no partial items", OccupiedTypes(player) == "");
        Check("failed apply leaves the selected slot unchanged", player.selectedItem == 0);
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

        Exception? failure = StartingInventory.TryApply(player);
        Check("mod starting item is kept", failure == null && player.inventory[3].type == 24);
    }

    // Source-level contract only: the self-check cannot execute DemoPlayerSystem.Spawn without the game.
    private static void CheckSingleCallSite()
    {
        string? root = FindRepoRoot();
        Check("repository root is reachable for the call-site check", root != null);
        if (root == null) return;
        string source = File.ReadAllText(Path.Combine(root, "Common", "DemoPlayerSystem.cs"));
        string spawn = MethodBody(source, "internal string Spawn(Player owner)");
        Check("Spawn applies the starting kit", spawn.Contains("StartingInventory.TryApply(", StringComparison.Ordinal));
        int apply = spawn.IndexOf("StartingInventory.TryApply(", StringComparison.Ordinal);
        int published = spawn.IndexOf("Actor = actor;", StringComparison.Ordinal);
        int slotPublished = spawn.IndexOf("Main.player[slot] = actor;", StringComparison.Ordinal);
        Check("Spawn rejects starting-inventory failure before publishing",
            apply >= 0 && published > apply && slotPublished > apply &&
            spawn[apply..published].Contains("return", StringComparison.Ordinal));
        int total = Directory.EnumerateFiles(Path.Combine(root, "Common"), "*.cs", SearchOption.AllDirectories)
            .Sum(file => Count(File.ReadAllText(file), "StartingInventory.TryApply("));
        Check("starting kit has a single call site in Common", total == 1);
    }

    private static void ResetLoader()
    {
        PlayerLoader.GetStartingItemsHook = null;
        PlayerLoader.PrefixCalls.Clear();
        PlayerLoader.GetStartingItemsCalls = 0;
        PlayerLoader.SetStartInventoryCalls = 0;
    }

    private static string OccupiedTypes(Player player) =>
        string.Join(",", player.inventory.Where(item => !item.IsAir).Select(item => item.type));

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
