using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

internal class VRStereoCulling : MonoBehaviour
{
    private const float Margin = 1.15f;

    private Camera cam;
    private int lastFrame = -1;
    private int passIndex;

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (cam != null)
        {
            cam.ResetCullingMatrix();
            cam.ResetProjectionMatrix();
        }
    }

    private void OnBeginCamera(ScriptableRenderContext ctx, Camera rendering)
    {
        if (rendering != cam)
            return;

        if (Time.frameCount != lastFrame)
        {
            lastFrame = Time.frameCount;
            passIndex = 0;
        }
        else
        {
            passIndex++;
        }

        var eye = passIndex >= 1 ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;

        var proj = cam.GetStereoProjectionMatrix(eye);
        if (proj.m11 == 0f || proj.m00 == 0f)
            return;

        cam.projectionMatrix = proj;

        var vfov = Mathf.Atan(1f / proj.m11) * 2f * Mathf.Rad2Deg * Margin;
        var aspect = proj.m11 / proj.m00 * Margin;

        var symProj = Matrix4x4.Perspective(vfov, aspect, cam.nearClipPlane, cam.farClipPlane);
        var centerView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * transform.worldToLocalMatrix;

        cam.cullingMatrix = symProj * centerView;
    }
}
