using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

[DefaultExecutionOrder(1100)]
internal class VRVignette : MonoBehaviour
{
    private const float Distance = 0.12f;
    private const float Coverage = 3.7f;
    private const float FadeInSpeed = 8f;
    private const float FadeOutSpeed = 4f;
    private const float MaxAlpha = 0.92f;
    private const float ClearRadius = 0.35f;
    private const float DarkRadius = 0.70f;

    private Transform quad;
    private Material mat;
    private Texture2D tex;
    private float current;

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

        go.name = "PeakVR Vignette";
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

        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;

        quad = go.transform;
    }

    private void LateUpdate()
    {
        if (mat == null)
            return;

        var cfg = Plugin.Config;
        var target = cfg.MovementVignette.Value ? ComputeIntensity(cfg) : 0f;
        var speed = target > current ? FadeInSpeed : FadeOutSpeed;
        current = Mathf.MoveTowards(current, target, speed * Time.deltaTime);

        var visible = current > 0.002f;
        var rend = quad.GetComponent<MeshRenderer>();
        if (rend.enabled != visible)
            rend.enabled = visible;

        if (visible)
            mat.SetColor("_BaseColor", new Color(0f, 0f, 0f, current * MaxAlpha));
    }

    private static float ComputeIntensity(LCVR.Config cfg)
    {
        var move = VRControls.MoveStick != null
            ? VRControls.MoveStick.ReadValue<Vector2>().magnitude
            : 0f;

        var turn = cfg.SmoothTurn.Value && VRControls.TurnStick != null
            ? Mathf.Abs(VRControls.TurnStick.ReadValue<Vector2>().x)
            : 0f;

        var amount = Mathf.Clamp01(Mathf.Max(move, turn));
        return amount * Mathf.Clamp01(cfg.VignetteStrength.Value);
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
