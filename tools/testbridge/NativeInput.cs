// Real OS input through SendInput, so the game sees exactly what a player's mouse and keyboard send: the Input System, the UI's
// hit test (mouseOverUI, Game.SceneFlow/UserInterface.cs:136-150) and the tools' raycast (ToolRaycastSystem.cs:71) all take their
// normal path. Every call requires the game to be the foreground window, so input can never land in another application.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SkiTestBridge
{
    internal static class NativeInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);

        private const uint kMouse = 0;
        private const uint kKeyboard = 1;
        private const uint kMove = 0x0001;
        private const uint kWheel = 0x0800;
        private const uint kVirtualDesk = 0x4000;
        private const uint kAbsolute = 0x8000;
        private const uint kKeyUp = 0x0002;
        private const uint kExtendedKey = 0x0001;
        private const int kVirtualX = 76;
        private const int kVirtualY = 77;
        private const int kVirtualWidth = 78;
        private const int kVirtualHeight = 79;

        private static readonly int s_ProcessId = Process.GetCurrentProcess().Id;
        private static readonly HashSet<string> s_HeldButtons = new HashSet<string>();
        private static readonly HashSet<ushort> s_HeldKeys = new HashSet<ushort>();

        private static readonly Dictionary<string, ushort> s_NamedKeys = new Dictionary<string, ushort>
        {
            ["ctrl"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["enter"] = 0x0D, ["esc"] = 0x1B, ["tab"] = 0x09, ["space"] = 0x20,
            ["backspace"] = 0x08, ["delete"] = 0x2E, ["insert"] = 0x2D, ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21,
            ["pagedown"] = 0x22, ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        };

        // Keys Windows flags as extended: without the flag their scan code reads as the number pad.
        private static readonly HashSet<ushort> s_ExtendedKeys = new HashSet<ushort>
        {
            0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28,
        };

        /// <summary>INPUT is 40 bytes on x64; any other size makes every SendInput fail.</summary>
        internal static void CheckLayout()
        {
            int size = Marshal.SizeOf(typeof(INPUT));
            if (size != 40)
                Mod.Log.Error($"INPUT marshals to {size} bytes, expected 40: input will fail.");
        }

        /// <summary>The game's window when it has focus, else zero.</summary>
        internal static IntPtr FocusedGameWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return IntPtr.Zero;
            GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == s_ProcessId ? hwnd : IntPtr.Zero;
        }

        internal static bool TryGetCursorClient(out int x, out int y)
        {
            x = y = 0;
            IntPtr hwnd = FocusedGameWindow();
            if (hwnd == IntPtr.Zero || !GetCursorPos(out POINT p) || !ScreenToClient(hwnd, ref p))
                return false;
            x = p.x;
            y = p.y;
            return true;
        }

        /// <summary>Moves the cursor to a client-area pixel (origin top left).</summary>
        internal static void MoveClient(int x, int y)
        {
            IntPtr hwnd = RequireFocus();
            var p = new POINT { x = x, y = y };
            ClientToScreen(hwnd, ref p);
            int vw = GetSystemMetrics(kVirtualWidth);
            int vh = GetSystemMetrics(kVirtualHeight);
            var input = new INPUT { type = kMouse };
            // Windows maps back with a floor, so round up or the cursor can land one pixel short (measured: 152 became 151).
            input.u.mi.dx = (int)(((long)(p.x - GetSystemMetrics(kVirtualX)) * 65536 + vw - 1) / vw);
            input.u.mi.dy = (int)(((long)(p.y - GetSystemMetrics(kVirtualY)) * 65536 + vh - 1) / vh);
            input.u.mi.dwFlags = kMove | kAbsolute | kVirtualDesk;
            Send(input);
        }

        internal static void Button(string button, bool down)
        {
            RequireFocus();
            SendButton(button, down);
            if (down)
                s_HeldButtons.Add(button);
            else
                s_HeldButtons.Remove(button);
        }

        internal static void Wheel(int delta)
        {
            RequireFocus();
            var input = new INPUT { type = kMouse };
            input.u.mi.mouseData = unchecked((uint)delta);
            input.u.mi.dwFlags = kWheel;
            Send(input);
        }

        internal static void Key(string name, bool down)
        {
            RequireFocus();
            ushort vk = KeyCode(name);
            SendKey(vk, down);
            if (down)
                s_HeldKeys.Add(vk);
            else
                s_HeldKeys.Remove(vk);
        }

        /// <summary>Lets go of everything still held, focus or not: a stuck button or modifier would follow the owner into other apps.</summary>
        internal static void ReleaseAll()
        {
            foreach (string button in new List<string>(s_HeldButtons))
                SendButton(button, false);
            foreach (ushort vk in new List<ushort>(s_HeldKeys))
                SendKey(vk, false);
            s_HeldButtons.Clear();
            s_HeldKeys.Clear();
        }

        private static ushort KeyCode(string name)
        {
            string key = name.ToLowerInvariant();
            if (s_NamedKeys.TryGetValue(key, out ushort vk))
                return vk;
            if (key.Length == 1 && ((key[0] >= 'a' && key[0] <= 'z') || (key[0] >= '0' && key[0] <= '9')))
                return char.ToUpperInvariant(key[0]);
            if (key.Length >= 2 && key[0] == 'f' && int.TryParse(key.Substring(1), out int f) && f >= 1 && f <= 12)
                return (ushort)(0x70 + f - 1);
            throw new ArgumentException($"unknown key '{name}'");
        }

        private static IntPtr RequireFocus()
        {
            IntPtr hwnd = FocusedGameWindow();
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("the game window does not have focus");
            return hwnd;
        }

        private static void SendButton(string button, bool down)
        {
            uint flags;
            switch (button)
            {
                case "left": flags = down ? 0x0002u : 0x0004u; break;
                case "right": flags = down ? 0x0008u : 0x0010u; break;
                case "middle": flags = down ? 0x0020u : 0x0040u; break;
                default: throw new ArgumentException($"unknown button '{button}'");
            }
            var input = new INPUT { type = kMouse };
            input.u.mi.dwFlags = flags;
            Send(input);
        }

        private static void SendKey(ushort vk, bool down)
        {
            var input = new INPUT { type = kKeyboard };
            input.u.ki.wVk = vk;
            input.u.ki.wScan = (ushort)MapVirtualKey(vk, 0);
            input.u.ki.dwFlags = (down ? 0u : kKeyUp) | (s_ExtendedKeys.Contains(vk) ? kExtendedKey : 0u);
            Send(input);
        }

        private static void Send(INPUT input)
        {
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) != 1)
                throw new InvalidOperationException($"SendInput failed, error {Marshal.GetLastWin32Error()}");
        }
    }
}
