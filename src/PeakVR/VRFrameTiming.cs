using Unity.Profiling;
using UnityEngine;

namespace PeakVR;

internal static class VRFrameTiming
{
    private const float Interval = 2f;

    private static bool active;
    private static bool started;
    private static float nextLog;

    private static ProfilerRecorder mainThread;
    private static ProfilerRecorder renderThread;
    private static ProfilerRecorder drawCalls;
    private static ProfilerRecorder batches;
    private static ProfilerRecorder setPass;
    private static ProfilerRecorder triangles;
    private static ProfilerRecorder vertices;
    private static ProfilerRecorder gcAlloc;

    private static FrameTiming[] timings = new FrameTiming[1];

    private static int samples;
    private static double frameSum;
    private static double mainSum;
    private static double renderSum;
    private static double gpuSum;
    private static double presentWaitSum;
    private static long drawSum;
    private static long batchSum;
    private static long setPassSum;
    private static long triSum;
    private static long gcSum;

    private static readonly float[] Scales = { 1f, 0.75f, 0.5f, 0.35f };
    private static int scaleIndex;

    public static void CycleRenderScale()
    {
        scaleIndex = (scaleIndex + 1) % Scales.Length;
        UnityEngine.XR.XRSettings.renderViewportScale = Scales[scaleIndex];

        var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var prop = asset?.GetType().GetProperty("renderScale");
        var urp = prop != null ? ((float)prop.GetValue(asset)).ToString("F2") : "?";

        Plugin.Log.LogInfo($"[PeakVR][Timing] renderViewportScale -> {UnityEngine.XR.XRSettings.renderViewportScale:F2} " +
            $"(requested {Scales[scaleIndex]:F2}, urpAssetRenderScale={urp} which URP ignores under XR) " +
            $"eyeTex={UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight} " +
            "— if fps scales with this, the graphics card is the limit; if not, the processor is");
    }

    public static void Toggle()
    {
        if (active)
        {
            Stop();
            return;
        }

        Start();
    }

    private static void Start()
    {
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
        renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread");
        drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

        started = true;
        active = true;
        VRProfile.Enabled = true;
        Reset();
        nextLog = Time.unscaledTime + Interval;

        Plugin.Log.LogInfo("[PeakVR][Timing] started — recorders valid: " +
            $"main={mainThread.Valid} render={renderThread.Valid} draws={drawCalls.Valid} " +
            $"batches={batches.Valid} tris={triangles.Valid} gc={gcAlloc.Valid}");
    }

    private static void Stop()
    {
        if (started)
        {
            mainThread.Dispose();
            renderThread.Dispose();
            drawCalls.Dispose();
            batches.Dispose();
            setPass.Dispose();
            triangles.Dispose();
            vertices.Dispose();
            gcAlloc.Dispose();
            started = false;
        }

        active = false;
        VRProfile.Enabled = false;
        Plugin.Log.LogInfo("[PeakVR][Timing] stopped");
    }

    public static void Tick()
    {
        if (!active)
            return;

        frameSum += Time.unscaledDeltaTime * 1000.0;

        if (mainThread.Valid)
            mainSum += mainThread.LastValue * 1e-6;
        if (renderThread.Valid)
            renderSum += renderThread.LastValue * 1e-6;
        if (drawCalls.Valid)
            drawSum += drawCalls.LastValue;
        if (batches.Valid)
            batchSum += batches.LastValue;
        if (setPass.Valid)
            setPassSum += setPass.LastValue;
        if (triangles.Valid)
            triSum += triangles.LastValue;
        if (gcAlloc.Valid)
            gcSum += gcAlloc.LastValue;

        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
        {
            gpuSum += timings[0].gpuFrameTime;
            presentWaitSum += timings[0].cpuMainThreadPresentWaitTime;
        }

        samples++;

        if (Time.unscaledTime < nextLog)
            return;

        nextLog = Time.unscaledTime + Interval;
        Log();
        Reset();
    }

    private static void Log()
    {
        if (samples == 0)
            return;

        var n = (double)samples;
        var frame = frameSum / n;

        Plugin.Log.LogInfo(
            $"[PeakVR][Timing] fps={1000.0 / frame:F1} frame={frame:F2}ms | " +
            $"main={mainSum / n:F2}ms render={renderSum / n:F2}ms gpu={gpuSum / n:F2}ms " +
            $"presentWait={presentWaitSum / n:F2}ms | " +
            $"draws={drawSum / samples} batches={batchSum / samples} setPass={setPassSum / samples} " +
            $"tris={triSum / samples / 1000}k gcAlloc={gcSum / samples / 1024}KB " +
            $"| viewportScale={UnityEngine.XR.XRSettings.renderViewportScale:F2} " +
            $"eyeTex={UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}" +
            VRProfile.Report(samples));
    }

    private static void Reset()
    {
        VRProfile.Reset();
        samples = 0;
        frameSum = 0;
        mainSum = 0;
        renderSum = 0;
        gpuSum = 0;
        presentWaitSum = 0;
        drawSum = 0;
        batchSum = 0;
        setPassSum = 0;
        triSum = 0;
        gcSum = 0;
    }
}
