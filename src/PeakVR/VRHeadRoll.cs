using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(2000)]
internal class VRHeadRoll : MonoBehaviour
{
    public static float LocalRoll;

    private Camera cam;
    private Quaternion hmdLocalRot = Quaternion.identity;

    private Quaternion lastHeadOutput;
    private Vector3 lastAxis;
    private float lastRoll;
    private bool rollInit;

    private void Awake()
    {
        cam = GetComponentInChildren<Camera>();
    }

    private void Update()
    {
        if (cam != null)
            hmdLocalRot = cam.transform.localRotation;
    }

    private void LateUpdate()
    {
        var fwd = hmdLocalRot * Vector3.forward;
        var up = hmdLocalRot * Vector3.up;
        var flatUp = Vector3.ProjectOnPlane(Vector3.up, fwd);
        if (flatUp.sqrMagnitude < 1e-4f)
            return;
        flatUp.Normalize();

        var roll = Vector3.SignedAngle(flatUp, up, fwd);
        LocalRoll = roll;

        var character = Character.localCharacter;
        if (character == null || character.refs.head == null || character.data.fullyPassedOut)
        {
            rollInit = false;
            return;
        }

        var axis = character.data.lookDirection;
        if (axis.sqrMagnitude < 1e-4f)
            return;
        axis.Normalize();

        var head = character.refs.head.transform;

        var baseRot = head.rotation;
        if (rollInit && Quaternion.Angle(head.rotation, lastHeadOutput) < 1f)
            baseRot = Quaternion.AngleAxis(-lastRoll, lastAxis) * head.rotation;

        var output = Quaternion.AngleAxis(roll, axis) * baseRot;
        head.rotation = output;

        lastHeadOutput = output;
        lastAxis = axis;
        lastRoll = roll;
        rollInit = true;
    }
}
