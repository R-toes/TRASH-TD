// Old-TV CRT look for a UI RawImage showing a RenderTexture (see TrashTD.UI.CrtDisplay / CrtScreen).
// Modelled on how a real tube draws low-res pixel art:
//   - soft horizontal reconstruction (neighbouring pixels blend like an analogue signal)
//   - scanline beams whose width grows with brightness (bright pixels bloom across the dark gaps)
//   - an RGB phosphor mask (slot mask or aperture grille) that lights each colour's own phosphor and
//     dims the others, which is what makes small palettes shimmer with colour up close
//   - halation glow that fills the mask gaps, plus curvature, rounded corners and bezel, colour fringing,
//     grain, a rolling hum bar, flicker, line jitter, vignette and a glass reflection.
// CrtScreen.Distort / IsOnScreen mirror Distort and ScreenMask here; keep them in sync.
Shader "TrashTD/UI/CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Curvature ("Curvature", Range(0, 0.5)) = 0.06
        _CornerRadius ("Corner Radius", Range(0.001, 0.3)) = 0.03

        _Sharpness ("Horizontal Sharpness", Range(0, 1)) = 0.5
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.85
        _ScanlinePeriod ("Scanline Period (px)", Float) = 4
        _BeamMin ("Beam Width (dark)", Range(0.05, 0.6)) = 0.2
        _BeamMax ("Beam Width (bright)", Range(0.05, 0.6)) = 0.42

        _MaskType ("Mask Type (0 grille, 1 slot)", Float) = 1
        _MaskStrength ("Mask Strength", Range(0, 1)) = 1
        _MaskDark ("Mask Dark", Range(0, 1)) = 0.55
        _MaskLight ("Mask Light", Range(1, 2)) = 1.45
        _MaskScale ("Mask Scale (px per phosphor)", Float) = 1

        _Chromatic ("Colour Fringing", Range(0, 0.02)) = 0.0012
        _Bloom ("Halation Glow", Range(0, 2)) = 0.35
        _Vignette ("Vignette", Range(0, 3)) = 0.6
        _Noise ("Grain", Range(0, 0.3)) = 0.03
        _Flicker ("Flicker", Range(0, 0.2)) = 0.015
        _RollStrength ("Rolling Bar", Range(0, 0.3)) = 0.035
        _RollSpeed ("Rolling Bar Speed", Float) = 0.08
        _Jitter ("Line Jitter", Range(0, 0.01)) = 0.0003
        _Glare ("Glass Glare", Range(0, 0.5)) = 0.04
        _Brightness ("Brightness", Range(0.5, 3)) = 1.3
        _Opaque ("Opaque Output", Range(0, 1)) = 0
        _BezelColor ("Bezel Color", Color) = (0.008, 0.01, 0.009, 1)

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
            float _Sharpness;
            float _ScanlineStrength;
            float _ScanlinePeriod;
            float _BeamMin;
            float _BeamMax;
            float _MaskType;
            float _MaskStrength;
            float _MaskDark;
            float _MaskLight;
            float _MaskScale;
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
            float _Opaque;
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

            // Analogue-style horizontal reconstruction: neighbours blend in as sharpness drops
            float3 SampleSoft(float2 uv, float texelX)
            {
                float side = 0.5 * (1.0 - _Sharpness);
                float3 c = tex2D(_MainTex, uv).rgb;
                float3 l = tex2D(_MainTex, uv - float2(texelX, 0.0)).rgb;
                float3 r = tex2D(_MainTex, uv + float2(texelX, 0.0)).rgb;
                return (c + (l + r) * side) / (1.0 + 2.0 * side);
            }

            // RGB phosphor mask in output pixels. Type 0: aperture grille (vertical RGB stripes).
            // Type 1: slot mask (RGB triads in short slots, alternate triads offset by half a slot).
            float3 PhosphorMask(float2 pixel)
            {
                float2 p = floor(pixel / max(_MaskScale, 1.0));
                float channel = fmod(p.x, 3.0);
                float3 lit = float3(channel < 0.5, channel >= 0.5 && channel < 1.5, channel >= 1.5);
                float3 mask = lerp(_MaskDark.xxx, _MaskLight.xxx, lit);

                if (_MaskType > 0.5)
                {
                    float triad = floor(p.x / 3.0);
                    float row = fmod(p.y + fmod(triad, 2.0) * 2.0, 4.0);
                    mask *= row < 0.5 ? _MaskDark : 1.0;
                }

                return lerp(float3(1.0, 1.0, 1.0), mask, _MaskStrength);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float t = _CrtTime;
                float2 res = max(_CrtResolution.xy, float2(1.0, 1.0));
                float2 texel = 1.0 / res;
                float period = max(_ScanlinePeriod, 1.0);

                float2 uv = Distort(IN.texcoord);

                // Faint per-line horizontal jitter
                float lineIndex = floor(uv.y * res.y / period);
                uv.x += (Hash(float2(lineIndex, floor(t * 24.0))) - 0.5) * _Jitter;

                float inside = ScreenMask(uv, 0.003);

                // Picture with colour fringing toward the edges
                float2 fringe = (uv - 0.5) * _Chromatic * 2.0;
                float3 col;
                col.r = SampleSoft(uv + fringe, texel.x).r;
                col.g = SampleSoft(uv, texel.x).g;
                col.b = SampleSoft(uv - fringe, texel.x).b;
                float alpha = tex2D(_MainTex, uv).a;

                // Scanline beam: a gaussian across each line, wider for brighter pixels,
                // normalised so a line keeps roughly its average brightness
                float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
                float f = frac(uv.y * res.y / period) - 0.5;
                float beam = lerp(_BeamMin, _BeamMax, saturate(luma));
                float scan = exp(-(f * f) / (2.0 * beam * beam));
                scan *= 1.0 / clamp(beam * 2.5066, 0.4, 1.0);
                col *= lerp(1.0, scan, _ScanlineStrength);

                // RGB phosphors
                col *= PhosphorMask(IN.vertex.xy);

                // Halation: light spreading in the glass, filling the mask and scanline gaps
                float2 r1 = texel * 3.0;
                float2 r2 = texel * 7.0;
                float3 glow =
                    tex2D(_MainTex, uv + float2(r1.x, 0.0)).rgb + tex2D(_MainTex, uv - float2(r1.x, 0.0)).rgb +
                    tex2D(_MainTex, uv + float2(0.0, r1.y)).rgb + tex2D(_MainTex, uv - float2(0.0, r1.y)).rgb +
                    tex2D(_MainTex, uv + r2).rgb + tex2D(_MainTex, uv - r2).rgb +
                    tex2D(_MainTex, uv + float2(r2.x, -r2.y)).rgb + tex2D(_MainTex, uv + float2(-r2.x, r2.y)).rgb;
                col += glow * (0.125 * _Bloom);

                col *= _Brightness;

                // Rolling hum bar drifting down the screen
                float roll = (frac(uv.y + t * _RollSpeed) - 0.5) * 9.0;
                col *= 1.0 + exp(-roll * roll) * _RollStrength;

                // Flicker: fast mains ripple plus small random dips
                col *= 1.0 + _Flicker * (0.5 * sin(t * 113.0) + (Hash(float2(floor(t * 18.0), 3.1)) - 0.5));

                // Grain
                col += (Hash(uv * res + frac(t * 7.13) * 91.7) - 0.5) * _Noise;

                // Vignette
                float vig = 16.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y);
                col *= pow(max(saturate(vig), 1e-4), 0.25 * _Vignette);

                // Glass reflection near the top-left
                float2 g = (uv - float2(0.28, 0.80)) * float2(1.5, 3.2);
                col += _Glare * exp(-dot(g, g) * 2.5);

                // Bezel outside the curved screen
                float pictureAlpha = lerp(alpha, 1.0, _Opaque);
                float3 finalRgb = lerp(_BezelColor.rgb, saturate(col), inside);
                float finalAlpha = lerp(_BezelColor.a, pictureAlpha, inside);
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
