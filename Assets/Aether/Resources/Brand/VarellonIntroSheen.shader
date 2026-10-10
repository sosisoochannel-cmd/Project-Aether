Shader "Sprites/VarellonIntroSheen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Logo Coverage", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SweepProgress ("Sweep Progress", Range(0,1)) = 0
        _SweepOpacity ("Sweep Opacity", Range(0,1)) = 0
        _BandHalfWidth ("Band Half Width", Range(0.12,0.22)) = 0.15
        _Tilt ("Tilt", Range(0,0.2)) = 0.08
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
            float4 _MainTex_ST;
            fixed4 _Color;
            float _SweepProgress;
            float _SweepOpacity;
            float _BandHalfWidth;
            float _Tilt;

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
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed coverage = tex2D(_MainTex, input.uv).a;
                float progress = saturate(_SweepProgress);
                float eased = progress * progress * (3.0 - 2.0 * progress);
                float centre = lerp(-_BandHalfWidth, 1.0 + _BandHalfWidth, eased);
                float across = (1.0 - _Tilt) * input.uv.x + _Tilt * (1.0 - input.uv.y);
                float distanceFromCrest = abs(across - centre);
                float band = 1.0 - smoothstep(0.0, _BandHalfWidth, distanceFromCrest);
                fixed alpha = coverage * input.color.a * band * saturate(_SweepOpacity);
                return fixed4(input.color.rgb, alpha);
            }
            ENDCG
        }
    }
}
