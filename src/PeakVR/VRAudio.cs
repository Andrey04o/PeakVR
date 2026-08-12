using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PeakVR;

internal static class VRAudio
{
    private static bool reacquired;
    private static bool subscribed;

    public static void Subscribe()
    {
        if (subscribed)
            return;

        subscribed = true;
        AudioSettings.OnAudioConfigurationChanged += OnConfigurationChanged;
    }

    private static void OnConfigurationChanged(bool deviceWasChanged)
    {
        Plugin.Log.LogInfo($"[PeakVR][Audio] configuration changed (deviceWasChanged={deviceWasChanged})");
        Report("on change");

        if (!deviceWasChanged)
            return;

        reacquired = false;
        ReacquireOutputDevice();
    }

    public static void Report(string when)
    {
        var config = AudioSettings.GetConfiguration();

        Plugin.Log.LogInfo($"[PeakVR][Audio] {when}: listenerVolume={AudioListener.volume:F2} " +
            $"paused={AudioListener.pause} listeners={UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length} " +
            $"speakerMode={config.speakerMode} driverCapabilities={AudioSettings.driverCapabilities} " +
            $"spatialExperience={AudioSettings.audioSpatialExperience} " +
            $"sampleRate={config.sampleRate} outputSampleRate={AudioSettings.outputSampleRate} " +
            $"dspBufferSize={config.dspBufferSize} numRealVoices={config.numRealVoices} " +
            $"numVirtualVoices={config.numVirtualVoices}");

        Plugin.Log.LogInfo($"[PeakVR][Audio] {when}: master={Setting("MasterVolumeSetting")} " +
            $"music={Setting("MusicVolumeSetting")} sfx={Setting("SFXVolumeSetting")} " +
            $"voice={Setting("VoiceVolumeSetting")}");
    }

    public static void ReacquireOutputDevice()
    {
        if (reacquired || Plugin.Config == null || !Plugin.Config.ReacquireAudioDevice.Value)
            return;

        reacquired = true;
        Reload();
    }

    public static void WatchSetting()
    {
        if (Plugin.Config == null)
            return;

        Plugin.Config.ReacquireAudioDevice.SettingChanged += (_, _) =>
        {
            if (!Plugin.Config.ReacquireAudioDevice.Value)
                return;

            Plugin.Log.LogInfo("[PeakVR][Audio] Reacquire toggled on — reloading the audio output now");
            reacquired = true;
            Reload();
        };
    }

    private static void Reload()
    {
        Report("before reset");

        var resume = Capture();

        if (!AudioSettings.Reset(AudioSettings.GetConfiguration()))
        {
            Plugin.Log.LogWarning("[PeakVR][Audio] AudioSettings.Reset returned false — output device unchanged");
            return;
        }

        var restarted = Restore(resume);

        Report("after reset");
        Plugin.Log.LogInfo($"[PeakVR][Audio] Audio output re-acquired, resumed {restarted}/{resume.Count} playing source(s)");
    }

    private static List<(AudioSource source, float time, bool loop)> Capture()
    {
        var playing = new List<(AudioSource, float, bool)>();

        foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (source == null || !source.isPlaying || source.clip == null)
                continue;

            playing.Add((source, source.time, source.loop));
        }

        return playing;
    }

    private static int Restore(List<(AudioSource source, float time, bool loop)> playing)
    {
        var restarted = 0;

        foreach (var (source, time, loop) in playing)
        {
            if (source == null || source.clip == null || source.isPlaying)
                continue;

            try
            {
                source.time = Mathf.Clamp(time, 0f, Mathf.Max(0f, source.clip.length - 0.01f));
                source.loop = loop;
                source.Play();
                restarted++;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[PeakVR][Audio] Could not resume '{source.name}': {e.Message}");
            }
        }

        return restarted;
    }

    public static void Reset() => reacquired = false;

    private static string Setting(string typeName)
    {
        try
        {
            var handler = GameHandler.Instance != null ? GameHandler.Instance.SettingsHandler : null;
            if (handler == null)
                return "n/a";

            var type = AccessTools.TypeByName(typeName);
            var method = handler.GetType().GetMethod("GetSetting");
            if (type == null || method == null)
                return "n/a";

            var setting = method.MakeGenericMethod(type).Invoke(handler, null);
            if (setting == null)
                return "null";

            var value = setting.GetType().GetProperty("Value");
            return value != null ? Convert.ToString(value.GetValue(setting)) : "?";
        }
        catch (Exception e)
        {
            return $"err({e.GetType().Name})";
        }
    }
}
