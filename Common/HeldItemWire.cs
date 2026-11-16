using System.IO;

namespace TerrariaPlayerAgent.Common;

// Wire layout for the displayed held item only; it has no Terraria dependency so it can be replayed offline.
public readonly struct HeldItemWire
{
    public readonly byte Slot;
    public readonly short Type;
    public readonly byte Prefix;
    public readonly short Stack;

    public HeldItemWire(int slot, int type, int prefix, int stack)
    {
        Slot = (byte)slot;
        Type = (short)type;
        Prefix = (byte)prefix;
        Stack = (short)stack;
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Slot);
        writer.Write(Type);
        writer.Write(Prefix);
        writer.Write(Stack);
    }

    public static HeldItemWire Read(BinaryReader reader) =>
        new(reader.ReadByte(), reader.ReadInt16(), reader.ReadByte(), reader.ReadInt16());
}
