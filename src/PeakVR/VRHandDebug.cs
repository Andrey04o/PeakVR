using System.Collections.Generic;
using UnityEngine;

namespace PeakVR;

internal static class VRHandDebug
{
    private const float TargetSize = 0.07f;
    private const float BoneSize = 0.05f;
    private const float HintSize = 0.04f;

    private static readonly Color TargetColor = new(1f, 0.1f, 0.1f, 1f);
    private static readonly Color BoneColor = new(0.1f, 1f, 0.2f, 1f);
    private static readonly Color HintColor = new(0.2f, 0.5f, 1f, 1f);

    private class Markers
    {
        public Transform leftTarget;
        public Transform rightTarget;
        public Transform leftBone;
        public Transform rightBone;
        public Transform leftHint;
        public Transform rightHint;

        public Character character;
        public Vector3 targetLeft;
        public Vector3 targetRight;

        public void Sync()
        {
            var refs = character != null ? character.refs : null;
            if (refs == null || refs.ikLeft == null || refs.ikRight == null)
                return;

            Put(leftTarget, targetLeft);
            Put(rightTarget, targetRight);
            Follow(leftBone, refs.ikLeft.data.tip);
            Follow(rightBone, refs.ikRight.data.tip);
            Follow(leftHint, refs.ikLeft.data.hint);
            Follow(rightHint, refs.ikRight.data.hint);
        }

        private static void Put(Transform marker, Vector3 position)
        {
            if (marker == null)
                return;

            marker.gameObject.SetActive(true);
            marker.position = position;
        }

        private static void Follow(Transform marker, Transform bone)
        {
            if (marker == null)
                return;

            if (bone == null)
            {
                marker.gameObject.SetActive(false);
                return;
            }

            marker.gameObject.SetActive(true);
            marker.position = bone.position;
        }
    }

    private class Driver : MonoBehaviour
    {
        private void LateUpdate()
        {
            foreach (var m in Live.Values)
                m.Sync();
        }
    }

    private static Driver driver;

    private static readonly Dictionary<Character, Markers> Live = new();

    public static bool Enabled =>
        Plugin.Config != null && Plugin.Config.ShowHandTargets.Value;

    public static void Show(Character c, Vector3 leftTarget, Vector3 rightTarget)
    {
        if (c == null || c.refs == null || c.refs.ikLeft == null || c.refs.ikRight == null)
            return;

        if (!Enabled)
        {
            Hide(c);
            return;
        }

        if (driver == null)
        {
            var host = new GameObject("PeakVR HandDebug");
            Object.DontDestroyOnLoad(host);
            driver = host.AddComponent<Driver>();
        }

        if (!Live.TryGetValue(c, out var m) || m.leftTarget == null)
        {
            m = new Markers
            {
                leftTarget = Sphere($"{c.characterName} L target", TargetColor, TargetSize),
                rightTarget = Sphere($"{c.characterName} R target", TargetColor, TargetSize),
                leftBone = Sphere($"{c.characterName} L hand", BoneColor, BoneSize),
                rightBone = Sphere($"{c.characterName} R hand", BoneColor, BoneSize),
                leftHint = Sphere($"{c.characterName} L elbow", HintColor, HintSize),
                rightHint = Sphere($"{c.characterName} R elbow", HintColor, HintSize)
            };
            Live[c] = m;
        }

        m.targetLeft = leftTarget;
        m.targetRight = rightTarget;
        m.character = c;
        m.Sync();
    }

    public static void Hide(Character c)
    {
        if (c == null || !Live.TryGetValue(c, out var m))
            return;

        Live.Remove(c);

        Kill(m.leftTarget);
        Kill(m.rightTarget);
        Kill(m.leftBone);
        Kill(m.rightBone);
        Kill(m.leftHint);
        Kill(m.rightHint);
    }

    private static void Kill(Transform marker)
    {
        if (marker != null)
            Object.Destroy(marker.gameObject);
    }

    private static Transform Sphere(string name, Color color, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = $"PeakVR Debug {name}";
        go.transform.localScale = Vector3.one * size;

        Object.Destroy(go.GetComponent<Collider>());

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        else
            mat.color = color;

        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DontDestroyOnLoad(go);
        return go.transform;
    }
}
