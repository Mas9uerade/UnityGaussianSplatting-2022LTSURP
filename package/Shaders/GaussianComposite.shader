// SPDX-License-Identifier: MIT
Shader "Hidden/Gaussian Splatting/Composite"
{
    Properties
    {
        // 混合数学模式:0 = 预乘线性(默认),1 = 原版 gamma 累加
        // 运行时由 GaussianSplatRenderer.m_EnableLegacyBlend 按对象控制
        _LegacyBlend ("Legacy Blend", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dest Blend", Float) = 6
    }

    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend [_SrcBlend] [_DstBlend]

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc
#include "UnityCG.cginc"

// 混合数学模式(0 = 预乘线性,1 = 原版 gamma 累加),运行时由 m_EnableLegacyBlend 控制
float _LegacyBlend;

struct v2f
{
    float4 vertex : SV_POSITION;
};

v2f vert (uint vtxID : SV_VertexID)
{
    v2f o;
    float2 quadPos = float2(vtxID&1, (vtxID>>1)&1) * 4.0 - 1.0;
	o.vertex = float4(quadPos, 1, 1);
    return o;
}

Texture2D _GaussianSplatRT;

half4 frag (v2f i) : SV_Target
{
    half4 col = _GaussianSplatRT.Load(int3(i.vertex.xy, 0));
    if (_LegacyBlend != 0)
    {
        // 原版模式:RT 里是 gamma 空间累加的颜色,先除以 alpha 再线性化
        return float4(GammaToLinearSpace(col.rgb / col.a), col.a);
    }
    // 默认模式:中间渲染纹理保存的是预乘线性颜色(见 RenderGaussianSplats.shader)
    return float4(col.rgb, col.a);
}
ENDCG
        }
    }
}
