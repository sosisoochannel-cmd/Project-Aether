Shader "Sprites/VarellonIntroReveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Logo Coverage", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RevealProgress ("Reveal Progress", Range(0,1)) = 0
        _RevealFeather ("Reveal Edge Softness", Range(0.03,0.25)) = 0.145
        _RevealTilt ("Reveal Edge Tilt", Range(0,0.25)) = 0.085
        _RevealEdgeColor ("Reveal Crest Light", Color) = (0.98,0.88,0.70,1)
        _RevealEdgeStrength ("Reveal Crest Strength", Range(0,1)) = 0.62
        _RevealEdgeWidth ("Reveal Crest Width", Range(0.01,0.15)) = 0.045
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _RevealProgress;
            float _RevealFeather;
            float _RevealTilt;
            fixed4 _RevealEdgeColor;
            float _RevealEdgeStrength;
            float _RevealEdgeWidth;

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
            };

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed coverage = tex2D(_MainTex, input.uv).a;

                // A soft, slightly diagonal matte reveals only the logo's own ink.
                // Progress 0 is fully hidden; progress 1 is the complete, untouched logo.
                float coordinate = saturate(input.uv.x + (input.uv.y - 0.5) * _RevealTilt);
                float boundary = lerp(1.0 + _RevealFeather, -_RevealFeather,
                                      saturate(_RevealProgress));
                float matte = smoothstep(boundary - _RevealFeather,
                                         boundary + _RevealFeather, coordinate);
                // A narrow antique-gold crest rides the moving matte edge. It is clipped by the
                // logo coverage, so the light can polish the Varellon ink but can never spill onto
                // the black field like a generic screen-space flare.
                float edgeDistance = abs(coordinate - boundary);
                float crest = (1.0 - smoothstep(0.0, _RevealEdgeWidth, edgeDistance))
                            * smoothstep(0.0, 0.16, matte);
                fixed3 ink = lerp(input.color.rgb, _RevealEdgeColor.rgb,
                                  saturate(crest * _RevealEdgeStrength));
                fixed alpha = coverage * input.color.a * matte;
                return fixed4(ink, alpha);
            }
            ENDCG
        }
    }
}
