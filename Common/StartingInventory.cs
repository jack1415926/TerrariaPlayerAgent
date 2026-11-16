using System;
using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// The vanilla classic starting kit is composed by
// Terraria.GameContent.UI.States.UICharacterCreation.SetupPlayerStatsAndInventoryBasedOnDifficulty
// in tML 2026.08.3.0; new Player() does not run it. PlayerLoader.GetStartingItems is the ModLoader
// extension point and SetStartInventory performs the native slot clearing/placement.
// The actor is published only after this succeeds, so a throw cannot leave a partially initialized AI.
internal static class StartingInventory
{
    internal static Exception? TryApply(Player player, bool collectorsEdition)
    {
        Item[] backup = (Item[])player.inventory.Clone();
        int previousSelected = player.selectedItem;
        try
        {
            var vanilla = new List<Item>(4);
            foreach (StartingKit.Entry entry in StartingKit.ClassicEntries(collectorsEdition))
            {
                var item = new Item();
                item.SetDefaults(entry.Type);
                if (entry.RollPrefix) item.Prefix(-1);
                vanilla.Add(item);
            }
            List<Item> items = PlayerLoader.GetStartingItems(player, vanilla, mediumCoreDeath: false);
            PlayerLoader.SetStartInventory(player, items);
            player.selectedItem = 0;
            return null;
        }
        catch (Exception ex)
        {
            // Restore slots even though the caller discards this unpublished player on failure.
            Array.Copy(backup, player.inventory, backup.Length);
            player.selectedItem = previousSelected;
            return ex;
        }
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
