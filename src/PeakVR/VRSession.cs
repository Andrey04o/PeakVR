using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakVR;

internal static class VRSession
{
    private static bool controlsReady;

    public static bool Switching { get; private set; }

    public static bool StartAtBoot()
    {
        if (!LCVR.OpenXR.Loader.InitializeXR())
            return false;

        if (LCVR.OpenXR.GetActiveRuntimeName(out var name) &&
            LCVR.OpenXR.GetActiveRuntimeVersion(out var major, out var minor, out var patch))
            LCVR.Logger.LogInfo($"OpenXR runtime being used: {name} ({major}.{minor}.{patch})");
        else
            LCVR.Logger.LogError("Could not get OpenXR runtime info?");

        ApplyRenderSetup();
        return true;
    }

    public static bool Start()
    {
        if (Plugin.VrEnabled)
            return true;

        Switching = true;
        try
        {
            if (!LCVR.OpenXR.Loader.RestartXR())
            {
                Plugin.Log.LogError("[PeakVR][Mode] OpenXR did not start — staying in flat mode");
                return false;
            }

            Plugin.SetVrEnabled(true);

            ApplyRenderSetup();
            BuildRig();

            Plugin.Log.LogWarning("[PeakVR][Mode] VR mode is active");
            return true;
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"[PeakVR][Mode] switch to VR failed: {e}");
            return false;
        }
        finally
        {
            Switching = false;
        }
    }

    public static void Stop()
    {
        if (!Plugin.VrEnabled)
            return;

        Switching = true;
        try
        {
            Plugin.SetVrEnabled(false);

            TeardownRig();

            UIOverlay.RestoreForFlat();
            ForegroundUI.Shutdown();
            VRRender.RestoreForFlat();

            RenderDiagnostics.RestoreForFlat();
            VRFoliage.RestoreForFlat();
            VRHazardLayer.Restore();

            if (!LCVR.OpenXR.Loader.ShutdownXR())
                Plugin.Log.LogWarning("[PeakVR][Mode] no XR manager to stop");

            UrpDiagnostics.RestoreDepthPriming();

            Plugin.Log.LogWarning("[PeakVR][Mode] flat mode is active");
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"[PeakVR][Mode] switch to flat failed: {e}");
        }
        finally
        {
            Switching = false;
        }
    }

    private static void ApplyRenderSetup()
    {
        VRRender.DisableXRVisibilityMesh();

        UrpDiagnostics.ApplyDepthPriming();

        ForegroundUI.Apply();
        XRMirror.Setup();

        if (!controlsReady)
        {
            controlsReady = true;
            VRControls.Init();
        }

        VRArmIKPatch.LoadArmScaleFromConfig();
        VRRender.DisableBrokenAO();
        VRRender.ApplySharpening();
        VRRender.ApplyFarPlane();
        RenderDiagnostics.ApplyLodBias();
    }

    private static void BuildRig()
    {
        var scene = SceneManager.GetActiveScene().name;

        if (MainCamera.instance != null)
            MainCameraPatch.Build(MainCamera.instance, scene);

        var movement = Object.FindObjectOfType<MainCameraMovement>();
        if (movement != null)
            InGameCameraPatch.Build(movement);

        var menu = Object.FindObjectOfType<MainMenu>();
        if (menu != null)
            MenuCanvasPatch.Build(menu);

        StaminaBarRectPatch.Apply();
        MirrorPatch.Apply();
        VRFoveation.Apply();
        RenderDiagnostics.ScheduleScan();
        VRFoliage.ScheduleScan();
        VRHazardLayer.ScheduleScan();
    }

    private static void TeardownRig()
    {
        VRKeyboard.Close();
        VRModHandUI.ReleaseAll();
        StaminaBarRectPatch.Apply();
        MirrorPatch.Apply();
        FogQuadPatch.Restore();
        InGameCameraPatch.Teardown();
        MainCameraPatch.Teardown();
        MenuCanvasPatch.Restore();
        VRMenuPopup.RestoreAll();
        VRMenuManager.RestoreAll();
        VRModCanvas.ReleaseAll();
        VRHands.Destroy();
        VRLayers.RestoreAll();
        VRControllerVisibility.Clear();
        VRHeadRig.ResetState();

        VRPointer.Canvas = null;
        VRPointer.Raycaster = null;
        VRPointer.Target = null;
    }
}
