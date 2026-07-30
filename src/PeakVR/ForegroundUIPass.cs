using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PeakVR;

internal class ForegroundUIPass : ScriptableRenderPass
{
    private class PassData
    {
        public RendererListHandle List;
        public bool ClearDepth;
    }

    private static readonly ShaderTagId[] Tags =
    {
        new("SRPDefaultUnlit"),
        new("UniversalForward"),
        new("UniversalForwardOnly"),
    };

    public LayerMask Layers = 1 << 5;
    public bool ClearDepth = true;

    private bool warnedBackBuffer;

    public ForegroundUIPass()
    {
        renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        profilingSampler = new ProfilingSampler("PeakVR Foreground UI");
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var resources = frameData.Get<UniversalResourceData>();
        var rendering = frameData.Get<UniversalRenderingData>();
        var cameraData = frameData.Get<UniversalCameraData>();

        if (resources.isActiveTargetBackBuffer)
        {
            if (!warnedBackBuffer)
            {
                warnedBackBuffer = true;
                Plugin.Log.LogWarning($"[PeakVR][ForegroundUI] active target is the back buffer on '{cameraData.camera.name}'; pass skipped");
            }
            return;
        }

        var sorting = new SortingSettings(cameraData.camera)
        {
            criteria = SortingCriteria.CommonTransparent | SortingCriteria.CanvasOrder | SortingCriteria.RendererPriority
        };

        var drawing = new DrawingSettings(Tags[0], sorting);
        for (var i = 1; i < Tags.Length; i++)
            drawing.SetShaderPassName(i, Tags[i]);
        drawing.perObjectData = PerObjectData.None;
        drawing.enableDynamicBatching = false;
        drawing.enableInstancing = false;

        var filtering = new FilteringSettings(RenderQueueRange.all, Layers);
        var parameters = new RendererListParams(rendering.cullResults, drawing, filtering);

        using var builder =
            renderGraph.AddRasterRenderPass<PassData>("PeakVR Foreground UI", out var data, profilingSampler);

        data.List = renderGraph.CreateRendererList(parameters);
        data.ClearDepth = ClearDepth;

        builder.UseRendererList(data.List);
        builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
        builder.AllowPassCulling(false);
        builder.AllowGlobalStateModification(true);

        builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
        {
            var flags = RTClearFlags.Stencil;
            if (d.ClearDepth)
                flags |= RTClearFlags.Depth;

            context.cmd.ClearRenderTarget(flags, Color.clear, 1f, 0);
            context.cmd.DrawRendererList(d.List);
        });
    }
}
