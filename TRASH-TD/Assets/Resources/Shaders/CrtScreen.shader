// Old-TV CRT look for a UI RawImage showing a RenderTexture (see TrashTD.UI.CrtScreen).
// Curved glass with rounded corners and a dark bezel, scanlines that bloom on bright pixels,
// an RGB aperture-grille mask, colour fringing toward the edges, a soft glow, grain, a rolling
// hum bar, flicker, faint line jitter, vignette and a glass reflection.
// CrtScreen.Distort / IsOnScreen mirror Distort and the corner mask here; keep them in sync.
Shader "TrashTD/UI/CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Curvature ("Curvature", Range(0, 0.5)) = 0.1
        _CornerRadius ("Corner Radius", Range(0.001, 0.3)) = 0.04
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.38
        _ScanlinePeriod ("Scanline Period (px)", Float) = 3
        _MaskStrength ("Aperture Mask Strength", Range(0, 1)) = 0.22
        _Chromatic ("Colour Fringing", Range(0, 0.02)) = 0.0016
        _Bloom ("Glow", Range(0, 2)) = 0.55
        _Vignette ("Vignette", Range(0, 3)) = 0.85
        _Noise ("Grain", Range(0, 0.3)) = 0.045
        _Flicker ("Flicker", Range(0, 0.2)) = 0.025
        _RollStrength ("Rolling Bar", Range(0, 0.3)) = 0.05
        _RollSpeed ("Rolling Bar Speed", Float) = 0.09
        _Jitter ("Line Jitter", Range(0, 0.01)) = 0.0005
        _Glare ("Glass Glare", Range(0, 0.5)) = 0.06
        _Brightness ("Brightness", Range(0.5, 2)) = 1.18
        _BezelColor ("Bezel Color", Color) = (0.012, 0.016, 0.013, 1)

        _CrtTime ("Time (unscaled, set by script)", Float) = 0
        _CrtResolution ("Resolution in px (set by script)", Vector) = (1920, 1080, 0, 0)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "CRT"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;

            float _Curvature;
            float _CornerRadius;
            float _ScanlineStrength;
            float _ScanlinePeriod;
            float _MaskStrength;
            float _Chromatic;
            float _Bloom;
            float _Vignette;
            float _Noise;
            float _Flicker;
            float _RollStrength;
            float _RollSpeed;
            float _Jitter;
            float _Glare;
            float _Brightness;
            fixed4 _BezelColor;
            float _CrtTime;
            float4 _CrtResolution;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            // Barrel distortion: a display point at uv shows the picture at Distort(uv)
            float2 Distort(float2 uv)
            {
                float2 cc = uv - 0.5;
                return 0.5 + cc * (1.0 + _Curvature * dot(cc, cc));
            }

            // 1 inside the curved, rounded screen, 0 on the bezel
            float ScreenMask(float2 uv, float softness)
            {
                float r = max(_CornerRadius, 0.001);
                float2 q = abs(uv - 0.5) * 2.0;
                float2 d = max(q - (1.0 - r), 0.0);
                float dist = length(d) / r;
                return 1.0 - smoothstep(1.0 - softness / r, 1.0, dist);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float t = _CrtTime;
                float2 res = max(_CrtResolution.xy, float2(1.0, 1.0));
                float2 texel = 1.0 / res;

                float2 uv = Distort(IN.texcoord);

                // Faint per-line horizontal jitter
                float lineIndex = floor(uv.y * res.y / max(_ScanlinePeriod, 1.0));
                uv.x += (Hash(float2(lineIndex, floor(t * 24.0))) - 0.5) * _Jitter;

                float inside = ScreenMask(uv, 0.004);

                // Colour fringing grows toward the edges
                float2 fromCenter = uv - 0.5;
                float2 fringe = fromCenter * _Chromatic * 2.0;
                fixed4 center = tex2D(_MainTex, uv);
                float3 col;
                col.r = tex2D(_MainTex, uv + fringe).r;
                col.g = center.g;
                col.b = tex2D(_MainTex, uv - fringe).b;
                float alpha = center.a;

                // Soft glow (halation) from a small cross blur
                float2 o1 = float2(texel.x * 2.5, 0.0);
                float2 o2 = float2(0.0, texel.y * 2.5);
                float3 blur = (tex2D(_MainTex, uv + o1).rgb + tex2D(_MainTex, uv - o1).rgb +
                               tex2D(_MainTex, uv + o2).rgb + tex2D(_MainTex, uv - o2).rgb) * 0.25;
                col += blur * blur * _Bloom * 0.6;

                // Scanlines follow the curved glass; bright pixels bleed across the dark lines
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                float scan = 0.5 + 0.5 * sin(uv.y * res.y * 6.2831853 / max(_ScanlinePeriod, 1.0));
                scan = lerp(scan, 1.0, saturate(luma * 0.6));
                col *= lerp(1.0, scan, _ScanlineStrength);

                // RGB aperture grille, one phosphor stripe per output pixel column
                float column = fmod(floor(IN.vertex.x), 3.0);
                float3 grille = column < 0.5 ? float3(1.0, 0.0, 0.0) : (column < 1.5 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0));
                col *= lerp(float3(1.0, 1.0, 1.0), lerp(float3(0.55, 0.55, 0.55), float3(1.0, 1.0, 1.0), grille), _MaskStrength);

                // Rolling hum bar drifting down the screen
                float roll = (frac(uv.y + t * _RollSpeed) - 0.5) * 9.0;
                float bar = exp(-roll * roll);
                col *= 1.0 + bar * _RollStrength;

                // Flicker: fast mains ripple plus small random dips
                col *= 1.0 + _Flicker * (0.5 * sin(t * 113.0) + (Hash(float2(floor(t * 18.0), 3.1)) - 0.5));

                // Grain
                col += (Hash(uv * res + frac(t * 7.13) * 91.7) - 0.5) * _Noise;

                // Vignette and darker glass edges
                float vig = 16.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y);
                col *= pow(max(saturate(vig), 1e-4), 0.25 * _Vignette);

                col *= _Brightness;

                // Glass reflection near the top-left
                float2 g = (uv - float2(0.28, 0.80)) * float2(1.5, 3.2);
                col += _Glare * exp(-dot(g, g) * 2.5);

                // Bezel outside the curved screen
                float3 finalRgb = lerp(_BezelColor.rgb, col, inside);
                float finalAlpha = lerp(_BezelColor.a, alpha, inside);
                fixed4 color = fixed4(finalRgb, finalAlpha) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
