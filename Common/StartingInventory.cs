using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// The vanilla classic starting kit is assigned by
// Terraria.GameContent.UI.States.UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty
// in this tML build; new Player() does not run it. PlayerLoader.GetStartingItems is the
// ModLoader entry point that adds mod starting items on top of a supplied vanilla list.
// shortcut: this classic list is transcribed from that UI method for tML 2026.08.3.0; re-check it on upgrade.
internal static class StartingInventory
{
    private static readonly int[] ClassicItemTypes =
    {
        ItemID.CopperShortsword, ItemID.CopperPickaxe, ItemID.CopperAxe, ItemID.Carrot
    };

    internal static void Apply(Player player)
    {
        var vanilla = new List<Item>(ClassicItemTypes.Length);
        foreach (int type in ClassicItemTypes)
        {
            var item = new Item();
            item.SetDefaults(type);
            item.Prefix(-1);
            vanilla.Add(item);
        }
        List<Item> items = PlayerLoader.GetStartingItems(player, vanilla, mediumCoreDeath: false);
        foreach (Item slot in player.inventory)
            slot.SetDefaults(ItemID.None);
        for (int i = 0; i < items.Count && i < player.inventory.Length; i++)
            player.inventory[i] = items[i];
        player.selectedItem = 0;
    }

    internal static string Describe(Player player)
    {
        var text = new StringBuilder();
        for (int i = 0; i < player.inventory.Length; i++)
        {
            Item item = player.inventory[i];
            if (item.IsAir) continue;
            if (text.Length > 0) text.Append("; ");
            text.Append($"slot{i}=type{item.type}/{item.Name} x{item.stack} prefix{item.prefix}");
        }
        text.Append($"; selected={player.selectedItem} held={player.HeldItem.type}");
        return text.ToString();
    }
}
