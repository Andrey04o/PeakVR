using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.InputSystem;

namespace PeakVR;

[DefaultExecutionOrder(2000)]
internal class VRShoulderTwist : MonoBehaviour
{
    public static bool Enabled = true;

    private const float ExtensionStart = 0.9f;
    private const float Influence = 0.6f;
    private const float MaxTwist = 60f;
    private const float Smoothing = 12f;

    private struct ArmState
    {
        public bool has;
        public float prev;
        public float baseline;
        public float smooth;
        public bool applied;
        public Quaternion cleanLocal;
        public Quaternion lastOutputLocal;
    }

    private ArmState right;
    private ArmState left;
    private bool recalibrate;

    private void LateUpdate()
    {
        HandleToggles();

        var c = Character.localCharacter;
        if (c == null || VRHands.Left == null || VRHands.Right == null)
            return;

        var refs = c.refs;
        if (refs == null || refs.ikLeft == null || refs.ikRight == null)
            return;

        var suppress = !Enabled || VRPointer.Canvas != null;

        Apply(refs.ikRight, VRHands.Right, suppress, ref right);
        Apply(refs.ikLeft, VRHands.Left, suppress, ref left);

        recalibrate = false;
    }

    private void HandleToggles()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return;

        if (kb.f8Key.wasPressedThisFrame)
        {
            Enabled = !Enabled;
            Plugin.Log.LogInfo($"[PeakVR][Twist] wrist shoulder-twist {(Enabled ? "ENABLED" : "DISABLED")}");
        }

        if (kb.f7Key.wasPressedThisFrame)
        {
            VRArmIKPatch.ElbowClampEnabled = !VRArmIKPatch.ElbowClampEnabled;
            Plugin.Log.LogInfo($"[PeakVR][Twist] min-elbow clamp {(VRArmIKPatch.ElbowClampEnabled ? "ENABLED" : "DISABLED")}");
        }

        if (kb.f6Key.wasPressedThisFrame)
        {
            recalibrate = true;
            Plugin.Log.LogInfo("[PeakVR][Twist] recalibrating neutral wrist baseline");
        }
    }

    private void Apply(TwoBoneIKConstraint ik, Transform hand, bool suppress, ref ArmState s)
    {
        var root = ik.data.root;
        var mid = ik.data.mid;
        var tip = ik.data.tip;
        if (root == null || mid == null || tip == null)
            return;

        if (!s.applied || Quaternion.Angle(root.localRotation, s.lastOutputLocal) > 0.5f)
            s.cleanLocal = root.localRotation;
        root.localRotation = s.cleanLocal;
        s.applied = false;

        var handRot = hand.rotation * VRArmIKPatch.HandRotationOffset;
        var worldAxis = tip.position - root.position;
        var dist = worldAxis.magnitude;
        if (dist < 1e-4f)
            return;
        worldAxis /= dist;

        var reach = Vector3.Distance(root.position, mid.position) + Vector3.Distance(mid.position, tip.position);
        if (reach < 1e-4f)
            return;

        var extension = Mathf.Clamp01(dist / reach);
        var t = Mathf.InverseLerp(ExtensionStart, 1f, extension);

        var raw = TwistAngle(handRot * Quaternion.Inverse(mid.rotation), worldAxis);
        if (s.has)
        {
            while (raw - s.prev > 180f) raw -= 360f;
            while (raw - s.prev < -180f) raw += 360f;
        }
        else
        {
            s.baseline = raw;
            s.has = true;
        }
        s.prev = raw;

        if (recalibrate)
            s.baseline = raw;

        var physical = raw - s.baseline;
        var move = Mathf.Clamp(Influence * t * physical, -MaxTwist, MaxTwist);
        s.smooth = Mathf.Lerp(s.smooth, move, Mathf.Clamp01(Smoothing * Time.deltaTime));

        if (suppress || t <= 0f)
            return;

        var localAxis = mid.localPosition.sqrMagnitude > 1e-8f ? mid.localPosition.normalized : Vector3.up;
        root.localRotation = s.cleanLocal * Quaternion.AngleAxis(s.smooth, localAxis);
        s.lastOutputLocal = root.localRotation;
        s.applied = true;
        tip.rotation = handRot;
    }

    private static float TwistAngle(Quaternion q, Vector3 axis)
    {
        var proj = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
        var mag = Mathf.Sqrt(proj.x * proj.x + proj.y * proj.y + proj.z * proj.z + q.w * q.w);
        if (mag < 1e-5f)
            return 0f;

        var w = q.w / mag;
        var angle = 2f * Mathf.Acos(Mathf.Clamp(w, -1f, 1f)) * Mathf.Rad2Deg;
        if (Vector3.Dot(proj, axis) < 0f)
            angle = -angle;
        if (angle > 180f)
            angle -= 360f;
        else if (angle < -180f)
            angle += 360f;
        return angle;
    }
}
