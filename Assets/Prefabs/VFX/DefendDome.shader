Shader "Cool/VFX/Defend Dome"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Back
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            half4 _Color;

            Varyings Vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.normal = UnityObjectToWorldNormal(input.normal);
                output.uv = input.texcoord.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 view = normalize(lerp(UnityWorldSpaceViewDir(input.worldPosition),
                    UNITY_MATRIX_V[2].xyz, unity_OrthoParams.w));
                half edge = 1.0h - saturate(dot(normalize(input.normal), view));
                half rim = smoothstep(0.32h, 0.93h, edge);
                half baseGlow = 1.0h - smoothstep(0.0h, 0.13h, input.uv.y);
                half ribs = pow(saturate(cos(input.uv.x * 50.2655)), 18.0h) *
                    sin(input.uv.y * UNITY_PI);
                half sheen = smoothstep(0.94h, 1.0h, sin(input.uv.y * 14.0h - _Time.y * 3.0h));
                half alpha = _Color.a * (0.045h + 0.63h * rim + 0.18h * baseGlow + 0.11h * ribs + 0.05h * sheen);
                half3 gold = lerp(half3(1.0h, 0.53h, 0.20h), half3(1.0h, 0.87h, 0.48h), rim);
                half energy = alpha + _Color.a * (0.32h * rim + 0.05h * ribs);
                return half4(gold * _Color.rgb * energy, alpha);
            }
            ENDCG
        }
    }
}
