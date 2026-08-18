using System.Text;
using UnityEngine;

namespace PeakVR;

internal static class VRLayerProbe
{
    private const float Distance = 2.5f;
    private const float FarDistance = 20f;
    private const float Spacing = 0.22f;
    private const float CubeSize = 0.11f;
    private const int Columns = 8;

    private static GameObject root;

    public static void Toggle(Camera cam)
    {
        if (root != null)
        {
            Object.Destroy(root);
            root = null;
            Plugin.Log.LogInfo("[PeakVR][LayerTest] removed");
            return;
        }

        if (cam == null)
            return;

        root = new GameObject("PeakVR LayerTest");

        var named = new StringBuilder();
        Build(cam, Distance, named);
        Build(cam, FarDistance, null);

        Plugin.Log.LogInfo($"[PeakVR][LayerTest] spawned two grids at {Distance:F0}m and {FarDistance:F0}m, "
            + "32 cubes each, numbered underneath");
        Plugin.Log.LogInfo($"[PeakVR][LayerTest] {named}");
    }

    private static void Build(Camera cam, float distance, StringBuilder named)
    {
        var head = cam.transform;
        var grid = new GameObject($"Grid{distance:F0}m");
        grid.transform.SetParent(root.transform, false);
        grid.transform.position = head.position + head.forward * distance;
        grid.transform.rotation = Quaternion.LookRotation(head.position - grid.transform.position, Vector3.up);

        var scale = distance / Distance;

        for (var layer = 0; layer < 32; layer++)
        {
            var column = layer % Columns;
            var row = layer / Columns;
            var local = new Vector3((column - (Columns - 1) * 0.5f) * Spacing, (1.5f - row) * Spacing, 0f);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"LayerCube{layer}";
            cube.layer = layer;
            cube.transform.SetParent(grid.transform, false);
            cube.transform.localPosition = local * scale;
            cube.transform.localScale = Vector3.one * CubeSize * scale;

            var collider = cube.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            cube.AddComponent<VRLayerProbeSpin>();

            var label = new GameObject($"LayerLabel{layer}");
            label.layer = 0;
            label.transform.SetParent(grid.transform, false);
            label.transform.localPosition = (local + new Vector3(0f, -CubeSize, 0f)) * scale;

            var text = label.AddComponent<TextMesh>();
            text.text = layer.ToString();
            text.fontSize = 80;
            text.characterSize = 0.03f * scale;
            text.anchor = TextAnchor.UpperCenter;
            text.color = Color.white;

            var name = LayerMask.LayerToName(layer);
            if (!string.IsNullOrEmpty(name))
                named?.Append($"{layer}={name} ");
        }
    }
}

internal class VRLayerProbeSpin : MonoBehaviour
{
    private const float Speed = 120f;
    private const float Sway = 0.35f;

    private Vector3 origin;

    private void Start() => origin = transform.localPosition;

    private void Update()
    {
        transform.Rotate(Vector3.up, Speed * Time.deltaTime, Space.Self);
        transform.localPosition = origin + Vector3.right * (Mathf.Sin(Time.time * 2f) * Sway);
    }
}
