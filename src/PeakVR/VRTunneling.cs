using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[DefaultExecutionOrder(1100)]
internal class VRTunneling : MonoBehaviour
{
    private const float Distance = 0.12f;
    private const float Coverage = 3.7f;
    private const float InTime = 0.3f;
    private const float OutTime = 1f;
    private const float HoldTime = 0.5f;
    private const float MaxAlpha = 1f;
    private const float MinClose = 1.5f;
    private const float MaxClose = 3f;
    private const float ClearRadius = 0.35f;
    private const float DarkRadius = 0.70f;
    private const float MinSpeed = 0.6f;
    private const float MaxSpeed = 6f;
    private const float LogInterval = 0.5f;

    private Transform quad;
    private MeshRenderer rend;
    private Material mat;
    private Texture2D tex;
    private float current;
    private float holdTimer;
    private float logTimer;

    private void Start()
    {
        Build();
    }

    private void Build()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = go.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);

        go.name = "PeakVR Tunneling";
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.forward * Distance;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(Distance * Coverage, Distance * Coverage, 1f);

        tex = GenerateRadial(256);

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        mat = new Material(shader);
        mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)CompareFunction.Always);
        mat.SetInt("_Cull", (int)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 4000;
        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));

        rend = go.GetComponent<MeshRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        rend.enabled = false;

        quad = go.transform;
    }

    private void LateUpdate()
    {
        if (mat == null)
            return;

        var cfg = Plugin.Config;
        var target = cfg.MovementTunneling.Value ? SpeedAmount() : 0f;
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

        mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, MaxAlpha));

        var maxTiling = Mathf.Lerp(MinClose, MaxClose, Mathf.Clamp01(cfg.TunnelingStrength.Value));
        var tiling = Mathf.Lerp(1f, maxTiling, current);
        var offset = 0.5f * (1f - tiling);
        mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
        mat.SetTextureOffset("_BaseMap", new Vector2(offset, offset));
    }

    private float SpeedAmount()
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

        var turn = Plugin.Config.SmoothTurn.Value && VRControls.TurnStick != null
            ? Mathf.Abs(VRControls.TurnStick.ReadValue<Vector2>().x)
            : 0f;

        return Mathf.Max(Mathf.InverseLerp(MinSpeed, MaxSpeed, speed), turn);
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
