using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace PeakVR;

internal static class VRLogBuffer
{
    private const int MaxLines = 100;
    private const int MaxLineLength = 150;

    private static readonly List<string> Lines = new();
    private static bool hooked;

    public static int Version { get; private set; }

    public static int Count => Lines.Count;

    public static void Start()
    {
        if (hooked)
            return;

        hooked = true;

        try
        {
            BepInEx.Logging.Logger.Listeners.Add(new Listener());
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Could not capture the OpenXR log: {e.Message}");
        }
    }

    public static string Window(int first, int count)
    {
        if (Lines.Count == 0)
            return "No OpenXR messages captured yet.";

        var from = Mathf.Clamp(first, 0, Math.Max(0, Lines.Count - 1));
        var to = Math.Min(Lines.Count, from + count);

        var text = new StringBuilder();
        for (var i = from; i < to; i++)
            text.AppendLine(Lines[i]);

        return text.ToString().TrimEnd();
    }

    private static void Add(string source, string message, LogLevel level)
    {
        if (string.IsNullOrEmpty(message) || !IsInteresting(source, message, level))
            return;

        var line = message.Replace('\n', ' ').Replace('\r', ' ');
        if (line.Length > MaxLineLength)
            line = line.Substring(0, MaxLineLength - 3) + "...";

        // Rich text rather than a flat colour, so an error stands out from the noise around it.
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        Lines.Add($"<color=#6f7784>{stamp}</color> <color={Tint(level)}>{line}</color>");

        while (Lines.Count > MaxLines)
            Lines.RemoveAt(0);

        Version++;
    }

    private static string Tint(LogLevel level)
    {
        if ((level & (LogLevel.Error | LogLevel.Fatal)) != 0)
            return "#ff6b5e";

        if ((level & LogLevel.Warning) != 0)
            return "#ffd166";

        if ((level & LogLevel.Debug) != 0)
            return "#7f8c8d";

        return "#a9d6a4";
    }

    private static bool IsInteresting(string source, string message, LogLevel level)
    {
        if (source != null && source.IndexOf("OpenXR", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (message.IndexOf("OpenXR", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("XR_", StringComparison.Ordinal) >= 0
            || message.IndexOf("[XR]", StringComparison.Ordinal) >= 0
            || message.IndexOf("xrCreate", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return (level & (LogLevel.Error | LogLevel.Fatal)) != 0
            && message.IndexOf("subsystem", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private class Listener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.All;

        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            Add(eventArgs.Source?.SourceName, eventArgs.Data?.ToString(), eventArgs.Level);
        }

        public void Dispose()
        {
        }
    }
}
