using Terraria;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Server-side respawn detection for the virtual actor. This deliberately does not reuse
// DemoUpdateProbe.IsActor (single-player only), so multiplayer generations still advance.
public sealed class ActorLifecycleProbe : ModPlayer
{
    public override void OnRespawn()
    {
        var system = ModContent.GetInstance<DemoPlayerSystem>();
        if (ReferenceEquals(Player, system.Actor))
            system.RecordActorRespawn();
    }
}
