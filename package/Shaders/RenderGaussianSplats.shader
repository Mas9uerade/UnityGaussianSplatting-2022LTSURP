// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    Properties
    {
        // 运行时由 GaussianSplatRenderer.m_EnableDepthWrite 按对象控制
        [Toggle] _ZWrite ("Write Depth", Float) = 0
        // 混合数学模式:0 = 预乘线性(默认),1 = 原版 gamma 累加
        // 运行时由 GaussianSplatRenderer.m_EnableLegacyBlend 按对象控制
        _LegacyBlend ("Legacy Blend", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dest Blend", Float) = 6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc

#include "UnityCG.cginc"
#include "GaussianSplatting.hlsl"

StructuredBuffer<uint> _OrderBuffer;

struct v2f
{
    half4 col : COLOR0;
    float2 pos : TEXCOORD0;
    float4 vertex : SV_POSITION;
};

StructuredBuffer<SplatViewData> _SplatViewData;
ByteAddressBuffer _SplatSelectedBits;
uint _SplatBitsValid;

v2f vert (uint vtxID : SV_VertexID, uint instID : SV_InstanceID)
{
    v2f o = (v2f)0;
    instID = _OrderBuffer[instID];
	SplatViewData view = _SplatViewData[instID];
	float4 centerClipPos = view.pos;
	bool behindCam = centerClipPos.w <= 0;
	if (behindCam)
	{
		o.vertex = asfloat(0x7fc00000); // NaN discards the primitive
	}
	else
	{
		o.col.r = f16tof32(view.color.x >> 16);
		o.col.g = f16tof32(view.color.x);
		o.col.b = f16tof32(view.color.y >> 16);
		o.col.a = f16tof32(view.color.y);

		// 每个四边形由 6 个顺序顶点组成(两个三角形),这样就能在不使用索引缓冲的
		// DrawProceduralIndirect 下绘制;结果与旧的索引缓冲(0,1,2,1,3,2)一致。
		float2 quadPos;
		uint v = vtxID;
		if (v == 0) quadPos = float2(-2, -2);
		else if (v == 1 || v == 3) quadPos = float2(2, -2);
		else if (v == 2 || v == 5) quadPos = float2(-2, 2);
		else quadPos = float2(2, 2);

		o.pos = quadPos;

		float2 deltaScreenPos = (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _ScreenParams.xy;
		o.vertex = centerClipPos;
		o.vertex.xy += deltaScreenPos * centerClipPos.w;

		// is this splat selected?
		if (_SplatBitsValid)
		{
			uint wordIdx = instID / 32;
			uint bitIdx = instID & 31;
			uint selVal = _SplatSelectedBits.Load(wordIdx * 4);
			if (selVal & (1 << bitIdx))
			{
				o.col.a = -1;				
			}
		}
	}
	FlipProjectionIfBackbuffer(o.vertex);
    return o;
}

half4 frag (v2f i) : SV_Target
{
	float power = -dot(i.pos, i.pos);
	half alpha = exp(power);
	if (i.col.a >= 0)
	{
		alpha = saturate(alpha * i.col.a);
	}
	else
	{
		// "selected" splat: magenta outline, increase opacity, magenta tint
		half3 selectedColor = half3(1,0,1);
		if (alpha > 7.0/255.0)
		{
			if (alpha < 10.0/255.0)
			{
				alpha = 1;
				i.col.rgb = selectedColor;
			}
			alpha = saturate(alpha + 0.3);
		}
		i.col.rgb = lerp(i.col.rgb, selectedColor, 0.5);
	}
	
    if (alpha < 1.0/255.0)
        discard;

    // 预乘线性(默认):既能直接与场景混合,也能在两段式的中间渲染纹理中正确累加;
    // 原版模式:gamma 空间颜色直接输出,由合成阶段统一线性化(与上游行为一致)
    half4 res = _LegacyBlend != 0
        ? half4(i.col.rgb * alpha, alpha)
        : half4(GammaToLinearSpace(i.col.rgb) * alpha, alpha);
    return res;
}
ENDCG
        }
    }
}
