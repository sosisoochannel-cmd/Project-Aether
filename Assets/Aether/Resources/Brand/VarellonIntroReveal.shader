Shader "Sprites/VarellonIntroReveal"
{
    Properties
    {
        [PerRendererData] _MainTex ("Logo Coverage", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _RevealProgress ("Reveal Progress", Range(0,1)) = 0
        _RevealFeather ("Reveal Edge Softness", Range(0.03,0.25)) = 0.12
        _RevealTilt ("Reveal Edge Tilt", Range(0,0.25)) = 0.10
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
                fixed alpha = coverage * input.color.a * matte;
                return fixed4(input.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
