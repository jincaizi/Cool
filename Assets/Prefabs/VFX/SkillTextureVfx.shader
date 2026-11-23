Shader "Cool/VFX/Skill Texture"
{
    Properties
    {
        _MainTex ("Effect texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _EchoBlade ("Orient sword around travel axis", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct Input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _EchoBlade;

            Varyings Vert(Input input)
            {
                float4 position = input.vertex;
                if (_EchoBlade > 0.5h)
                {
                    float3 camera = ObjSpaceViewDir(float4(0, 0, 0, 1));
                    float2 across = float2(-camera.y, camera.x);
                    across *= rsqrt(max(dot(across, across), 0.0001));
                    position.xy = across * input.vertex.y;
                }

                Varyings output;
                output.position = UnityObjectToClipPos(position);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, input.uv) * input.color;
                return fixed4(c.rgb * c.a, c.a);
            }
            ENDCG
        }
    }
}
