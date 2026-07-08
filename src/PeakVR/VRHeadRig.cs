using UnityEngine;

namespace PeakVR;

[DefaultExecutionOrder(1000)]
internal class VRHeadRig : MonoBehaviour
{
    private const float ForwardOffset = 0f;

    private Camera cam;
    private Vector3 hmdOffset;

    private void Awake()
    {
        cam = GetComponentInChildren<Camera>();
    }

    private void Update()
    {
        if (cam != null)
            hmdOffset = cam.transform.localPosition;
    }

    private void LateUpdate()
    {
        var character = Character.localCharacter;
        if (character == null || cam == null)
            return;

        transform.position = character.GetCameraPos(ForwardOffset) - hmdOffset;
    }
}
