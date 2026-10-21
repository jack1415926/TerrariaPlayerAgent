using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
using SDL2;
using ReLogic.OS;
using ReLogic.Localization.IME;
using Terraria;
using Terraria.ModLoader;

namespace TerrariaPlayerAgent.Common;

// Opt-in diagnostic: count characters and record Enter events; never store text contents.
public sealed class EnterDiagnostics : ModSystem
{
    private readonly ConcurrentQueue<object> events = new();
    private SDL.SDL_EventFilter? watch;
    private bool tracing;
    private int samples;
    private int downSamples;
    private int focusedSamples;
    private int keyboardEvents;
    private int releaseAfterTicks;
    private uint inputWindow;
    private IImeService? ime;
    private int receivedCharacters;

    private void CountCharacter(char value)
    {
        if (value >= 32 && value != 127)
            System.Threading.Interlocked.Increment(ref receivedCharacters);
    }

    internal void PressEnter()
    {
        IntPtr window = SDL.SDL_GetKeyboardFocus();
        if (window == IntPtr.Zero || !Main.hasFocus)
            throw new InvalidOperationException("Focus the test game before testing Enter.");
        if (releaseAfterTicks > 0) throw new InvalidOperationException("Enter is already held.");
        inputWindow = SDL.SDL_GetWindowID(window);
        PushEnter(true);
        releaseAfterTicks = 6;
    }

    private void PushEnter(bool down)
    {
        var e = new SDL.SDL_Event
        {
            key = new SDL.SDL_KeyboardEvent
            {
                type = down ? SDL.SDL_EventType.SDL_KEYDOWN : SDL.SDL_EventType.SDL_KEYUP,
                timestamp = SDL.SDL_GetTicks(), windowID = inputWindow, state = (byte)(down ? 1 : 0),
                keysym = new SDL.SDL_Keysym { scancode = SDL.SDL_Scancode.SDL_SCANCODE_RETURN,
                    sym = SDL.SDL_Keycode.SDLK_RETURN }
            }
        };
        if (SDL.SDL_PushEvent(ref e) != 1) throw new InvalidOperationException("SDL rejected test Enter event.");
    }

    internal void Start()
    {
        Stop();
        events.Clear();
        samples = downSamples = focusedSamples = keyboardEvents = 0;
        receivedCharacters = 0;
        ime = Platform.Get<IImeService>();
        ime.AddKeyListener(CountCharacter);
        tracing = true;
        watch = Watch;
        SDL.SDL_AddEventWatch(watch, IntPtr.Zero);
    }

    internal void Stop()
    {
        if (releaseAfterTicks > 0) { PushEnter(false); releaseAfterTicks = 0; }
        tracing = false;
        ime?.RemoveKeyListener(CountCharacter);
        ime = null;
        if (watch != null) SDL.SDL_DelEventWatch(watch, IntPtr.Zero);
        watch = null;
    }

    private int Watch(IntPtr user, IntPtr address)
    {
        var e = Marshal.PtrToStructure<SDL.SDL_Event>(address);
        if (e.type is SDL.SDL_EventType.SDL_KEYDOWN or SDL.SDL_EventType.SDL_KEYUP)
        {
            System.Threading.Interlocked.Increment(ref keyboardEvents);
            if (e.key.keysym.sym is SDL.SDL_Keycode.SDLK_RETURN or SDL.SDL_Keycode.SDLK_KP_ENTER)
            {
                events.Enqueue(new { kind = e.type.ToString(), timestamp = e.key.timestamp,
                    key = e.key.keysym.sym.ToString(), repeat = e.key.repeat });
                while (events.Count > 32) events.TryDequeue(out _);
            }
        }
        return 1;
    }

    internal void Sample()
    {
        if (releaseAfterTicks > 0 && --releaseAfterTicks == 0) PushEnter(false);
        if (!tracing) return;
        samples++;
        if (Main.hasFocus) focusedSamples++;
        if (Keyboard.GetState().IsKeyDown(Keys.Enter)) downSamples++;
    }

    internal object Snapshot() => new
    {
        tracing, samples, downSamples, focusedSamples, keyboardEvents, receivedCharacters,
        imeEnabled = Platform.Get<IImeService>().IsEnabled,
        writingText = Terraria.GameInput.PlayerInput.WritingText,
        chatLength = Main.chatText?.Length ?? 0,
        focused = Main.hasFocus,
        sdlKeyboardFocus = SDL.SDL_GetKeyboardFocus() != IntPtr.Zero,
        enterDown = Keyboard.GetState().IsKeyDown(Keys.Enter),
        chatOpen = Main.drawingPlayerChat,
        chatRelease = Main.chatRelease,
        optionsOpen = Main.ingameOptionsWindow,
        enterEvents = events.ToArray()
    };

    public override void OnModUnload() => Stop();
}
