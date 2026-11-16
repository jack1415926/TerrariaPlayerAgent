namespace TerrariaPlayerAgent.Common;

// Classic new-character kit transcribed from
// Terraria.GameContent.UI.States.UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty
// in tML 2026.08.3.0. Terraria-free so the offline self-check can compile it.
internal static class StartingKit
{
    internal const int CopperShortsword = 3507;
    internal const int CopperPickaxe = 3509;
    internal const int CopperAxe = 3506;
    internal const int Carrot = 603;

    internal readonly record struct Entry(int Type, bool RollPrefix);

    // Native classic difficulty writes the three copper tools with Prefix(-1), then appends the
    // Carrot without a prefix only when Main.runningCollectorsEdition is true.
    internal static Entry[] ClassicEntries(bool collectorsEdition) =>
        collectorsEdition
            ? new[] { Tool(CopperShortsword), Tool(CopperPickaxe), Tool(CopperAxe), new Entry(Carrot, false) }
            : new[] { Tool(CopperShortsword), Tool(CopperPickaxe), Tool(CopperAxe) };

    private static Entry Tool(int type) => new(type, true);
}
