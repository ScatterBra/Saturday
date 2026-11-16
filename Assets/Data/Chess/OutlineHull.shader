// 描边用的纯色外壳：模型复制一份放大一圈，只画背面（Cull Front），原模型挡住中间，露出来的边就是轮廓。
// 规则书鼠标悬停的高光轮廓用它（RuleBookFocus）。不受光照，颜色就是 _Color。
Shader "Saturday/Outline Hull"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.35, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Front
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Color;

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            half4 frag() : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
