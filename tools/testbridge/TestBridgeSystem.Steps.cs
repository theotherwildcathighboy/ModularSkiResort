// Step sequences: mouse and keyboard input spread over frames, because a tool sees a hover only on a later frame than the move, and
// a click needs the button held across frames. World targets are resolved on the frame they run, after any camera move has settled.

using System;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private const int kMaxSteps = 1000;
        private const int kSettleFrames = 5;

        private sealed class Sequence
        {
            public int id;
            public JArray steps;
            public int index;
            public int phase;
            public int waitFrames;
            public int count;
            public float2 start;
            public Vector3 lastPosition;
            public Quaternion lastRotation;
            public JArray results = new JArray();
            public bool done;
            public string error;
        }

        private Sequence m_Sequence;
        private int m_NextSequenceId = 1;

        private JObject StartSequence(JObject args)
        {
            if (m_Sequence != null && !m_Sequence.done)
                throw new InvalidOperationException($"sequence {m_Sequence.id} is still running");
            var steps = (JArray)args["steps"];
            if (steps == null || steps.Count == 0 || steps.Count > kMaxSteps)
                throw new ArgumentException($"steps must hold 1 to {kMaxSteps} entries");
            m_Sequence = new Sequence { id = m_NextSequenceId++, steps = steps };
            return SequenceState();
        }

        private JObject AbortSequence()
        {
            if (m_Sequence != null && !m_Sequence.done)
                Finish(m_Sequence, "aborted");
            NativeInput.ReleaseAll();
            return SequenceState();
        }

        private JObject SequenceState()
        {
            if (m_Sequence == null)
                return new JObject { ["id"] = 0, ["done"] = true };
            return new JObject
            {
                ["id"] = m_Sequence.id,
                ["done"] = m_Sequence.done,
                ["step"] = m_Sequence.index,
                ["of"] = m_Sequence.steps.Count,
                ["error"] = m_Sequence.error,
                ["results"] = m_Sequence.results,
            };
        }

        private void TickSequence()
        {
            Sequence s = m_Sequence;
            if (s == null || s.done)
                return;
            try
            {
                if (s.waitFrames > 0)
                {
                    s.waitFrames--;
                    return;
                }
                // Steps with no wait run on the same frame; the bound only stops a runaway list from stalling one frame.
                for (int i = 0; i < 64 && !s.done && s.waitFrames == 0; i++)
                {
                    if (s.index >= s.steps.Count)
                    {
                        Finish(s, null);
                        return;
                    }
                    if (!RunStep(s, (JObject)s.steps[s.index]))
                        return;
                }
            }
            catch (Exception e)
            {
                Finish(s, $"step {s.index}: {e.Message}");
            }
        }

        /// <summary>Runs the current step. False: it continues on the next frame.</summary>
        private bool RunStep(Sequence s, JObject step)
        {
            string op = (string)step["op"];
            int after = (int?)step["after"] ?? 2;
            switch (op)
            {
                case "move":
                    return RunMove(s, step, after);
                case "down":
                    NativeInput.Button(ButtonOf(step), true);
                    Next(s, after);
                    return true;
                case "up":
                    NativeInput.Button(ButtonOf(step), false);
                    Next(s, after);
                    return true;
                case "click":
                    if (s.phase == 0)
                    {
                        NativeInput.Button(ButtonOf(step), true);
                        s.phase = 1;
                        s.waitFrames = (int?)step["hold"] ?? 3;
                        return true;
                    }
                    NativeInput.Button(ButtonOf(step), false);
                    Next(s, after);
                    return true;
                case "wheel":
                    NativeInput.Wheel((int)step["delta"]);
                    Next(s, after);
                    return true;
                case "key":
                    string[] keys = ((string)step["keys"]).Split('+');
                    if (s.phase == 0)
                    {
                        foreach (string key in keys)
                            NativeInput.Key(key, true);
                        s.phase = 1;
                        s.waitFrames = (int?)step["hold"] ?? 3;
                        return true;
                    }
                    for (int i = keys.Length - 1; i >= 0; i--)
                        NativeInput.Key(keys[i], false);
                    Next(s, after);
                    return true;
                case "keydown":
                    NativeInput.Key((string)step["key"], true);
                    Next(s, after);
                    return true;
                case "keyup":
                    NativeInput.Key((string)step["key"], false);
                    Next(s, after);
                    return true;
                case "wait":
                    Next(s, (int)step["frames"]);
                    return true;
                case "camera":
                    SetCamera(step);
                    Next(s, 0);
                    return true;
                case "settle":
                    return RunSettle(s, step);
                case "hover":
                    s.results.Add(new JObject { ["step"] = s.index, ["hover"] = Hover() });
                    Next(s, 0);
                    return true;
                case "status":
                    s.results.Add(new JObject { ["step"] = s.index, ["status"] = Status(includeSequence: false) });
                    Next(s, 0);
                    return true;
                case "dump":
                    s.results.Add(new JObject { ["step"] = s.index, ["dump"] = DumpQuery(step) });
                    Next(s, 0);
                    return true;
                case "waitFor":
                    return RunWaitFor(s, step);
                case "sim":
                case "time":
                case "money":
                case "climate":
                    s.results.Add(new JObject { ["step"] = s.index, [op] = Run(op, step) });
                    Next(s, 0);
                    return true;
                default:
                    throw new ArgumentException($"unknown step op '{op}'");
            }
        }

        /// <summary>Moves to client pixels (x, y) or a world point (wx, wz, optional wy), over "frames" frames for a drag.</summary>
        private bool RunMove(Sequence s, JObject step, int after)
        {
            float2 target = MoveTarget(step);
            int frames = Math.Max(1, (int?)step["frames"] ?? 1);
            if (s.phase == 0)
            {
                if (!NativeInput.TryGetCursorClient(out int x, out int y))
                    throw new InvalidOperationException("the game window does not have focus");
                s.start = new float2(x, y);
                s.count = 0;
                s.phase = 1;
            }
            s.count++;
            float2 position = math.lerp(s.start, target, (float)s.count / frames);
            NativeInput.MoveClient((int)math.round(position.x), (int)math.round(position.y));
            if (s.count < frames)
                return false;
            Next(s, after);
            return true;
        }

        private float2 MoveTarget(JObject step)
        {
            if (step["wx"] == null)
                return new float2((float)step["x"], (float)step["y"]);
            Vector3 screen = m_Camera.activeCamera.WorldToScreenPoint(WorldPoint(step));
            if (!IsSafeTarget(screen))
                throw new InvalidOperationException($"world point is off screen or in the edge-scroll band: screen {screen}");
            return new float2(screen.x, Screen.height - 1 - screen.y);
        }

        /// <summary>Waits until the camera has held still for a few frames, so a world target projects to where it will stay.</summary>
        private bool RunSettle(Sequence s, JObject step)
        {
            Transform camera = m_Camera.activeCamera.transform;
            bool still = (camera.position - s.lastPosition).sqrMagnitude < 1e-4f && Quaternion.Angle(camera.rotation, s.lastRotation) < 0.01f;
            s.lastPosition = camera.position;
            s.lastRotation = camera.rotation;
            s.phase = still ? s.phase + 1 : 0;
            if (s.phase >= kSettleFrames)
            {
                Next(s, 0);
                return true;
            }
            s.count++;
            if (s.count > ((int?)step["timeout"] ?? 600))
                throw new InvalidOperationException("the camera did not settle");
            return false;
        }

        /// <summary>Waits until a dump query matches at least "min" entities (default 1), so a test waits on the result instead of a guessed
        /// delay. Checked every 10 frames. Gives up after "timeout" frames (default 1200) and fails the sequence.</summary>
        private bool RunWaitFor(Sequence s, JObject step)
        {
            int min = (int?)step["min"] ?? 1;
            int matched = (int)DumpQuery(step)["count"];
            if (matched >= min)
            {
                s.results.Add(new JObject { ["step"] = s.index, ["waitFor"] = matched, ["frames"] = s.count });
                Next(s, 0);
                return true;
            }
            s.count += 10;
            if (s.count > ((int?)step["timeout"] ?? 1200))
                throw new InvalidOperationException($"waitFor timed out with {matched} of {min} matches");
            s.waitFrames = 10;
            return true;
        }

        private static string ButtonOf(JObject step)
        {
            return (string)step["button"] ?? "left";
        }

        private static void Next(Sequence s, int waitFrames)
        {
            s.index++;
            s.phase = 0;
            s.count = 0;
            s.waitFrames = waitFrames;
        }

        private static void Finish(Sequence s, string error)
        {
            s.done = true;
            s.error = error;
            NativeInput.ReleaseAll();
            if (error != null)
                Mod.Log.Warn($"Sequence {s.id}: {error}");
        }
    }
}
