Shader "UI/VarellonLogoSheen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SweepProgress ("Sweep Progress", Range(0,1)) = 0
        _SweepOpacity ("Sweep Opacity", Range(0,1)) = 0
        _BandHalfWidth ("Band Half Width", Range(0.02,0.3)) = 0.12

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
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            Name "VarellonLogoSheen"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float _SweepProgress;
            float _SweepOpacity;
            float _BandHalfWidth;

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
            };

            v2f vert(appdata_t input)
            {
                v2f output;
                output.localPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed coverage = tex2D(_MainTex, input.uv).a;
                float progress = saturate(_SweepProgress);
                float eased = progress * progress * (3.0 - 2.0 * progress);
                float centre = lerp(-0.16, 1.16, eased);

                // A very slight diagonal gives the light a deliberate, crafted direction.
                // The band is multiplied by the original logo alpha, so no glow spills into the UI.
                float across = input.uv.x + (input.uv.y - 0.5) * 0.14;
                float distanceFromCrest = abs(across - centre);
                float band = 1.0 - smoothstep(_BandHalfWidth * 0.18,
                                              _BandHalfWidth, distanceFromCrest);
                fixed alpha = coverage * input.color.a * band * saturate(_SweepOpacity);

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(input.localPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(input.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
