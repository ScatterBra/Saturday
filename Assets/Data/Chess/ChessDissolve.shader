// 棋子被烧掉（火龙、掉进岩浆）时的溶解：按噪声图一块块镂空，镂空的边缘发橙红色的光。
// 不受光照，2D 贴图棋子（SpriteRenderer 自动把贴图塞进 _MainTex、颜色走顶点色）和 3D 模型（代码把原材质的贴图、颜色抄过来）都能用。
// _Progress 由 BoardView 从 0 拉到 1：0 = 完整，1 = 全部消失。
Shader "Saturday/Chess Dissolve"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _NoiseTex ("Noise", 2D) = "gray" {}
        _NoiseScale ("Noise Scale (per world unit)", Float) = 1.2
        _NoiseOffset ("Noise Offset", Vector) = (0, 0, 0, 0)
        _Progress ("Progress", Range(0, 1)) = 0
        _EdgeWidth ("Edge Width", Range(0.01, 0.5)) = 0.12
        [HDR] _EdgeColor ("Edge Color", Color) = (4, 1.3, 0.25, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull Off

        Pass
        {
            Name "Dissolve"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                float4 _NoiseTex_ST;
                float _NoiseScale;
                float4 _NoiseOffset;
                float _Progress;
                float _EdgeWidth;
                half4 _EdgeColor;
                float _Cutoff;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _BaseColor * input.color;
                clip(color.a - _Cutoff);

                // 噪声按世界坐标取：贴图棋子和模型的溶解纹理粗细一样，模型的侧面也不会一下子全没
                float3 p = input.positionWS * _NoiseScale + _NoiseOffset.xyz;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(p.x + p.z * 0.6, p.y + p.z * 0.3)).r;

                // 进度 0 时门槛低于所有噪声值（完整），进度 1 时门槛高于所有噪声值（全没）
                float threshold = lerp(-_EdgeWidth, 1.0, _Progress);
                float above = noise - threshold;
                clip(above);

                // 快要镂空的一圈发光
                float edge = 1.0 - saturate(above / _EdgeWidth);
                color.rgb = lerp(color.rgb, _EdgeColor.rgb, edge * step(0.0001, _Progress));
                color.a = 1;
                return color;
            }
            ENDHLSL
        }
    }
}
