// Op "vfxprobe" (T270, 1.0.4): probes a vanilla VFX graph as a snow-gun plume. Dev only, nothing is saved: every change lives in
// this run of the game (restore with action "restore", or quit).
// Facts it builds on (decompile 1.6.0f1, same in 1.6.2f1): VFXSystem.Initialize makes ONE VisualEffect per EffectPrefab with a VFX
// component, on a GameObject named "VFX " + prefab name (Game.Effects/VFXSystem.cs:146-160), and feeds it per instance position +
// intensity, rotation (euler, radians) and scale through the "InstanceData" texture (:79-82), plus "Count", "WindTexture" and
// "MapOffsetScale" (:148, :160, :274-275). The rotation comes from the owner's Transform and the prefab's Effect element m_Rotation,
// which EffectTransformSystem reads from the prefab's Effect buffer (Game.Rendering/EffectTransformSystem.cs:79, :139, :162).
//   list    : {names?: ["FountainVFX", ...]} per VisualEffect: asset name, exposed properties (name, type), events, alive particles.
//             Default names: FountainVFX, FountainSmallVFX, SkiSnowGunPlume, WaterVaporFactorySmallVFX.
//   swap    : {from: "SkiSnowGunPlume", to: "FountainVFX"} puts the "to" graph on the "from" VisualEffect (our plume instance only, so
//             park fountains are untouched) and re-binds InstanceData / Count. "restore" puts every swapped asset back.
//   set     : {on: "SkiSnowGunPlume", name, value} sets an exposed property on that VisualEffect: a number = float, [r,g,b,a] = Vector4,
//             [x,y,z] = Vector3, true/false = bool. Replies whether the graph has it.
//   tilt    : {prefabMatch: "SkiSnowGun", effect: "SkiSnowGunPlume", pitch, yaw, roll (deg)} sets m_Rotation of every Effect element
//             that points at that effect on every prefab whose name contains prefabMatch, and stamps EffectsUpdated on their placed
//             objects. {reset: true} puts the saved rotations back.
//   restore : swap and tilt back.
using System;
using System.Collections.Generic;
using Game.Common;
using Game.Prefabs;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace SkiTestBridge
{
    public partial class TestBridgeSystem
    {
        private readonly Dictionary<string, VisualEffectAsset> m_VfxSwapped = new Dictionary<string, VisualEffectAsset>();
        private readonly Dictionary<(Entity, int), quaternion> m_VfxTiltSaved = new Dictionary<(Entity, int), quaternion>();

        private JObject VfxProbeCommand(JObject args)
        {
            string action = (string)args["action"] ?? "list";
            switch (action)
            {
                case "list": return VfxList(args);
                case "swap": return VfxSwap((string)args["from"] ?? "SkiSnowGunPlume", (string)args["to"]);
                case "set": return VfxSet(args);
                case "tilt": return VfxTilt(args);
                case "restore":
                    {
                        JObject r = VfxRestoreSwaps();
                        r["tilt"] = VfxTiltReset();
                        return r;
                    }
                default: throw new ArgumentException("vfxprobe action: list, swap, set, tilt, restore");
            }
        }

        private static VisualEffect VfxFind(string prefabName)
        {
            GameObject go = GameObject.Find("VFX " + prefabName);
            return go != null ? go.GetComponent<VisualEffect>() : null;
        }

        private JObject VfxList(JObject args)
        {
            var names = new List<string>();
            if (args["names"] is JArray arr)
                foreach (JToken t in arr) names.Add((string)t);
            else
                names.AddRange(new[] { "FountainVFX", "FountainSmallVFX", "SkiSnowGunPlume", "WaterVaporFactorySmallVFX" });
            var rows = new JArray();
            foreach (string n in names)
            {
                VisualEffect ve = VfxFind(n);
                var row = new JObject { ["name"] = n, ["found"] = ve != null };
                if (ve != null)
                {
                    VisualEffectAsset asset = ve.visualEffectAsset;
                    row["asset"] = asset != null ? asset.name : null;
                    row["swapped"] = m_VfxSwapped.ContainsKey(n);
                    row["aliveParticles"] = ve.aliveParticleCount;
                    row["count"] = ve.HasInt("Count") ? ve.GetInt("Count") : -1;
                    var props = new JArray();
                    var events = new JArray();
                    if (asset != null)
                    {
                        var exposed = new List<VFXExposedProperty>();
                        asset.GetExposedProperties(exposed);
                        foreach (VFXExposedProperty p in exposed)
                            props.Add(new JObject { ["name"] = p.name, ["type"] = p.type != null ? p.type.Name : "?" });
                        var ev = new List<string>();
                        asset.GetEvents(ev);
                        foreach (string e in ev) events.Add(e);
                    }
                    row["exposed"] = props;
                    row["events"] = events;
                }
                rows.Add(row);
            }
            return new JObject { ["effects"] = rows };
        }

        private JObject VfxSwap(string from, string to)
        {
            VisualEffect target = VfxFind(from);
            VisualEffect source = VfxFind(to);
            if (target == null || source == null)
                throw new ArgumentException($"VisualEffect not found: {(target == null ? from : to)}");
            Texture instanceData = target.HasTexture("InstanceData") ? target.GetTexture("InstanceData") : null;
            int count = target.HasInt("Count") ? target.GetInt("Count") : 0;
            if (!m_VfxSwapped.ContainsKey(from))
                m_VfxSwapped[from] = target.visualEffectAsset;
            target.visualEffectAsset = source.visualEffectAsset;
            bool hasData = target.HasTexture("InstanceData");
            if (hasData && instanceData != null)
                target.SetTexture("InstanceData", instanceData);
            if (target.HasInt("Count"))
                target.SetInt("Count", count);
            target.Reinit();
            return new JObject
            {
                ["from"] = from, ["to"] = to, ["asset"] = target.visualEffectAsset.name,
                ["instanceDataBound"] = hasData && instanceData != null, ["count"] = count,
            };
        }

        private JObject VfxRestoreSwaps()
        {
            var done = new JArray();
            foreach (KeyValuePair<string, VisualEffectAsset> kv in m_VfxSwapped)
            {
                VisualEffect ve = VfxFind(kv.Key);
                if (ve == null)
                    continue;
                Texture instanceData = ve.HasTexture("InstanceData") ? ve.GetTexture("InstanceData") : null;
                int count = ve.HasInt("Count") ? ve.GetInt("Count") : 0;
                ve.visualEffectAsset = kv.Value;
                if (instanceData != null && ve.HasTexture("InstanceData"))
                    ve.SetTexture("InstanceData", instanceData);
                if (ve.HasInt("Count"))
                    ve.SetInt("Count", count);
                ve.Reinit();
                done.Add(kv.Key);
            }
            m_VfxSwapped.Clear();
            return new JObject { ["restored"] = done };
        }

        private JObject VfxSet(JObject args)
        {
            string on = (string)args["on"] ?? "SkiSnowGunPlume";
            string name = (string)args["name"];
            JToken v = args["value"];
            VisualEffect ve = VfxFind(on);
            if (ve == null)
                throw new ArgumentException("VisualEffect not found: " + on);
            bool has;
            if (v is JArray a && a.Count == 4)
            {
                has = ve.HasVector4(name);
                if (has) ve.SetVector4(name, new Vector4((float)a[0], (float)a[1], (float)a[2], (float)a[3]));
            }
            else if (v is JArray b && b.Count == 3)
            {
                has = ve.HasVector3(name);
                if (has) ve.SetVector3(name, new Vector3((float)b[0], (float)b[1], (float)b[2]));
            }
            else if (v != null && v.Type == JTokenType.Boolean)
            {
                has = ve.HasBool(name);
                if (has) ve.SetBool(name, (bool)v);
            }
            else
            {
                has = ve.HasFloat(name);
                if (has) ve.SetFloat(name, (float)v);
            }
            return new JObject { ["on"] = on, ["name"] = name, ["has"] = has };
        }

        private JObject VfxTilt(JObject args)
        {
            if ((bool?)args["reset"] == true)
                return new JObject { ["reset"] = VfxTiltReset() };
            string match = (string)args["prefabMatch"] ?? "SkiSnowGun";
            string effectName = (string)args["effect"] ?? "SkiSnowGunPlume";
            float pitch = (float?)args["pitch"] ?? 0f, yaw = (float?)args["yaw"] ?? 0f, roll = (float?)args["roll"] ?? 0f;
            Entity effect = FindPrefabEntity(effectName);
            quaternion rot = quaternion.EulerYXZ(math.radians(pitch), math.radians(yaw), math.radians(roll));
            RefreshPrefabNames();
            var touched = new JArray();
            var prefabSet = new HashSet<Entity>();
            foreach (KeyValuePair<string, Entity> kv in m_PrefabEntities)
            {
                if (kv.Key.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0 || !EntityManager.HasBuffer<Effect>(kv.Value))
                    continue;
                DynamicBuffer<Effect> effects = EntityManager.GetBuffer<Effect>(kv.Value);
                for (int i = 0; i < effects.Length; i++)
                {
                    Effect e = effects[i];
                    if (e.m_Effect != effect)
                        continue;
                    if (!m_VfxTiltSaved.ContainsKey((kv.Value, i)))
                        m_VfxTiltSaved[(kv.Value, i)] = e.m_Rotation;
                    e.m_Rotation = math.mul(m_VfxTiltSaved[(kv.Value, i)], rot);
                    effects[i] = e;
                    prefabSet.Add(kv.Value);
                    touched.Add(kv.Key + "#" + i);
                }
            }
            int stamped = VfxStampPlaced(prefabSet);
            return new JObject { ["entries"] = touched, ["placedStamped"] = stamped, ["pitch"] = pitch, ["yaw"] = yaw, ["roll"] = roll };
        }

        private JArray VfxTiltReset()
        {
            var done = new JArray();
            var prefabSet = new HashSet<Entity>();
            foreach (KeyValuePair<(Entity, int), quaternion> kv in m_VfxTiltSaved)
            {
                (Entity prefab, int i) = kv.Key;
                if (!EntityManager.Exists(prefab) || !EntityManager.HasBuffer<Effect>(prefab))
                    continue;
                DynamicBuffer<Effect> effects = EntityManager.GetBuffer<Effect>(prefab);
                if (i >= effects.Length)
                    continue;
                Effect e = effects[i];
                e.m_Rotation = kv.Value;
                effects[i] = e;
                prefabSet.Add(prefab);
                done.Add(prefab.Index + "#" + i);
            }
            m_VfxTiltSaved.Clear();
            VfxStampPlaced(prefabSet);
            return done;
        }

        /// <summary>Stamps EffectsUpdated on every placed object of those prefabs, so EffectControlSystem re-checks their effects.</summary>
        private int VfxStampPlaced(HashSet<Entity> prefabs)
        {
            if (prefabs.Count == 0)
                return 0;
            EntityQuery q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Object>());
            NativeArray<Entity> ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            try
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (!prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(ents[i]).m_Prefab))
                        continue;
                    if (!EntityManager.HasComponent<EffectsUpdated>(ents[i]))
                        EntityManager.AddComponent<EffectsUpdated>(ents[i]);
                    n++;
                }
            }
            finally
            {
                ents.Dispose();
            }
            return n;
        }
    }
}
