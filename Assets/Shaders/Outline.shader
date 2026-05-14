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
        _OutlineFresnelPower ("Outline Fresnel Power", Range(0.5, 5)) = 2.0

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (0.3, 0.5, 0.8, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        // ============================================================
        // Pass 0: Base rendering — Lambert diffuse + SH ambient + rim light
        // ============================================================
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
                float4 pos         : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos    : TEXCOORD2;
                float3 viewDir     : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            fixed4    _Color;
            fixed4    _RimColor;
            float     _RimPower;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos         = UnityObjectToClipPos(v.vertex);
                o.uv          = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir     = WorldSpaceViewDir(v.vertex);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                float3 normal   = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 viewDir  = normalize(i.viewDir);
                float  NdotL    = saturate(dot(normal, lightDir));
                float  NdotV    = saturate(dot(normal, viewDir));

                // Ambient (spherical harmonics)
                fixed3 ambient = ShadeSH9(half4(normal, 1));

                // Direct diffuse
                fixed3 diffuse = _LightColor0.rgb * albedo.rgb * NdotL;

                // Shadow attenuation
                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                diffuse *= atten;

                // Rim light — fresnel-based edge glow keeps weapon from looking flat
                float rim = 1.0 - NdotV;
                rim = pow(rim, _RimPower);
                fixed3 rimLight = _RimColor.rgb * rim;

                fixed3 finalColor = ambient * albedo.rgb + diffuse + rimLight;
                return fixed4(finalColor, albedo.a);
            }
            ENDCG
        }

        // ============================================================
        // Pass 1: Outline — object-space extrusion + fresnel gradient
        // ============================================================
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
                float4 color  : COLOR;    // optional baked smoothed normals
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float  fresnel : TEXCOORD0;
            };

            fixed4 _OutlineColor;
            float  _OutlineWidth;
            float  _OutlineGlow;
            float  _OutlineFresnelPower;

            v2f vert_outline(appdata v)
            {
                v2f o;

                // ---- Normal selection ----
                // Prefer vertex-color baked smooth normals, fallback to vertex normal.
                float3 n = v.normal;
                {
                    float3 smoothN = v.color.rgb * 2.0 - 1.0; // decode 0..1 → -1..1
                    if (dot(v.color.rgb, v.color.rgb) > 0.001)
                        n = smoothN;
                }
                n = normalize(n);

                // ---- Extract uniform scale to keep width consistent ----
                float3 scale = float3(
                    length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20)),
                    length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21)),
                    length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22))
                );
                float uniformScale = (scale.x + scale.y + scale.z) / 3.0;

                // ---- Object-space extrusion ----
                float4 extruded = v.vertex;
                extruded.xyz += n * (_OutlineWidth / uniformScale);

                // ---- Fresnel for outline gradient ----
                float3 worldNormal = UnityObjectToWorldNormal(n);
                float3 viewDir     = WorldSpaceViewDir(v.vertex);
                float  NdotV       = abs(dot(normalize(worldNormal), normalize(viewDir)));
                float  fresnel     = pow(1.0 - NdotV, _OutlineFresnelPower);

                o.vertex = UnityObjectToClipPos(extruded);
                o.fresnel = fresnel;
                return o;
            }

            fixed4 frag_outline(v2f i) : SV_Target
            {
                float alpha = saturate(i.fresnel);
                return fixed4(_OutlineColor.rgb * _OutlineGlow * alpha, alpha);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
