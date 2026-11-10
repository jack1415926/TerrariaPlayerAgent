using System;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Identifies one stay in a world so a resumed task cannot silently attach to a different one.
public sealed class WorldSession : ModSystem
{
    public string Id { get; private set; } = "";

    public override void OnWorldLoad()
    {
        Id = $"{Main.worldID}-{Guid.NewGuid():N}";
    }

    public override void OnWorldUnload()
    {
        Id = "";
    }
}
