using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakVR;

internal static class VRSession
{
    private static bool urpApplied;
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
            // The flag goes first so nothing that survives the rig (VRMenuPopup on the main menu, VRHud
            // on the GUIManager) re-converts a canvas we are in the middle of putting back.
            Plugin.SetVrEnabled(false);

            TeardownRig();

            ForegroundUI.Shutdown();
            VRRender.RestoreForFlat();

            // These are driven from VRHeadRig, which the rig teardown just destroyed — so anything they
            // forced would stay forced forever with nothing left to tick it back.
            RenderDiagnostics.RestoreForFlat();
            VRFoliage.RestoreForFlat();
            VRHazardLayer.Restore();

            if (!LCVR.OpenXR.Loader.ShutdownXR())
                Plugin.Log.LogWarning("[PeakVR][Mode] no XR manager to stop");

            // After the XR teardown: this rebuilds the URP renderers, which is exactly what freezes an
            // installed desktop mirror — by now there is none.
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

        // Both reinitialise the GPU Resident Drawer, which tears the XR system down and freezes an
        // already-installed desktop mirror — hence once per process, and always before XRMirror.Setup().
        // Neither is visible in flat mode (they only relax culling), so they are not reverted on the way
        // out; doing so would mean a drawer rebuild on every single switch.
        if (!urpApplied)
        {
            urpApplied = true;
            UrpDiagnostics.ApplySmallMeshCulling();
            UrpDiagnostics.ApplyGpuOcclusionCulling();
        }

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

        VRFoveation.Apply();
        RenderDiagnostics.ScheduleScan();
        VRFoliage.ScheduleScan();
        VRHazardLayer.ScheduleScan();
    }

    // Order matters: elements borrowed from other mods' canvases live inside the wrist HUD, so they have
    // to go home before the rig that owns it is destroyed, or they die with it and the owning mod is
    // left holding dead references.
    private static void TeardownRig()
    {
        VRModHandUI.ReleaseAll();
        FogQuadPatch.Restore();
        InGameCameraPatch.Teardown();
        MainCameraPatch.Teardown();
        MenuCanvasPatch.Restore();
        VRMenuPopup.RestoreAll();
        VRModCanvas.ReleaseAll();
        VRHands.Destroy();
        VRControllerVisibility.Clear();
        VRHeadRig.ResetState();

        VRPointer.Canvas = null;
        VRPointer.Raycaster = null;
        VRPointer.Target = null;
    }
}
