using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRSmokeShader
{
    private enum Variant
    {
        Original,
        GameSimple,
        PeakVrCustom,
        PeakVrCustom2,
    }

    private class Entry
    {
        public ParticleSystemRenderer Renderer;
        public Material Original;
        public Material Simple;
        public Material Custom;
        public Material Custom2;
        public ParticleSystemSortMode OriginalSort;
        public List<ParticleSystemVertexStream> OriginalStreams;
    }

    private static readonly List<Entry> Entries = new();
    private static Variant current = Variant.Original;

    private static readonly List<ParticleSystemVertexStream> CenterStreams = new()
    {
        ParticleSystemVertexStream.Position,
        ParticleSystemVertexStream.Normal,
        ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.Center,
    };

    private static readonly string[] Probe =
    {
        "_Distortion", "_DistortionScale", "_DepthFadeDistance", "_ShadowStr",
        "_SunGlowLighting", "_Opacity", "_TextureInfluence", "_Roundness",
        "_RoundnessExp", "_TextureRotateSpeed",
    };

    private static readonly string[] ProbeColors =
    {
        "_Color", "_Color2", "_Color3", "_EmissionColor",
    };

    private static readonly string[] ProbeVectors =
    {
        "_AlphaRemap", "_FinalAlphaRemap", "_Color2Remap", "_Color3Remap", "_TexScroll", "_DistortionScroll",
    };

    public static void Dump()
    {
        var systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
        Plugin.Log.LogInfo($"[PeakVR][Smoke] ===== {systems.Length} particle systems in scene =====");

        var shown = 0;
        foreach (var system in systems)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                continue;

            var material = renderer.sharedMaterial;
            var shaderName = material != null && material.shader != null ? material.shader.name : "?";

            var interesting = system.name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("fire", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!interesting || shown++ > 25)
                continue;

            var main = system.main;
            Plugin.Log.LogInfo(
                $"[PeakVR][Smoke] {Path(system.transform)} | mat={(material == null ? "null" : material.name)} " +
                $"shader='{shaderName}' max={main.maxParticles} alive={system.particleCount} " +
                $"size={main.startSize.constant:F2} life={main.startLifetime.constant:F1}s " +
                $"queue={(material == null ? -1 : material.renderQueue)} align={renderer.alignment} mode={renderer.renderMode}");

            if (material == null)
                continue;

            var properties = string.Empty;
            foreach (var name in Probe)
                if (material.HasProperty(name))
                    properties += $" {name}={material.GetFloat(name):F3}";

            if (properties.Length > 0)
                Plugin.Log.LogInfo($"[PeakVR][Smoke]      floats:{properties}");

            var colors = string.Empty;
            foreach (var name in ProbeColors)
                if (material.HasProperty(name))
                    colors += $" {name}={material.GetColor(name)}";

            if (colors.Length > 0)
                Plugin.Log.LogInfo($"[PeakVR][Smoke]      colors:{colors}");

            var vectors = string.Empty;
            foreach (var name in ProbeVectors)
                if (material.HasProperty(name))
                    vectors += $" {name}={material.GetVector(name)}";

            if (vectors.Length > 0)
                Plugin.Log.LogInfo($"[PeakVR][Smoke]      vectors:{vectors}");

            var col = system.colorOverLifetime;
            Plugin.Log.LogInfo($"[PeakVR][Smoke]      startColor={main.startColor.color} mode={main.startColor.mode} " +
                $"colorOverLifetime={(col.enabled ? col.color.mode.ToString() : "off")} " +
                $"renderAlignment={renderer.alignment} sortMode={renderer.sortMode} " +
                $"matInstancing={material?.enableInstancing} rendererGpuInstancing={renderer.enableGPUInstancing} " +
                $"mesh={(renderer.renderMode == ParticleSystemRenderMode.Mesh ? "yes" : "no")}");

            var tex = material.HasProperty("_Textureu") ? material.GetTexture("_Textureu") : null;
            Plugin.Log.LogInfo($"[PeakVR][Smoke]      tex _Textureu={(tex == null ? "null" : tex.name)} " +
                $"keywords=[{string.Join(",", material.shaderKeywords)}]");
        }

        Plugin.Log.LogInfo($"[PeakVR][Smoke] game simple shader found={FindShader("SmokeParticleSimple") != null} " +
            $"peakvr custom found={PeakAssets.SimpleSmoke != null} " +
            $"peakvr custom2 found={PeakAssets.SimpleSmoke2 != null} | current variant={current}");
    }

    public static void Cycle()
    {
        Collect();

        if (Entries.Count == 0)
        {
            Plugin.Log.LogWarning("[PeakVR][Smoke] no smoke particle systems found");
            return;
        }

        current = (Variant)(((int)current + 1) % 4);

        var applied = 0;
        foreach (var entry in Entries)
        {
            if (entry.Renderer == null)
                continue;

            var material = Pick(entry, current);
            if (material == null)
                continue;

            entry.Renderer.sharedMaterial = material;
            entry.Renderer.sortMode = current == Variant.Original
                ? entry.OriginalSort
                : ParticleSystemSortMode.Distance;

            if (current == Variant.PeakVrCustom2)
                entry.Renderer.SetActiveVertexStreams(CenterStreams);
            else if (entry.OriginalStreams != null)
                entry.Renderer.SetActiveVertexStreams(entry.OriginalStreams);

            applied++;
        }

        Plugin.Log.LogInfo($"[PeakVR][Smoke] variant -> {current} (applied to {applied}/{Entries.Count})");

        var sample = Pick(Entries[0], current);
        if (sample != null && (current == Variant.PeakVrCustom || current == Variant.PeakVrCustom2))
        {
            var map = sample.HasProperty("_BaseMap") ? sample.GetTexture("_BaseMap") : null;
            var state = $"[PeakVR][Smoke]   custom: tex={(map == null ? "NULL (shape will be circles!)" : map.name)} " +
                $"roundness={sample.GetFloat("_Roundness"):F2} exp={sample.GetFloat("_RoundnessExp"):F2} " +
                $"noiseShape={sample.GetFloat("_TextureInfluence"):F2} remap={sample.GetVector("_AlphaRemap")} " +
                $"alphaScale={sample.GetFloat("_AlphaScale"):F2} sunGlow={sample.GetFloat("_SunGlowLighting"):F2}";

            if (current == Variant.PeakVrCustom2)
                state += $" boost={sample.GetFloat("_ColorBoost"):F2} gradScale={sample.GetFloat("_GradientScale"):F2} " +
                    $"world={sample.GetFloat("_WorldGradient"):F2} packing={sample.GetFloat("_CenterPacking"):F2} " +
                    $"box={sample.GetFloat("_BoxMask"):F2} colorTex={sample.GetFloat("_ColorTexture"):F2} " +
                    $"clip={sample.GetFloat("_AlphaClip"):F3}";

            Plugin.Log.LogInfo(state);
        }
    }

    private static Material Pick(Entry entry, Variant variant) => variant switch
    {
        Variant.GameSimple => entry.Simple,
        Variant.PeakVrCustom => entry.Custom,
        Variant.PeakVrCustom2 => entry.Custom2,
        _ => entry.Original,
    };

    private static readonly float[] AlphaScales = { 1f, 0.58f, 0.8f, 0.6f, 0.45f, 0.3f };
    private static readonly float[] TextureInfluences = { 0.85f, 0.615f, 1f, 0.65f, 0.45f };
    private static readonly float[] Roundnesses = { 1.8f, 2.05f, 2.5f, 3.5f, 1.12f };
    private static readonly float[] SunGlows = { 1f, 0f, 1.5f, 2f, 0.5f };
    private static int alphaIndex;
    private static int textureIndex;
    private static int roundIndex;
    private static int sunIndex;

    public static void TuneRoundness()
    {
        roundIndex = (roundIndex + 1) % Roundnesses.Length;
        ApplyTuning();
        Plugin.Log.LogInfo($"[PeakVR][Smoke] _Roundness -> {Roundnesses[roundIndex]:F2} (higher = fuller, bigger-looking puffs)");
    }

    public static void TuneSunGlow()
    {
        sunIndex = (sunIndex + 1) % SunGlows.Length;
        ApplyTuning();
        Plugin.Log.LogInfo($"[PeakVR][Smoke] _SunGlowLighting -> {SunGlows[sunIndex]:F2} (tints smoke toward the sun colour)");
    }

    public static void TuneAlpha()
    {
        alphaIndex = (alphaIndex + 1) % AlphaScales.Length;
        ApplyTuning();
        Plugin.Log.LogInfo($"[PeakVR][Smoke] _AlphaScale -> {AlphaScales[alphaIndex]:F2}");
    }

    public static void TuneTexture()
    {
        textureIndex = (textureIndex + 1) % TextureInfluences.Length;
        ApplyTuning();
        Plugin.Log.LogInfo($"[PeakVR][Smoke] _TextureInfluence -> {TextureInfluences[textureIndex]:F2}");
    }

    private static void ApplyTuning()
    {
        if (current != Variant.PeakVrCustom && current != Variant.PeakVrCustom2)
        {
            Plugin.Log.LogWarning($"[PeakVR][Smoke] tuning ignored — variant is {current}, press I until it is a PeakVR one");
            return;
        }

        foreach (var entry in Entries)
        {
            var material = Pick(entry, current);
            if (material == null)
                continue;

            if (material.HasProperty("_AlphaScale"))
                material.SetFloat("_AlphaScale", AlphaScales[alphaIndex]);
            if (material.HasProperty("_TextureInfluence"))
                material.SetFloat("_TextureInfluence", TextureInfluences[textureIndex]);
            if (material.HasProperty("_Roundness"))
                material.SetFloat("_Roundness", Roundnesses[roundIndex]);
            if (material.HasProperty("_SunGlowLighting"))
                material.SetFloat("_SunGlowLighting", SunGlows[sunIndex]);
        }
    }

    private static void Collect()
    {
        Entries.RemoveAll(e => e.Renderer == null);

        var simpleShader = FindShader("SmokeParticleSimple");
        var customShader = PeakAssets.SimpleSmoke;
        var customShader2 = PeakAssets.SimpleSmoke2;

        foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || renderer.sharedMaterial == null)
                continue;

            var shader = renderer.sharedMaterial.shader;
            if (shader == null || shader.name.IndexOf("SmokeParticle", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (Entries.Exists(e => e.Renderer == renderer))
                continue;

            var original = renderer.sharedMaterial;
            var streams = new List<ParticleSystemVertexStream>();
            renderer.GetActiveVertexStreams(streams);

            var entry = new Entry
            {
                Renderer = renderer,
                Original = original,
                OriginalSort = renderer.sortMode,
                OriginalStreams = streams,
            };

            if (simpleShader != null && shader.name != "SmokeParticleSimple")
            {
                entry.Simple = new Material(original) { name = original.name + " (simple)" };
                entry.Simple.shader = simpleShader;
            }
            else
            {
                entry.Simple = original;
            }

            if (customShader != null)
            {
                entry.Custom = new Material(customShader) { name = original.name + " (peakvr)" };
                CopyTexture(original, entry.Custom);
                CopyFloats(original, entry.Custom);
                entry.Custom.renderQueue = original.renderQueue;
            }

            if (customShader2 != null)
            {
                entry.Custom2 = new Material(customShader2) { name = original.name + " (peakvr2)" };
                CopyTexture(original, entry.Custom2);
                CopyFloats(original, entry.Custom2);
                entry.Custom2.renderQueue = original.renderQueue;
            }

            Entries.Add(entry);
        }
    }

    private static void CopyFloats(Material from, Material to)
    {
        string[] names = { "_Opacity" };

        foreach (var name in names)
            if (from.HasProperty(name) && to.HasProperty(name))
                to.SetFloat(name, from.GetFloat(name));

        string[] vectors = { "_TexScroll" };
        foreach (var name in vectors)
            if (from.HasProperty(name) && to.HasProperty(name))
                to.SetVector(name, from.GetVector(name));

    }

    private static void CopyTexture(Material from, Material to)
    {
        string[] sources = { "_Textureu", "_BaseMap", "_MainTex" };
        Texture texture = null;

        foreach (var name in sources)
        {
            if (!from.HasProperty(name))
                continue;

            texture = from.GetTexture(name);
            if (texture != null)
                break;
        }

        if (texture == null)
            return;

        if (to.HasProperty("_BaseMap"))
            to.SetTexture("_BaseMap", texture);
        if (to.HasProperty("_MainTex"))
            to.SetTexture("_MainTex", texture);
    }

    private static Shader FindShader(string name)
    {
        var shader = Shader.Find(name);
        if (shader != null)
            return shader;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<Shader>())
            if (candidate != null && candidate.name == name)
                return candidate;

        return null;
    }

    private static string Path(Transform t)
    {
        var path = t.name;
        var parent = t.parent;
        var depth = 0;
        while (parent != null && depth++ < 4)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
