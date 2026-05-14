Shader "Custom/Outline"
{
    Properties
    {
        _Color ("Main Color", Color) = (1, 1, 1, 1)
        _MainTex ("Main Texture", 2D) = "white" {}

        [Header(Outline)]
        _OutlineColor ("Outline Color", Color) = (0.2, 0.5, 1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.1)) = 0.02
        _OutlineGlow ("Outline Glow", Range(0, 3)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        // Pass 0: Normal rendering with Lambert diffuse
        Pass
        {
            Name "BASE"
            Tags { "LightMode"="ForwardBase" }
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos    : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                float3 normal = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = saturate(dot(normal, lightDir));

                // Ambient (spherical harmonics)
                fixed3 ambient = ShadeSH9(half4(normal, 1));

                // Direct diffuse
                fixed3 diffuse = _LightColor0.rgb * albedo.rgb * NdotL;

                // Shadow
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                diffuse *= atten;

                fixed3 finalColor = ambient * albedo.rgb + diffuse;
                return fixed4(finalColor, albedo.a);
            }
            ENDCG
        }

        // Pass 1: Outline — clip-space vertex extrusion
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert_outline
            #pragma fragment frag_outline
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color  : COLOR;    // vertex color = smoothed normals (optional)
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            fixed4 _OutlineColor;
            float  _OutlineWidth;
            float  _OutlineGlow;

            v2f vert_outline(appdata v)
            {
                v2f o;

                // Use vertex color as smoothed normal if available,
                // otherwise fallback to vertex normal.
                // Vertex colors are stored in 0-1 range, decode to -1..1.
                float3 normal = v.normal;
                #if UNITY_COLORSPACE_GAMMA
                float3 smoothNormal = v.color.rgb * 2.0 - 1.0;
                #else
                float3 smoothNormal = GammaToLinearSpace(v.color.rgb) * 2.0 - 1.0;
                #endif
                // Use smooth normal when vertex color is not default (not 0.5,0.5,1.0)
                if (length(v.color.rgb) > 0.01)
                    normal = smoothNormal;

                normal = normalize(normal);

                // Transform normal to clip space for screen-space-consistent width
                float3 worldNormal = UnityObjectToWorldNormal(normal);
                float3 viewNormal  = mul((float3x3)UNITY_MATRIX_V, worldNormal);
                float2 clipNormal  = mul((float3x3)UNITY_MATRIX_P, float3(viewNormal.xy, 0)).xy;

                float4 clipPos = UnityObjectToClipPos(v.vertex);

                // Scale by w so outline width is roughly resolution-independent
                clipPos.xy += normalize(clipNormal) * _OutlineWidth * clipPos.w;

                o.vertex = clipPos;
                return o;
            }

            fixed4 frag_outline(v2f i) : SV_Target
            {
                return fixed4(_OutlineColor.rgb * _OutlineGlow, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
