// Old-TV CRT look for a UI RawImage showing a RenderTexture (see TrashTD.UI.CrtDisplay / CrtScreen).
// Modelled on how a real tube draws low-res pixel art:
//   - soft horizontal reconstruction (neighbouring pixels blend like an analogue signal)
//   - scanline beams whose width grows with brightness (bright pixels bloom across the dark gaps)
//   - an RGB phosphor mask (slot mask or aperture grille) that lights each colour's own phosphor and
//     dims the others, which is what makes small palettes shimmer with colour up close
//   - halation glow that fills the mask gaps, plus curvature, rounded corners and bezel, colour fringing,
//     grain, a rolling hum bar, flicker, line jitter, vignette and a glass reflection.
// _Legacy = 1 keeps the first, crisper version (sine scanlines, light stripe tint, small glow) for the
// Classic graphics option; _Passthrough = 1 shows the picture with brightness only.
// CrtScreen.Distort / IsOnScreen mirror Distort and ScreenMask here; keep them in sync.
// _CrtVirtualScale > 1 (Full TV below 4K): the full-look effects are laid out as if the picture were
// _CrtVirtualScale times taller (about 2160 lines), then filtered down to the real pixels instead of being
// redrawn per screen pixel, so low resolutions get the 4K look without the effects smearing small text.
Shader "TrashTD/UI/CRT"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "black" {}
        _UiTex ("UI Layer (set by CrtDisplay)", 2D) = "black" {}
        _HasUi ("Composite UI Layer", Float) = 0
        _Legacy ("Classic (first version) look", Float) = 0
        _Passthrough ("Passthrough (brightness only)", Float) = 0
        _HeatBlur ("Heat Flare Blur", Range(0, 1)) = 0
        _HeatWarp ("Heat Warp (strength in uv, band height 0-1)", Vector) = (0, 0.4, 0, 0)
        _HeatVentX ("Heat Vent X (uv, negative = off)", Vector) = (-1, -1, -1, -1)
        _HeatVentY ("Heat Vent Y (uv)", Vector) = (0, 0, 0, 0)
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
        _CrtVirtualScale ("Virtual resolution scale (set by script)", Float) = 1

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
            sampler2D _UiTex;
            float _HasUi;
            float _Legacy;
            float _Passthrough;
            float _HeatBlur;
            float4 _HeatWarp;
            float4 _HeatVentX;
            float4 _HeatVentY;
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
            float _CrtVirtualScale;

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

            // Stage 4 heat shimmer: how far the game picture is shoved sideways and up/down at uv. Rippling bands rise
            // from the floor (strongest at the bottom edge) and from columns above each steam vent. Only the game layer
            // is warped, never the UI.
            float2 HeatOffset(float2 uv)
            {
                float strength = _HeatWarp.x;
                if (strength < 0.00001) return float2(0.0, 0.0);

                float t = _CrtTime;
                float band = saturate(1.0 - uv.y / max(_HeatWarp.y, 0.05));
                float amount = band * band;

                float4 dx = uv.x - _HeatVentX;
                float4 rise = uv.y - _HeatVentY;
                float4 column = exp(-dx * dx * 700.0) * step(0.0, rise) * saturate(1.0 - rise / 0.5) * step(0.0, _HeatVentX);
                amount += (column.x + column.y + column.z + column.w) * 0.9;

                float w1 = sin(uv.y * 85.0 - t * 2.8 + sin(uv.x * 11.0 + t * 0.9) * 2.2);
                float w2 = sin(uv.y * 41.0 - t * 1.9 + uv.x * 23.0 + t * 0.4);
                float2 offset;
                offset.x = (w1 * 0.65 + w2 * 0.35) * amount;
                offset.y = sin(uv.x * 37.0 + t * 1.6 + uv.y * 20.0) * 0.3 * amount;
                return offset * strength;
            }

            // The picture at uv: the game layer, with the UI layer on top when CrtDisplay supplies one.
            // The UI texture is rendered with normal alpha blending onto transparent black, so its colour is
            // already multiplied by coverage and its alpha is coverage squared (single layer).
            float4 Pic(float2 uv)
            {
                float4 picture = tex2D(_MainTex, uv + HeatOffset(uv));
                if (_HasUi > 0.5)
                {
                    float4 ui = tex2D(_UiTex, uv);
                    float coverage = sqrt(saturate(ui.a));
                    picture.rgb = ui.rgb + picture.rgb * (1.0 - coverage);
                    picture.a = max(picture.a, coverage);
                }
                return picture;
            }

            float4 HeatPic(float2 uv)
            {
                float2 warped = uv + HeatOffset(uv);
                float4 picture = tex2D(_MainTex, warped);
                if (_HeatBlur > 0.001)
                {
                    float2 offset = _HeatBlur * 1.5 / max(_CrtResolution.xy, float2(1.0, 1.0));
                    picture.rgb = picture.rgb * 0.4
                        + tex2D(_MainTex, warped + float2(offset.x, 0.0)).rgb * 0.15
                        + tex2D(_MainTex, warped - float2(offset.x, 0.0)).rgb * 0.15
                        + tex2D(_MainTex, warped + float2(0.0, offset.y)).rgb * 0.15
                        + tex2D(_MainTex, warped - float2(0.0, offset.y)).rgb * 0.15;
                }

                if (_HasUi > 0.5)
                {
                    float4 ui = tex2D(_UiTex, uv);
                    float coverage = sqrt(saturate(ui.a));
                    picture.rgb = ui.rgb + picture.rgb * (1.0 - coverage);
                    picture.a = max(picture.a, coverage);
                }
                return picture;
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
                // Clamp so a tiny corner radius can't stretch the edge fade across the whole screen
                return 1.0 - smoothstep(1.0 - min(softness / r, 0.5), 1.0, dist);
            }

            fixed4 FinishUi(fixed4 color, v2f IN)
            {
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }

            // Sharp bilinear: nearly nearest-pixel inside each texel with a thin blended band at its edges, so
            // the curvature's resampling keeps 1-pixel strokes crisp. scale 1 = plain bilinear.
            float2 SharpUv(float2 uv, float scale)
            {
                float2 size = max(_CrtResolution.xy, float2(1.0, 1.0));
                float2 texel = uv * size;
                float2 base = floor(texel);
                float2 fromCentre = frac(texel) - 0.5;
                float region = 0.5 - 0.5 / max(scale, 1.0);
                float2 f = (fromCentre - clamp(fromCentre, -region, region)) * max(scale, 1.0) + 0.5;
                return (base + f) / size;
            }

            // Analogue-style horizontal reconstruction: neighbours blend in as sharpness drops
            float3 SampleSoft(float2 uv, float texelX, float sharpScale)
            {
                float side = 0.5 * (1.0 - _Sharpness);
                float3 c = Pic(SharpUv(uv, sharpScale)).rgb;
                float3 l = Pic(SharpUv(uv - float2(texelX, 0.0), sharpScale)).rgb;
                float3 r = Pic(SharpUv(uv + float2(texelX, 0.0), sharpScale)).rgb;
                return (c + (l + r) * side) / (1.0 + 2.0 * side);
            }

            // RGB phosphor mask; scale = output pixels per phosphor. Type 0: aperture grille (vertical RGB
            // stripes). Type 1: slot mask (RGB triads in short slots, alternate triads offset by half a slot).
            float3 PhosphorMask(float2 pixel, float scale)
            {
                float2 p = floor(pixel / max(scale, 1.0));
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

            // What the mask averages to from a distance (the same for every channel)
            float MaskMean()
            {
                float mean = (2.0 * _MaskDark + _MaskLight) / 3.0;
                if (_MaskType > 0.5) mean *= (3.0 + _MaskDark) / 4.0;
                return lerp(1.0, mean, _MaskStrength);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Passthrough: the picture as-is, only brightness (CrtDisplay in Classic mode)
                if (_Passthrough > 0.5)
                {
                    float4 plain = Pic(IN.texcoord);
                    if (_HeatBlur > 0.001)
                        plain = HeatPic(IN.texcoord);
                    fixed4 plainColor = fixed4(saturate(plain.rgb * _Brightness), lerp(plain.a, 1.0, _Opaque)) * IN.color;
                    return FinishUi(plainColor, IN);
                }

                float t = _CrtTime;
                float2 res = max(_CrtResolution.xy, float2(1.0, 1.0));
                float2 texel = 1.0 / res;
                float period = max(_ScanlinePeriod, 1.0);
                // Full TV lays its effects out on the virtual (about 4K) picture; 1 for every other style
                float virtualScale = max(_CrtVirtualScale, 1.0);
                float2 virtualRes = res * virtualScale;
                float2 virtualTexel = texel / virtualScale;

                float2 uv = Distort(IN.texcoord);

                // Faint per-line horizontal jitter
                float lineIndex = floor(uv.y * virtualRes.y / period);
                uv.x += (Hash(float2(lineIndex, floor(t * 24.0))) - 0.5) * _Jitter;

                float inside = ScreenMask(uv, 0.003);

                float2 fringe = (uv - 0.5) * _Chromatic * 2.0;
                float3 col;
                float alpha;

                if (_Legacy > 0.5)
                {
                    // Classic: the first CRT version, kept exactly. Direct samples, a small glow,
                    // sine scanlines that bright pixels wash out, and a light RGB stripe tint.
                    float4 center = Pic(uv);
                    col.r = Pic(uv + fringe).r;
                    col.g = center.g;
                    col.b = Pic(uv - fringe).b;
                    alpha = center.a;

                    float2 o1 = float2(texel.x * 2.5, 0.0);
                    float2 o2 = float2(0.0, texel.y * 2.5);
                    float3 blur = (Pic(uv + o1).rgb + Pic(uv - o1).rgb + Pic(uv + o2).rgb + Pic(uv - o2).rgb) * 0.25;
                    col += blur * blur * _Bloom * 0.6;

                    float classicLuma = dot(col, float3(0.299, 0.587, 0.114));
                    float classicScan = 0.5 + 0.5 * sin(uv.y * res.y * 6.2831853 / period);
                    classicScan = lerp(classicScan, 1.0, saturate(classicLuma * 0.6));
                    col *= lerp(1.0, classicScan, _ScanlineStrength);

                    float column = fmod(floor(IN.vertex.x), 3.0);
                    float3 grille = column < 0.5 ? float3(1.0, 0.0, 0.0) : (column < 1.5 ? float3(0.0, 1.0, 0.0) : float3(0.0, 0.0, 1.0));
                    col *= lerp(float3(1.0, 1.0, 1.0), lerp(float3(0.55, 0.55, 0.55), float3(1.0, 1.0, 1.0), grille), _MaskStrength);
                }
                else
                {
                    // Picture with colour fringing toward the edges
                    col.r = SampleSoft(uv + fringe, virtualTexel.x, virtualScale).r;
                    col.g = SampleSoft(uv, virtualTexel.x, virtualScale).g;
                    col.b = SampleSoft(uv - fringe, virtualTexel.x, virtualScale).b;
                    alpha = Pic(uv).a;

                    // Scanline beam: a gaussian across each line, wider for brighter pixels,
                    // normalised so a line keeps roughly its average brightness. Averaged over the
                    // pixel's height, so lines only a few screen pixels tall soften instead of cutting
                    // through text; under 3 screen pixels a line also fades a little.
                    float luma = dot(col, float3(0.2126, 0.7152, 0.0722));
                    float beam = lerp(_BeamMin, _BeamMax, saturate(luma));
                    float lineCoord = uv.y * virtualRes.y / period;
                    float linesPerPixel = virtualScale / period;
                    float scan = 0.0;
                    [unroll] for (int k = 0; k < 4; k++)
                    {
                        float f = frac(lineCoord + ((k + 0.5) * 0.25 - 0.5) * linesPerPixel) - 0.5;
                        scan += exp(-(f * f) / (2.0 * beam * beam));
                    }
                    scan *= 0.25 / clamp(beam * 2.5066, 0.4, 1.0);
                    float linePixels = period / virtualScale;
                    col *= lerp(1.0, scan, _ScanlineStrength * saturate((linePixels - 1.5) / 1.5));

                    // RGB phosphors. A phosphor never gets narrower than one screen pixel (sub-pixel
                    // stripes alias into colour noise on text); the lost detail fades to the mask's
                    // average, so the picture keeps the same tone as at 4K.
                    float phosphorPixels = _MaskScale / virtualScale;
                    float3 mask = PhosphorMask(IN.vertex.xy, max(phosphorPixels, 1.0));
                    col *= lerp(MaskMean().xxx, mask, saturate(phosphorPixels));

                    // Halation: light spreading in the glass, filling the mask and scanline gaps
                    float2 r1 = virtualTexel * 3.0;
                    float2 r2 = virtualTexel * 7.0;
                    float3 glow =
                        Pic(uv + float2(r1.x, 0.0)).rgb + Pic(uv - float2(r1.x, 0.0)).rgb +
                        Pic(uv + float2(0.0, r1.y)).rgb + Pic(uv - float2(0.0, r1.y)).rgb +
                        Pic(uv + r2).rgb + Pic(uv - r2).rgb +
                        Pic(uv + float2(r2.x, -r2.y)).rgb + Pic(uv + float2(-r2.x, r2.y)).rgb;
                    col += glow * (0.125 * _Bloom);
                }

                if (_HeatBlur > 0.001)
                    col = lerp(col, HeatPic(uv).rgb, _HeatBlur * 0.55);

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
                return FinishUi(fixed4(finalRgb, finalAlpha) * IN.color, IN);
            }
            ENDCG
        }
    }
}
