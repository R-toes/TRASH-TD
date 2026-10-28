// Solid-colour sprite silhouette for hit flashes (HitReaction).
// Keeps the sprite's alpha and fills it with the renderer colour, so a white renderer colour turns the
// whole sprite white; the colour's alpha fades the flash out.
Shader "TrashTD/SpriteFlash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [AlphaThreshold] _AlphaClip ("Alpha Clip", Float) = 0.5
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment SpriteFragFlash
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed4 SpriteFragFlash(v2f IN) : SV_Target
            {
                fixed a = SampleSpriteTexture(IN.texcoord).a * IN.color.a;
                return fixed4(IN.color.rgb * a, a);
            }
            ENDCG
        }
    }
}
