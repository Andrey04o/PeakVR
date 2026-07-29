using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[DefaultExecutionOrder(1150)]
internal class VRBinoculars : MonoBehaviour
{
    private const float Distance = 0.3f;
    private const float Size = 0.8f;
    private const float EyeOffset = 0.06f;
    private const float ItemForward = 0.06f;
    private const float ItemUp = 0f;
    private const float SmoothRate = 8f;

    private bool smoothing;
    private Vector3 smoothPos;
    private Quaternion smoothRot = Quaternion.identity;
    private const int RenderQueue = 4000;
    private const int TextureSize = 1024;
    private const float MinFov = 1f;
    private const float MaxFov = 120f;

    private Camera mainCam;
    private Camera scopeCam;
    private RenderTexture target;
    private MeshRenderer quad;
    private Material mat;

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled)
            return;

        var over = ActiveOverride();
        if (over == null)
        {
            SetActive(false);
            smoothing = false;
            return;
        }

        if (!Build())
            return;

        SetActive(true);
        ResolveFeature();

        var hand = VRHands.Right;
        var aim = AimTransform();

        if (aim != null && hand != null)
        {
            Vector3 wantPos;
            Quaternion wantRot;

            if (hasRig)
            {
                wantRot = hand.rotation * Quaternion.Inverse(gripRot);
                wantPos = hand.position - wantRot * gripPos;
            }
            else
            {
                wantPos = hand.position + hand.forward * ItemForward + hand.up * ItemUp;
                wantRot = hand.rotation;
            }

            var frame = transform.parent != null ? transform.parent : transform;
            var localWantPos = frame.InverseTransformPoint(wantPos);
            var localWantRot = Quaternion.Inverse(frame.rotation) * wantRot;

            if (!smoothing)
            {
                smoothPos = localWantPos;
                smoothRot = localWantRot;
                smoothing = true;
            }
            else
            {
                var t = 1f - Mathf.Exp(-SmoothRate * Time.deltaTime);
                smoothPos = Vector3.Lerp(smoothPos, localWantPos, t);
                smoothRot = Quaternion.Slerp(smoothRot, localWantRot, t);
            }

            aim.position = frame.TransformPoint(smoothPos);
            aim.rotation = frame.rotation * smoothRot;
        }

        var view = aim != null ? aim : transform;

        scopeCam.transform.SetPositionAndRotation(over.transform.position, view.rotation);
        scopeCam.fieldOfView = Mathf.Clamp(over.fov, MinFov, MaxFov);
        scopeCam.cullingMask = mainCam.cullingMask & ~(1 << VRLayers.UI);
        if (fogLayer >= 0)
            scopeCam.cullingMask |= 1 << fogLayer;
        scopeCam.clearFlags = mainCam.clearFlags;
        scopeCam.backgroundColor = mainCam.backgroundColor;
        scopeCam.nearClipPlane = mainCam.nearClipPlane;
        scopeCam.farClipPlane = mainCam.farClipPlane;

        PlaceQuad(view);
        EnsureFog();
        UpdateFog();
    }

    private void PlaceQuad(Transform view)
    {
        if (quad == null)
            return;

        var t = quad.transform;

        if (hasRig)
        {
            t.position = view.TransformPoint(scopePos);
            t.rotation = view.rotation * scopeRot;
            t.localScale = scopeScale;
            return;
        }

        t.position = view.position - view.forward * EyeOffset;
        t.rotation = view.rotation;
        t.localScale = Vector3.one * Size;
    }

    private bool hasRig;
    private Vector3 scopePos;
    private Quaternion scopeRot = Quaternion.identity;
    private Vector3 scopeScale = Vector3.one;
    private Vector3 gripPos;
    private Quaternion gripRot = Quaternion.identity;

    private void LoadRig()
    {
        BinocularRig.Load();
        if (!BinocularRig.HasRig)
            return;

        scopePos = BinocularRig.ScopePos;
        scopeRot = BinocularRig.ScopeRot;
        scopeScale = BinocularRig.ScopeScale;
        gripPos = BinocularRig.GripPos;
        gripRot = BinocularRig.GripRot;
        hasRig = true;
    }

    private static Transform AimTransform()
    {
        var ch = Character.localCharacter;
        var item = ch != null && ch.data != null ? ch.data.currentItem : null;
        return item != null ? item.transform : null;
    }

    private object feature;
    private MethodInfo featureSetActive;
    private bool hooked;

    private void ResolveFeature()
    {
        if (feature != null)
            return;

        var ch = Character.localCharacter;
        var item = ch != null && ch.data != null ? ch.data.currentItem : null;
        if (item == null)
            return;

        var overlay = item.GetComponentInChildren<Action_ShowBinocularOverlay>(true);
        if (overlay == null || overlay.featureManager == null)
            return;

        var field = AccessTools.Field(typeof(ItemRenderFeatureManager), "rendererFeature");
        feature = field?.GetValue(overlay.featureManager);
        if (feature == null)
            return;

        featureSetActive = AccessTools.Method(feature.GetType(), "SetActive", new[] { typeof(bool) });
        if (featureSetActive == null)
        {
            feature = null;
            return;
        }

        if (!hooked)
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            hooked = true;
        }

        Plugin.Log.LogInfo("[PeakVR] Binocular overlay feature bound (scope-only)");
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera rendering)
    {
        if (feature == null || scopeCam == null)
            return;

        var wanted = scopeCam.enabled && rendering == scopeCam;
        try { featureSetActive.Invoke(feature, new object[] { wanted }); }
        catch { /* feature went away with the renderer */ }
    }

    private static CameraOverride_Binoculars ActiveOverride()
    {
        var main = MainCamera.instance;
        if (main == null)
            return null;

        return main.camOverride as CameraOverride_Binoculars;
    }

    private bool Build()
    {
        if (scopeCam != null)
            return true;

        mainCam = MainCamera.instance != null ? MainCamera.instance.cam : null;
        if (mainCam == null)
            return false;

        LoadRig();

        var aspect = hasRig && scopeScale.y > 0.0001f ? Mathf.Abs(scopeScale.x / scopeScale.y) : 1f;
        aspect = Mathf.Clamp(aspect, 0.25f, 4f);

        var height = TextureSize;
        var width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 64, 4096);

        target = new RenderTexture(width, height, 24)
        {
            name = "PeakVR ScopeTarget"
        };
        target.Create();

        Plugin.Log.LogInfo($"[PeakVR] Scope target {width}x{height} (aspect {aspect:F2})");

        var camGo = new GameObject("PeakVR ScopeCamera");
        camGo.transform.SetParent(null);

        scopeCam = camGo.AddComponent<Camera>();
        scopeCam.stereoTargetEye = StereoTargetEyeMask.None;
        scopeCam.targetTexture = target;
        scopeCam.aspect = (float)width / height;
        scopeCam.depth = mainCam.depth - 1f;
        scopeCam.allowMSAA = false;
        scopeCam.depthTextureMode |= DepthTextureMode.Depth;
        scopeCam.enabled = false;

        ConfigureUrpCamera(camGo);
        BuildQuad();
        Plugin.Log.LogInfo("[PeakVR] Binocular scope created");
        return true;
    }

    private static void ConfigureUrpCamera(GameObject camGo)
    {
        try
        {
            System.Type type = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", false);
                if (type != null)
                    break;
            }

            if (type == null)
            {
                Plugin.Log.LogWarning("[PeakVR] UniversalAdditionalCameraData not found; scope may miss fog");
                return;
            }

            var data = camGo.GetComponent(type) ?? camGo.AddComponent(type);
            SetMember(data, "renderPostProcessing", true);
            SetMember(data, "requiresDepthTexture", true);
            SetMember(data, "requiresColorTexture", true);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"[PeakVR] Scope URP setup failed: {e.Message}");
        }
    }

    private static void SetMember(object target, string name, object value)
    {
        var prop = target.GetType().GetProperty(name);
        if (prop != null && prop.CanWrite)
            prop.SetValue(target, value);
    }

    private Transform fog;
    private int fogLayer = -1;
    private float nextFogTry;

    private CameraQuad FindFogQuad()
    {
        CameraQuad best = null;

        foreach (var candidate in Resources.FindObjectsOfTypeAll<CameraQuad>())
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid())
                continue;

            var rend = candidate.GetComponentInChildren<Renderer>(true);
            if (rend == null || !candidate.gameObject.activeInHierarchy)
                continue;

            var shader = rend.sharedMaterial != null ? rend.sharedMaterial.shader.name : string.Empty;
            if (best == null || shader.IndexOf("Fog", System.StringComparison.OrdinalIgnoreCase) >= 0)
                best = candidate;
        }

        return best;
    }

    private void EnsureFog()
    {
        if (fog != null || Time.time < nextFogTry)
            return;

        nextFogTry = Time.time + 1f;
        BuildFog();
    }

    private void BuildFog()
    {
        var source = FindFogQuad();
        if (source == null)
            return;

        var clone = Instantiate(source.gameObject);
        clone.name = "PeakVR ScopeFog";

        var comp = clone.GetComponent<CameraQuad>();
        if (comp != null)
            Destroy(comp);

        fog = clone.transform;
        fog.SetParent(scopeCam.transform, false);

        if (clone.layer == VRLayers.UI)
            clone.layer = 0;

        fogLayer = clone.layer;
    }

    private void UpdateFog()
    {
        if (fog == null)
            return;

        var d = scopeCam.nearClipPlane + 0.01f;
        var h = 2f * d * Mathf.Tan(scopeCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        var w = h * scopeCam.aspect;

        fog.localPosition = new Vector3(0f, 0f, d);
        fog.localRotation = Quaternion.identity;
        fog.localScale = new Vector3(w, h, 1f);
    }

    private void BuildQuad()
    {
        var go = new GameObject("PeakVR Scope") { layer = VRLayers.UI };
        go.transform.localScale = Vector3.one * Size;

        var mesh = new Mesh { name = "PeakVR Scope Quad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        mat = new Material(Shader.Find("UI/Default"))
        {
            mainTexture = target,
            renderQueue = RenderQueue
        };
        mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        mat.SetColor("_Color", Color.white);

        quad = go.AddComponent<MeshRenderer>();
        quad.sharedMaterial = mat;
        quad.shadowCastingMode = ShadowCastingMode.Off;
        quad.receiveShadows = false;
        quad.enabled = false;
    }

    private void SetActive(bool active)
    {
        if (scopeCam != null && scopeCam.enabled != active)
            scopeCam.enabled = active;

        if (quad != null && quad.enabled != active)
            quad.enabled = active;
    }

    private void OnDestroy()
    {
        if (hooked)
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            hooked = false;
        }

        if (scopeCam != null)
            Destroy(scopeCam.gameObject);

        if (quad != null)
            Destroy(quad.gameObject);

        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
    }
}
