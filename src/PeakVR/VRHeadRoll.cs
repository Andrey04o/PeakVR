using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(2000)]
internal class VRHeadRoll : MonoBehaviour
{
    public static float LocalRoll;

    private Camera cam;
    private Quaternion hmdLocalRot = Quaternion.identity;

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
        if (character == null || character.refs.head == null || VRPointer.Canvas != null)
            return;

        var axis = character.data.lookDirection;
        if (axis.sqrMagnitude < 1e-4f)
            return;
        axis.Normalize();

        var head = character.refs.head.transform;
        head.rotation = Quaternion.AngleAxis(roll, axis) * head.rotation;
    }
}
