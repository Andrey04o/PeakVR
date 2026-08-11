using System.Diagnostics;
using System.Text;

namespace PeakVR;

internal static class VRProfile
{
    public const int Lod = 0;
    public const int StereoCull = 1;
    public const int HeadRig = 2;
    public const int Hud = 3;
    public const int HeadTilt = 4;
    public const int Foreground = 5;
    private const int Count = 6;

    private static readonly string[] Names = { "lod", "stereoCull", "headRig", "hud", "headTilt", "fgUI" };
    private static readonly long[] Ticks = new long[Count];

    public static bool Enabled;

    public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0L;

    public static void End(int id, long start)
    {
        if (start == 0L)
            return;

        Ticks[id] += Stopwatch.GetTimestamp() - start;
    }

    public static string Report(int frames)
    {
        if (frames <= 0)
            return string.Empty;

        var builder = new StringBuilder(" | mod:");
        double total = 0;

        for (var i = 0; i < Count; i++)
        {
            var ms = Ticks[i] * 1000.0 / Stopwatch.Frequency / frames;

            if (i != Lod)
                total += ms;

            builder.Append($" {Names[i]}={ms:F2}");
            if (i == Lod)
                builder.Append("(inside headRig)");
        }

        builder.Append($" TOTAL={total:F2}ms");
        return builder.ToString();
    }

    public static void Reset()
    {
        for (var i = 0; i < Count; i++)
            Ticks[i] = 0;
    }
}
