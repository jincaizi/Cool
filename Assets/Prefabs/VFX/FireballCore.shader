Shader "Cool/VFX/Fireball Core"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Input
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 surface : TEXCOORD0;
                float facing : TEXCOORD1;
            };

            Varyings Vert(Input input)
            {
                float3 surface = input.vertex.xyz;
                float wake = saturate(0.5 - surface.z);
                float flicker = sin(dot(surface, float3(16, 26, -21)) - _Time.y * 17);
                float4 position = input.vertex;
                position.xyz += input.normal * (0.08 * wake * flicker);
                position.z -= 0.16 * wake * wake;

                Varyings output;
                output.position = UnityObjectToClipPos(position);
                output.surface = surface;
                output.facing = saturate(dot(input.normal, normalize(ObjSpaceViewDir(position))));
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.surface;
                float flow = p.z * 18 + _Time.y * 23;
                float flame = sin(flow + sin(15 * p.y + 9 * p.x))
                            + 0.5 * sin(28 * p.x - 13 * p.y + flow * 0.7);
                float heat = saturate(0.40 + 0.24 * flame + 0.35 * input.facing + 0.28 * p.z);
                half3 red = half3(0.95, 0.13, 0.012);
                half3 orange = half3(1.0, 0.46, 0.032);
                half3 gold = half3(1.0, 0.93, 0.57);
                half3 color = lerp(red, orange, smoothstep(0.17, 0.60, heat));
                color = lerp(color, gold, smoothstep(0.47, 0.96, heat));
                return fixed4(color, 1);
            }
            ENDCG
        }
    }
}
