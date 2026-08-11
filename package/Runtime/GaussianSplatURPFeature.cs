// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
            RTHandle m_RenderTarget;
            internal ScriptableRenderer m_Renderer = null;
            internal CommandBuffer m_Cmb = null;
            readonly GaussianSplatURPFeature m_Feature;

            public GSRenderPass(GaussianSplatURPFeature feature)
            {
                m_Feature = feature;
            }

            public void Dispose()
            {
                m_RenderTarget?.Release();
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                if (!m_Feature.m_EnableDirectRendering)
                {
                    // two-stage fallback: splats go into an intermediate RT, composited afterwards
                    RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
                    rtDesc.depthBufferBits = 0;
                    rtDesc.msaaSamples = 1;
                    rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: GaussianSplatRTName);
                    cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                    // cameraDepthTargetHandle can be null on some platforms/setups during
                    // OnCameraSetup; only bind it when available
                    var depthHandle = m_Renderer.cameraDepthTargetHandle;
                    if (depthHandle != null)
                        ConfigureTarget(m_RenderTarget, depthHandle);
                    else
                        ConfigureTarget(m_RenderTarget);
                    ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
                }

                // Direct rendering mode: do not configure any render targets here. The
                // renderer binds the current camera color/depth for passes that do not
                // override the target, so splats are drawn directly into the camera
                // target and depth-tested against the scene. Accessing
                // cameraColorTargetHandle / cameraDepthTargetHandle in OnCameraSetup can
                // return null on some platforms and throw inside ConfigureTarget.
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Cmb == null)
                    return;

                // add sorting, view calc and drawing commands for each splat object
                Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(renderingData.cameraData.camera, m_Cmb);

                // compose
                if (!m_Feature.m_EnableDirectRendering && matComposite != null)
                {
                    m_Cmb.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                    Blitter.BlitCameraTexture(m_Cmb, m_RenderTarget, m_Renderer.cameraColorTargetHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                    m_Cmb.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                }
                context.ExecuteCommandBuffer(m_Cmb);
            }
        }

        [SerializeField] bool m_EnableDirectRendering = true;
        GSRenderPass m_Pass;
        bool m_HasCamera;

        public override void Create()
        {
            m_Pass = new GSRenderPass(this)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            CommandBuffer cmb = system.InitialClearCmdBuffer(cameraData.camera);
            m_Pass.m_Cmb = cmb;
            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
                return;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass?.Dispose();
            m_Pass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
