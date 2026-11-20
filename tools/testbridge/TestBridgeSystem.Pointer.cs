// Op "pointer": a game-side cursor for measurements without window focus (T253, 2026-11-20).
//
// Without OS focus the game reads no mouse: InputManager.mouseOnScreen is false (it returns m_HasFocus, InputManager.cs:467-477),
// and mouseOverUI keeps its last value, often true. Both flags change only on events: OnFocusChanged (InputManager.cs:1551-1555,
// public, called from Application.focusChanged) and Gameface mouse events (UserInterface.cs:136-148). With no input there are
// no events, so a value the bridge writes stays until real input comes.
//
// While held, every frame: InputManager.OnFocusChanged(true), mouseOverUI = false, and the InputSystem mouse position set to the
// held pixel (InputState.Change on Mouse.current.position, which InputManager.mousePosition reads, InputManager.cs:481-492).
// The game's own systems then see a cursor at that pixel over the world: ToolRaycastSystem.CalculateRaycastLine
// (ToolRaycastSystem.cs:69-78) and every mod system that reads InputManager. No OS input is sent, no window is touched, nothing is
// saved. It ends after `seconds`, on hold false, or at exit; then OnFocusChanged(Application.isFocused) puts the real focus back.
// Not a replacement for real input: clicks and keys are not faked, so tools that need a click still need focus.

using System;
using Game.Input;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private bool m_PointerHeld;
        private float m_PointerDeadline;
        private Vector2 m_PointerPos;   // InputSystem pixels: origin bottom left

        /// <summary>Op "pointer": {hold, seconds, x, y}. x / y are screen pixels from the top left (default: the screen center).
        /// seconds: default 120, at most 900. No argument: the state.</summary>
        private JObject PointerCommand(JObject args)
        {
            if (args["hold"] != null)
            {
                bool hold = (bool)args["hold"];
                if (hold)
                {
                    float seconds = (float?)args["seconds"] ?? 120f;
                    if (!(seconds > 0f) || seconds > kUiMaxHiddenSeconds)
                        throw new ArgumentException($"pointer: seconds must be over 0 and at most {kUiMaxHiddenSeconds}");
                    float x = (float?)args["x"] ?? Screen.width / 2f;
                    float y = (float?)args["y"] ?? Screen.height / 2f;
                    if (x < 0f || x >= Screen.width || y < 0f || y >= Screen.height)
                        throw new ArgumentException($"pointer: x / y must be on the screen ({Screen.width} x {Screen.height})");
                    m_PointerPos = new Vector2(x, Screen.height - 1 - y);
                    m_PointerDeadline = UnityEngine.Time.realtimeSinceStartup + seconds;
                    m_PointerHeld = true;
                    ApplyPointer();
                }
                else
                    ReleasePointer();
            }
            InputManager input = InputManager.instance;
            var result = new JObject
            {
                ["held"] = m_PointerHeld,
                ["mouseOnScreen"] = input != null && input.mouseOnScreen,
                ["mouseOverUI"] = input != null && input.mouseOverUI,
                ["appFocused"] = Application.isFocused,
            };
            if (input != null)
            {
                Vector3 p = input.mousePosition;
                result["mousePosition"] = new JArray(Math.Round(p.x), Math.Round(Screen.height - 1 - p.y));
            }
            return result;
        }

        /// <summary>Every frame while held; ends at the deadline.</summary>
        private void TickPointer()
        {
            if (!m_PointerHeld)
                return;
            if (UnityEngine.Time.realtimeSinceStartup > m_PointerDeadline)
            {
                ReleasePointer();
                return;
            }
            ApplyPointer();
        }

        private void ApplyPointer()
        {
            InputManager input = InputManager.instance;
            if (input == null)
                return;
            input.OnFocusChanged(true);
            input.mouseOverUI = false;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.position.ReadValue() != m_PointerPos)
                InputState.Change(mouse.position, m_PointerPos);
        }

        private void ReleasePointer()
        {
            if (!m_PointerHeld)
                return;
            m_PointerHeld = false;
            InputManager.instance?.OnFocusChanged(Application.isFocused);
        }
    }
}
