using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Server-owned AI projectiles hit on the server. ModPlayer.OnHitNPCWithProj is documented
// client-only, so the authoritative record comes from the GlobalNPC hook.
public sealed class CombatHitProbe : GlobalNPC
{
    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        if (Main.netMode != NetmodeID.Server) return;
        var system = ModContent.GetInstance<DemoPlayerSystem>();
        if (system.Actor is not { } actor || projectile.owner != actor.whoAmI) return;
        if (!NativeCombat.IsAuthoritativeActor(actor)) return;
        system.RecordProjectileHit(projectile, npc, damageDone);
    }
}
