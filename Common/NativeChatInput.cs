using System;
using System.Runtime.InteropServices;
using ReLogic.Localization.IME;
using ReLogic.OS;
using SDL2;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Fixed development probe for this game's own Windows window, not a general OS input tool.
public sealed class NativeChatInput : ModSystem
{
    internal const string StatusCommand = "/tpa status";
    private int nextCharacter = -1;
    internal bool Typing => nextCharacter >= 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr value, IntPtr flags);

    private static void SendCharacter(char character)
    {
        if (!OperatingSystem.IsWindows() || !Main.hasFocus)
            throw new InvalidOperationException("This probe requires the Windows test game in the foreground.");
        var info = new SDL.SDL_SysWMinfo();
        SDL.SDL_VERSION(out info.version);
        if (SDL.SDL_GetWindowWMInfo(Main.instance.Window.Handle, ref info) == SDL.SDL_bool.SDL_FALSE)
            throw new InvalidOperationException("Cannot find this game's native window.");
        // WM_CHAR enters WinImm32Ime and Main's existing character buffer.
        SendMessageW(info.info.win.window, 0x0102, (IntPtr)character, IntPtr.Zero);
    }

    internal void BeginStatus()
    {
        if (!Main.drawingPlayerChat || !Platform.Get<IImeService>().IsEnabled)
            throw new InvalidOperationException("Open chat and wait for IME to become enabled first.");
        if (Typing || Main.chatText.Length != 0)
            throw new InvalidOperationException("The chat input must be empty; existing text will not be overwritten.");
        nextCharacter = 0;
    }

    internal void Tick()
    {
        if (!Typing) return;
        if (!Main.drawingPlayerChat || !Main.hasFocus)
        {
            nextCharacter = -1;
            return;
        }
        // One character per update stays below the game's ten-character input buffer limit.
        try
        {
            SendCharacter(StatusCommand[nextCharacter++]);
            if (nextCharacter == StatusCommand.Length) nextCharacter = -1;
        }
        catch
        {
            nextCharacter = -1;
            throw;
        }
    }

    internal void SubmitStatus()
    {
        if (Typing || !Main.drawingPlayerChat || Main.chatText != StatusCommand || !Main.chatRelease)
            throw new InvalidOperationException("Only the completed /tpa status test command can be submitted.");
        SendCharacter('\r');
    }

    public override void OnWorldUnload() => nextCharacter = -1;
    public override void OnModUnload() => nextCharacter = -1;
}
