using Terraria.ModLoader;
using System.IO;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent;

public sealed class TerrariaPlayerAgentMod : Mod
{
    public override void HandlePacket(BinaryReader reader, int whoAmI) =>
        ModContent.GetInstance<MultiplayerProbe>().Receive(reader, whoAmI);
}
