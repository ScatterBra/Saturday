// 地面投影的 HDRI 天空盒（第一、二阶段的房间背景）。
//
// 普通天空盒相当于贴在无限远处的照片：照片里的地板不会跟着相机产生透视，桌子看起来像飘在房间中间。
// 这里假设照片是在房间里离地板 _CaptureHeight 高的地方拍的，房间是一个半径 _Radius 的圆顶、底下是平地板：
// 从相机发出的视线先算它打到地板（或圆顶）的哪一点，再从拍摄点朝那一点去取 HDRI 的颜色。
// 这样照片里的地板就像真的铺在桌子下面 _TableHeight 处，转视角时透视也对。
// 桌子正下方的地板会压暗一块（_Shadow*），代替桌子投下的影子。
// 背景虚化也在这里做：离相机越远，从 Cubemap 越低精度的 mip 取色（_Blur*）。房间全是天空盒画的，
// 所以只糊房间，不会像后处理景深那样把桌上的半透明东西（文字、Sprite）一起糊掉。Cubemap 要开 mipmap。
//
// 参数名和 Unity 自带的 Skybox/Cubemap 一样（_Tex、_Tint、_Exposure、_Rotation），可以直接换着用；
// ChessGameController 会在运行时把 _DeskTop 设成场景里桌面的位置。
Shader "Saturday/Skybox Ground Projected"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}

        [Header(Ground Projection)]
        _DeskTop ("Desk Top Center (world, set at runtime)", Vector) = (1000, -0.4, 0, 0)
        _TableHeight ("Table Height (floor is this far below the desk top)", Float) = 15
        _CaptureHeight ("HDRI Camera Height (above the floor)", Float) = 32
        _Radius ("Room Radius", Float) = 220

        [Header(Background Blur)]
        _BlurMax ("Blur (mip level at Full Blur distance, 0 = off)", Range(0, 7)) = 3.5
        _BlurNear ("Blur Starts At (distance from camera)", Float) = 20
        _BlurFar ("Full Blur At (distance from camera)", Float) = 120

        [Header(Table Shadow On The Floor)]
        _ShadowSize ("Shadow Half Size (x, z)", Vector) = (13, 8.5, 0, 0)
        _ShadowSoftness ("Shadow Softness", Float) = 6
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            samplerCUBE _Tex;
            half4 _Tex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            float4 _DeskTop;
            float _TableHeight;
            float _CaptureHeight;
            float _Radius;
            float4 _ShadowSize;
            float _ShadowSoftness;
            float _ShadowStrength;
            float _BlurMax;
            float _BlurNear;
            float _BlurFar;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // 天空盒网格以相机为中心，顶点位置就是视线方向
                o.dir = v.vertex.xyz;
                return o;
            }

            // 和 Skybox/Cubemap 的旋转方向一致
            float3 RotateY(float3 d, float degrees)
            {
                float s, c;
                sincos(degrees * UNITY_PI / 180.0, s, c);
                return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 floorCenter = float3(_DeskTop.x, _DeskTop.y - _TableHeight, _DeskTop.z);
                float3 capture = floorCenter + float3(0, _CaptureHeight, 0);
                float3 cam = _WorldSpaceCameraPos;

                // 视线打到房间圆顶（以拍摄点为中心）的远交点
                float3 oc = cam - capture;
                float b = dot(oc, d);
                float h = b * b - (dot(oc, oc) - _Radius * _Radius);
                float t = h > 0 ? -b + sqrt(h) : 1e6;

                // 往下看、先打到地板就用地板上的点
                bool onFloor = false;
                if (d.y < -1e-4)
                {
                    float tFloor = (floorCenter.y - cam.y) / d.y;
                    if (tFloor > 0 && tFloor < t)
                    {
                        t = tFloor;
                        onFloor = true;
                    }
                }
                float3 hit = cam + d * t;
                float3 sampleDir = t < 1e5 ? normalize(hit - capture) : d;

                // 越远越糊：圆顶外（t 很大）按最远算
                float blur = _BlurMax * smoothstep(_BlurNear, max(_BlurFar, _BlurNear + 0.01), min(t, 1e4));
                half4 tex = texCUBElod(_Tex, float4(RotateY(sampleDir, _Rotation), blur));
                half3 c = DecodeHDR(tex, _Tex_HDR) * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure;

                // 桌子下面的影子：以桌面中心为中心的圆角矩形，边缘 _ShadowSoftness 内慢慢变淡
                if (onFloor)
                {
                    float2 q = abs(hit.xz - floorCenter.xz) - _ShadowSize.xy;
                    float dist = length(max(q, 0)) + min(max(q.x, q.y), 0);
                    float shadow = 1 - smoothstep(-_ShadowSoftness * 0.5, _ShadowSoftness, dist);
                    c *= 1 - _ShadowStrength * shadow;
                }
                return half4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
