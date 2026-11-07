// The "needs a lift line" notification icon prefab (SkiLiftLineNeeded), made in code. It is a NotificationIconPrefab whose m_Icon texture
// matches the vanilla icons exactly, plus a UIObject naming the SVG that the info panel's Notifications section shows. LiftLineIconSystem
// uses it and registers it in OnGamePreload (before the save's prefab list is resolved, GameManager.cs:1021-1028), so a saved icon finds
// its prefab on load.
// Why the texture must match: NotificationIconRenderSystem builds ONE Texture2DArray at the largest icon size, in the format of the last
// icon it reads. It then copies every icon into it with Graphics.CopyTexture (NotificationIconRenderSystem.cs:207-245). So the code reads
// the size, mip count and format from the vanilla icons at runtime. It resamples the PNG and block-encodes it to them (IconTextureCodec).
// When the vanilla icons do not share one size and format, or the format is one we cannot write, nothing is registered (Register returns
// false and says why). The caller then leaves the vanilla icons alone.
// Files: Icons/SkiLiftLineNeeded.png (the world icon's pixels, rendered from the SVG) and Icons/SkiLiftLineNeeded.svg, deployed to
// <mod>/SkiResortMod-icons/ (SkiResortMod.csproj). "coui://ui-mods/" serves every UI mod's folder (ModManager.cs:468-479).
// Engine notes: EngineNotes/LiftLineIconSystem.md

using System;
using System.Collections.Generic;
using System.IO;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace SkiResortMod.Systems
{
    internal static class LiftLineIconAsset
    {
        internal const string PrefabName = "SkiLiftLineNeeded";
        internal const string IconFolder = "SkiResortMod-icons";
        internal const string PngFile = "SkiLiftLineNeeded.png";
        internal const string UiIcon = "coui://ui-mods/" + IconFolder + "/SkiLiftLineNeeded.svg";

        /// <summary>The registered prefab's entity, or Entity.Null. Returns the existing one when an earlier load registered it.</summary>
        internal static Entity Find(PrefabSystem prefabs)
        {
            if (prefabs.TryGetPrefab(new PrefabID(nameof(NotificationIconPrefab), PrefabName), out PrefabBase p) && p != null &&
                prefabs.TryGetEntity(p, out Entity e))
                return e;
            return Entity.Null;
        }

        /// <summary>Builds the texture and adds the prefab. <paramref name="report"/> is one log sentence either way.</summary>
        internal static bool Register(PrefabSystem prefabs, EntityManager em, Entity referenceIcon, out string report)
        {
            if (Find(prefabs) != Entity.Null)
            {
                report = $"{PrefabName} already registered this session";
                return true;
            }
            if (!TryReadVanillaShape(prefabs, em, referenceIcon, out IconShape shape, out NotificationIconPrefab reference, out report))
                return false;
            IconTexFormat fmt = Map(shape.Format);
            if (fmt == IconTexFormat.Unsupported)
            {
                report = $"vanilla icon format {shape.Format} is not one we can write ({shape.Describe()}); {PrefabName} not registered";
                return false;
            }
            string root = Mod.ModRootPath;
            string path = root != null ? Path.Combine(root, IconFolder, PngFile) : null;
            if (path == null || !File.Exists(path))
            {
                report = $"{IconFolder}/{PngFile} not found beside the mod DLL; {PrefabName} not registered";
                return false;
            }
            Texture2D icon = Build(File.ReadAllBytes(path), shape, fmt, out string built);
            if (icon == null)
            {
                report = built + $"; {PrefabName} not registered";
                return false;
            }
            var prefab = ScriptableObject.CreateInstance<NotificationIconPrefab>();
            prefab.name = PrefabName;
            prefab.m_Icon = icon;
            if (reference != null)
            {
                prefab.m_DisplaySize = reference.m_DisplaySize;
                prefab.m_PulsateAmplitude = reference.m_PulsateAmplitude;
            }
            UIObject ui = prefab.AddComponent<UIObject>();
            ui.m_Icon = UiIcon;
            if (!prefabs.AddPrefab(prefab))
            {
                UnityEngine.Object.Destroy(icon);
                report = $"PrefabSystem.AddPrefab rejected {PrefabName}";
                return false;
            }
            report = $"registered {PrefabName}: {built}; matches the vanilla icons ({shape.Describe()}), UI icon {UiIcon}";
            return true;
        }

        /// <summary>One icon texture's shape.</summary>
        internal struct IconShape
        {
            public int Width, Height, Mips;
            public TextureFormat Format;
            public int Count;
            public string Describe() => $"{Width} x {Height}, {Format}, {Mips} mips, {Count} icon prefabs";
        }

        /// <summary>The shape every vanilla icon shares. Returns false (with the spread) when they differ or none is readable.</summary>
        private static bool TryReadVanillaShape(PrefabSystem prefabs, EntityManager em, Entity referenceIcon, out IconShape shape,
                                                out NotificationIconPrefab reference, out string report)
        {
            shape = default;
            reference = null;
            var seen = new Dictionary<string, int>();
            int total = 0;
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<NotificationIconData>(), ComponentType.ReadOnly<PrefabData>()))
            using (NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!prefabs.TryGetPrefab(em.GetComponentData<PrefabData>(entities[i]), out PrefabBase pb) ||
                        !(pb is NotificationIconPrefab np) || np.m_Icon == null || np.name == PrefabName)
                        continue;
                    Texture2D t = np.m_Icon;
                    var s = new IconShape { Width = t.width, Height = t.height, Mips = t.mipmapCount, Format = t.format };
                    string key = $"{s.Width}x{s.Height}/{s.Format}/{s.Mips}";
                    seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (total == 0 || entities[i] == referenceIcon)
                    {
                        shape = s;
                        if (entities[i] == referenceIcon || reference == null)
                            reference = np;
                    }
                    total++;
                }
            }
            if (total == 0)
            {
                report = "no vanilla notification icon texture found";
                return false;
            }
            if (seen.Count != 1)
            {
                var parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in seen)
                    parts.Add($"{kv.Key} x{kv.Value}");
                report = $"vanilla icon textures differ ({string.Join(", ", parts.ToArray())}); an icon of either shape could break the " +
                         "shared texture array";
                return false;
            }
            shape.Count = total;
            report = shape.Describe();
            return true;
        }

        private static IconTexFormat Map(TextureFormat f)
        {
            switch (f)
            {
                case TextureFormat.RGBA32: return IconTexFormat.RGBA32;
                case TextureFormat.ARGB32: return IconTexFormat.ARGB32;
                case TextureFormat.DXT5: return IconTexFormat.DXT5;
                case TextureFormat.BC7: return IconTexFormat.BC7;
                default: return IconTexFormat.Unsupported;
            }
        }

        /// <summary>The PNG resampled to the vanilla size, every mip encoded to the vanilla format, uploaded and made GPU-only.</summary>
        private static Texture2D Build(byte[] png, IconShape shape, IconTexFormat fmt, out string report)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(src, png, false))
                {
                    report = $"{PngFile} did not decode";
                    return null;
                }
                Color32[] pixels = src.GetPixels32();
                var rgba = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                {
                    rgba[i * 4] = pixels[i].r;
                    rgba[i * 4 + 1] = pixels[i].g;
                    rgba[i * 4 + 2] = pixels[i].b;
                    rgba[i * 4 + 3] = pixels[i].a;
                }
                byte[] top = src.width == shape.Width && src.height == shape.Height
                    ? rgba
                    : IconTextureCodec.Resize(rgba, src.width, src.height, shape.Width, shape.Height);
                List<byte[]> levels = IconTextureCodec.MipChain(top, shape.Width, shape.Height, shape.Mips);
                var tex = new Texture2D(shape.Width, shape.Height, shape.Format, shape.Mips, false)
                {
                    name = PrefabName,
                };
                int w = shape.Width, h = shape.Height;
                for (int m = 0; m < levels.Count; m++)
                {
                    byte[] data = IconTextureCodec.EncodeLevel(fmt, levels[m], w, h);
                    tex.SetPixelData(data, m);
                    w = Math.Max(1, w >> 1);
                    h = Math.Max(1, h >> 1);
                }
                tex.Apply(false, true);
                report = $"{PngFile} {src.width} x {src.height} -> {tex.width} x {tex.height} {tex.format}, {tex.mipmapCount} mips";
                return tex;
            }
            catch (Exception e)
            {
                report = $"building the icon texture threw {e.GetType().Name}: {e.Message}";
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(src);
            }
        }
    }
}
