using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1150)]
internal class VRBazookaHold : MonoBehaviour
{
    private const float SmoothRate = 8f;

    private bool smoothing;
    private Vector3 smoothPos;
    private Quaternion smoothRot = Quaternion.identity;

    private void LateUpdate()
    {
        if (!Plugin.VrEnabled)
            return;

        var item = HeldBazooka();
        var hand = VRHands.Right;

        if (item == null || hand == null)
        {
            smoothing = false;
            return;
        }

        var wantRot = hand.rotation;
        var wantPos = hand.position;

        var localWantPos = transform.InverseTransformPoint(wantPos);
        var localWantRot = Quaternion.Inverse(transform.rotation) * wantRot;

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

        item.transform.SetPositionAndRotation(
            transform.TransformPoint(smoothPos),
            transform.rotation * smoothRot);
    }

    private static Item HeldBazooka()
    {
        var character = Character.localCharacter;
        var item = character != null && character.data != null ? character.data.currentItem : null;

        if (item == null || item.GetComponentInChildren<Peak.Action_Antizooka>(true) == null)
            return null;

        return item;
    }
}
