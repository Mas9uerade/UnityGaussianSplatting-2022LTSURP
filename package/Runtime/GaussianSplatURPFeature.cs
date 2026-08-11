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
                    // 两段式回退:泼溅先渲染进中间的渲染纹理,再合成到相机目标
                    RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
                    rtDesc.depthBufferBits = 0;
                    rtDesc.msaaSamples = 1;
                    rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: GaussianSplatRTName);
                    cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                    // 部分平台在 OnCameraSetup 时深度目标句柄可能为空,仅在可用时才绑定深度
                    var depthHandle = m_Renderer.cameraDepthTargetHandle;
                    if (depthHandle != null)
                        ConfigureTarget(m_RenderTarget, depthHandle);
                    else
                        ConfigureTarget(m_RenderTarget);
                    ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
                }

                // 直绘模式:这里不配置任何渲染目标,URP 会自动为未覆盖目标的渲染 Pass
                // 绑定当前相机的颜色与深度缓冲,泼溅因此直接绘制进相机目标并参与场景深度测试。
                // 在 OnCameraSetup 中直接访问相机目标句柄,在某些平台会返回空值并导致
                // ConfigureTarget 抛异常,所以这里不再使用它们。
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Cmb == null)
                    return;

                // 为每个泼溅对象添加排序、视图计算与绘制命令
                Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(renderingData.cameraData.camera, m_Cmb);

                // 合成(仅两段式回退需要)
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
            m_Pass.m_Renderer = renderer;
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
