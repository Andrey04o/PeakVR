using System;
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
        Report("before reset");

        if (!AudioSettings.Reset(AudioSettings.GetConfiguration()))
        {
            Plugin.Log.LogWarning("[PeakVR][Audio] AudioSettings.Reset returned false — output device unchanged");
            return;
        }

        Report("after reset");
        Plugin.Log.LogInfo("[PeakVR][Audio] Audio output re-acquired (Unity re-reads the current Windows default device)");
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
