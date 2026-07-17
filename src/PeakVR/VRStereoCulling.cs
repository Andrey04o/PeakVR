using UnityEngine;
using UnityEngine.Rendering;

namespace PeakVR;

internal class VRStereoCulling : MonoBehaviour
{
    // Widens the shared (both-eye) culling frustum. Each eye's real frustum is asymmetric — wider on
    // the temporal/outer side — so a symmetric reconstruction must be over-widened to avoid culling
    // small objects in the outer periphery of one eye. Tunable at runtime with F9/F10.
    public static float Margin = 1.4f;

    // Per-eye occlusion culling was investigated as a cause of one-eye popping but wasn't it; leave it
    // on for performance. Toggle with F11 if needed.
    public static bool DisableOcclusion = false;

    private Camera cam;
    private int lastFrame = -1;
    private int passIndex;
    private Matrix4x4 sharedCulling;
    private bool haveShared;

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

        // Occlusion culling runs from each eye's own position, so a small object / rock near an
        // occluder edge can be visible to one eye but occluded in the other (frustum margin can't
        // fix that). Disable it so both eyes render the same set.
        cam.useOcclusionCulling = !DisableOcclusion;

        var newFrame = Time.frameCount != lastFrame;
        if (newFrame)
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

        // Build ONE culling matrix per frame (on the first eye) and reuse it for both eyes. Culling
        // AND LOD selection derive from the culling matrix, so a per-eye matrix makes each eye pick a
        // different LOD (buttons/apples/terrain visible in one eye only). A shared matrix fixes that.
        if (newFrame || !haveShared)
        {
            var vfov = Mathf.Atan(1f / proj.m11) * 2f * Mathf.Rad2Deg * Margin;
            var aspect = proj.m11 / proj.m00 * Margin;

            var symProj = Matrix4x4.Perspective(vfov, aspect, cam.nearClipPlane, cam.farClipPlane);
            var centerView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * transform.worldToLocalMatrix;

            sharedCulling = symProj * centerView;
            haveShared = true;
        }

        cam.cullingMatrix = sharedCulling;
    }
}
