// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

#if !UNITY_6000_0_OR_NEWER
#error Unity Gaussian Splatting URP support only works in Unity 6 or later
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.XR;

namespace GaussianSplatting.Runtime
{
    // Note: I have no idea what is the purpose of ScriptableRendererFeature vs ScriptableRenderPass, which one of those
    // is supposed to do resource management vs logic, etc. etc. Code below "seems to work" but I'm just fumbling along,
    // without understanding any of it.
    //
    // ReSharper disable once InconsistentNaming
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        class GSRenderPass : ScriptableRenderPass
        {
            const string GaussianSplatRTName = "_GaussianSplatRT";

            const string ProfilerTag = "GaussianSplatRenderGraph";
            static readonly ProfilingSampler s_profilingSampler = new(ProfilerTag);
            static readonly int s_gaussianSplatRT = Shader.PropertyToID(GaussianSplatRTName);

            class PassData
            {
                internal UniversalCameraData CameraData;
                internal TextureHandle SourceTexture;
                internal TextureHandle SourceDepth;
                internal TextureHandle GaussianSplatRT;
            }

            [System.Obsolete("Compatibility path used when URP Render Graph is disabled.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var commandBuffer = CommandBufferPool.Get(ProfilerTag);
                var gaussianSplatRT = new RenderTargetIdentifier(s_gaussianSplatRT);

                RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

                using (new ProfilingScope(commandBuffer, s_profilingSampler))
                {
                    commandBuffer.GetTemporaryRT(s_gaussianSplatRT, rtDesc, FilterMode.Point);
                    commandBuffer.SetGlobalTexture(s_gaussianSplatRT, gaussianSplatRT);

                    var depthTarget = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                    CoreUtils.SetRenderTarget(commandBuffer, gaussianSplatRT, depthTarget.nameID,
                        ClearFlag.Color, Color.clear);

                    Material matComposite =
                        GaussianSplatRenderSystem.instance.SortAndRenderSplats(renderingData.cameraData.camera,
                            commandBuffer);

                    if (matComposite != null)
                    {
                        commandBuffer.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                        CoreUtils.SetRenderTarget(commandBuffer,
                            renderingData.cameraData.renderer.cameraColorTargetHandle, ClearFlag.None);
                        commandBuffer.DrawProcedural(Matrix4x4.identity, matComposite, 0,
                            MeshTopology.Triangles, 3, 1);
                        commandBuffer.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                    }

                    commandBuffer.ReleaseTemporaryRT(s_gaussianSplatRT);
                }

                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass(ProfilerTag, out PassData passData);

                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                RenderTextureDescriptor rtDesc = cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                var textureHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtDesc, GaussianSplatRTName, true);

                passData.CameraData = cameraData;
                passData.SourceTexture = resourceData.activeColorTexture;
                passData.SourceDepth = resourceData.activeDepthTexture;
                passData.GaussianSplatRT = textureHandle;

                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                builder.UseTexture(resourceData.activeDepthTexture);
                builder.UseTexture(textureHandle, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    using var _ = new ProfilingScope(commandBuffer, s_profilingSampler);
                    commandBuffer.SetGlobalTexture(s_gaussianSplatRT, data.GaussianSplatRT);
                    CoreUtils.SetRenderTarget(commandBuffer, data.GaussianSplatRT, data.SourceDepth, ClearFlag.Color, Color.clear);
                    Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(data.CameraData.camera, commandBuffer);
                    commandBuffer.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                    Blitter.BlitCameraTexture(commandBuffer, data.GaussianSplatRT, data.SourceTexture, matComposite, 0);
                    commandBuffer.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                });
            }
        }

        class GSCompositePass : ScriptableRenderPass
        {
            const string ProfilerTag = "GaussianSplatComposite";
            static readonly ProfilingSampler s_profilingSampler = new(ProfilerTag);

            class PassData
            {
                internal TextureHandle SourceTexture;
                internal Material CompositeMaterial;
            }

            public Material compositeMaterial;

            static int StereoInstanceCount()
            {
                if (!XRSettings.enabled)
                    return 1;

                var mode = XRSettings.stereoRenderingMode;
                return mode == XRSettings.StereoRenderingMode.SinglePassInstanced ||
                       mode == XRSettings.StereoRenderingMode.SinglePassMultiview
                    ? 2
                    : 1;
            }

            [System.Obsolete("Compatibility path used when URP Render Graph is disabled.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (compositeMaterial == null)
                    return;

                var commandBuffer = CommandBufferPool.Get(ProfilerTag);
                using (new ProfilingScope(commandBuffer, s_profilingSampler))
                {
                    CoreUtils.SetRenderTarget(commandBuffer, renderingData.cameraData.renderer.cameraColorTargetHandle);
                    commandBuffer.DrawProcedural(Matrix4x4.identity, compositeMaterial, 0,
                        MeshTopology.Triangles, 3, StereoInstanceCount());
                }
                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (compositeMaterial == null)
                    return;

                using var builder = renderGraph.AddUnsafePass(ProfilerTag, out PassData passData);

                var resourceData = frameData.Get<UniversalResourceData>();
                passData.SourceTexture = resourceData.activeColorTexture;
                passData.CompositeMaterial = compositeMaterial;

                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    using var _ = new ProfilingScope(commandBuffer, s_profilingSampler);
                    CoreUtils.SetRenderTarget(commandBuffer, data.SourceTexture);
                    commandBuffer.DrawProcedural(Matrix4x4.identity, data.CompositeMaterial, 0,
                        MeshTopology.Triangles, 3, StereoInstanceCount());
                });
            }
        }

        GSRenderPass m_Pass;
        GSCompositePass m_CompositePass;
        bool m_HasCamera;
        bool m_HasCompositeCamera;
        Material m_CompositeMaterial;

        public override void Create()
        {
            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
            m_CompositePass = new GSCompositePass
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            m_HasCompositeCamera = false;
            m_CompositeMaterial = null;

            if (cameraData.camera.TryGetComponent<GaussianSplatProjectionCamera>(out var projectionCamera) &&
                projectionCamera.isActiveAndEnabled &&
                projectionCamera.role == GaussianSplatProjectionCameraRole.Output &&
                projectionCamera.compositeActive &&
                projectionCamera.compositeMaterial != null)
            {
                m_HasCompositeCamera = true;
                m_CompositeMaterial = projectionCamera.compositeMaterial;
                return;
            }

            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
            {
                if (m_HasCompositeCamera)
                {
                    m_CompositePass.compositeMaterial = m_CompositeMaterial;
                    renderer.EnqueuePass(m_CompositePass);
                }
                return;
            }
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass = null;
            m_CompositePass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
