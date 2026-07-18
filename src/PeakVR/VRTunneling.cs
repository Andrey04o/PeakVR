using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[DefaultExecutionOrder(1100)]
internal class VRTunneling : MonoBehaviour
{
    private const float Distance = 2f;
    private const float Size = 12f;
    private const int RenderQueue = 4000;
    private const float InTime = 0.3f;
    private const float OutTime = 1f;
    private const float HoldTime = 0.5f;
    private const float MaxAlpha = 1f;
    private const float MinClose = 0f;
    private const float MaxClose = 7f;
    private const float ClearRadius = 0.35f;
    private const float DarkRadius = 0.70f;
    private const float MinSpeed = 0.6f;
    private const float TurnThreshold = 0.5f;
    private const float LogInterval = 0.5f;

    private MeshRenderer rend;
    private Material mat;
    private Texture2D tex;
    private Mesh mesh;
    private readonly Vector2[] uv = new Vector2[4];
    private float current;
    private float holdTimer;
    private float logTimer;

    private void Start()
    {
        Build();
    }

    private void Build()
    {
        var go = new GameObject("PeakVR Tunneling");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.forward * Distance;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * Size;

        var ui = LayerMask.NameToLayer("UI");
        if (ui >= 0)
            go.layer = ui;

        mesh = new Mesh { name = "PeakVR Tunneling Quad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        mesh.MarkDynamic();
        ApplyUv(1f);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        tex = GenerateRadial(256);

        mat = new Material(Shader.Find("UI/Default"))
        {
            mainTexture = tex,
            renderQueue = RenderQueue
        };
        mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        mat.SetColor("_Color", new Color(0f, 0f, 0f, MaxAlpha));

        rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.enabled = false;
    }

    private void LateUpdate()
    {
        if (mat == null)
            return;

        if (VRPointer.Canvas != null)
        {
            current = 0f;
            holdTimer = 0f;
            if (rend.enabled)
                rend.enabled = false;
            return;
        }

        var cfg = Plugin.Config;
        var target = cfg.MovementTunneling.Value ? MovementTarget() : 0f;
        var dt = Time.deltaTime;

        if (target >= current)
        {
            holdTimer = 0f;
            current = Mathf.MoveTowards(current, target, dt / InTime);
        }
        else
        {
            holdTimer += dt;
            if (holdTimer >= HoldTime)
                current = Mathf.MoveTowards(current, target, dt / OutTime);
        }

        var visible = current > 0.002f;
        if (rend.enabled != visible)
            rend.enabled = visible;

        if (!visible)
            return;

        var maxTiling = Mathf.Lerp(MinClose, MaxClose, Mathf.Clamp01(cfg.TunnelingStrength.Value));
        ApplyUv(Mathf.Lerp(1f, maxTiling, current));
    }

    private void ApplyUv(float tiling)
    {
        var o = 0.5f * (1f - tiling);
        uv[0] = new Vector2(o, o);
        uv[1] = new Vector2(o + tiling, o);
        uv[2] = new Vector2(o, o + tiling);
        uv[3] = new Vector2(o + tiling, o + tiling);
        mesh.uv = uv;
    }

    private float MovementTarget()
    {
        var character = Character.localCharacter;
        if (character == null || character.data == null)
            return 0f;

        var speed = character.data.avarageVelocity.magnitude;

        logTimer += Time.deltaTime;
        if (logTimer >= LogInterval && speed > 0.05f)
        {
            logTimer = 0f;
            Plugin.Log.LogInfo($"[PeakVR][Tunnel] speed={speed:F2} current={current:F2}");
        }

        var turning = Plugin.Config.SmoothTurn.Value && VRControls.TurnStick != null
            && Mathf.Abs(VRControls.TurnStick.ReadValue<Vector2>().x) > TurnThreshold;

        return speed > MinSpeed || turning ? 1f : 0f;
    }

    private static Texture2D GenerateRadial(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var px = new Color32[size * size];
        var half = (size - 1) * 0.5f;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x - half) / half;
                var dy = (y - half) / half;
                var r = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp01((r - ClearRadius) / (DarkRadius - ClearRadius));
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
        }

        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }
}
