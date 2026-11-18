namespace TerrariaPlayerAgent.Common;

// The classic new-character kit from
// Terraria.GameContent.UI.States.UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty
// in tML 2026.08.3.0: three copper tools, each rolled with Prefix(-1).
internal static class StartingKit
{
    internal const int CopperShortsword = 3507;
    internal static readonly int[] ClassicItemTypes = { CopperShortsword, 3509, 3506 };
}
