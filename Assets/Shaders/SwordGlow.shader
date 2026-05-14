Shader "Custom/SwordGlow"
{
    Properties
    {
        // 模型基础颜色，与 MainTex 相乘
        _Color ("Main Color", Color) = (1, 1, 1, 1)
        // 武器漫反射贴图
        _MainTex ("Main Texture", 2D) = "white" {}

        [Header(Gradient)]
        // 剑脊中心区域颜色，通常为亮白色
        _CoreColor ("Core Color (center)", Color) = (1, 1, 1, 1)
        // 剑刃边缘辉光颜色。蓄力时 SwordGlowVFX 设为冰蓝，熄辉时设为黑色
        _EdgeColor ("Glow Color (edge)", Color) = (0.2, 0.5, 1, 1)
        // 辉光整体亮度倍率。0=无辉光，1.5=标准亮度，5=极亮
        _GlowIntensity ("Glow Intensity", Range(0, 5)) = 1.5
        // 渐变陡峭度。越大辉光越集中在刃边，越小越均匀。1=线性，2=快速衰减，4=锐利
        _GradientPower ("Gradient Power", Range(0.5, 4)) = 1.5

        [Header(Pulse)]
        // 呼吸脉冲频率(Hz)。0=不脉冲常亮，2=每秒两个完整周期
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 2.0
        // 脉冲最低亮度比例。0=完全暗掉，0.3=最暗时30%亮度，1=不脉冲
        _PulseMin ("Pulse Min", Range(0, 1)) = 0.3

        [Header(Flow)]
        // 沿剑脊流动的符文/光纹遮罩(R通道)。黑色=无流动效果
        [NoScaleOffset] _FlowTex ("Flow Texture", 2D) = "black" {}
        // 符文流动方向和速度。正=向UV上方，负=反向
        _FlowSpeed ("Flow Speed", Range(-2, 2)) = 0.5
        // 符文流光叠加强度。0=无，0.8=微妙流动，3=强烈光纹
        _FlowIntensity ("Flow Intensity", Range(0, 3)) = 0.8

        [Header(Rim)]
        // 边缘光颜色。模拟环境补光，防止暗面死黑
        _RimColor ("Rim Color", Color) = (0.3, 0.5, 0.8, 1)
        // 边缘光锐度。越小范围越宽，越大越集中。3=典型武器边缘补光
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        Pass
        {
            Name "FORWARD"
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

            fixed4 _CoreColor;
            fixed4 _EdgeColor;
            float  _GlowIntensity;
            float  _GradientPower;

            float  _PulseSpeed;
            float  _PulseMin;

            sampler2D _FlowTex;
            float     _FlowSpeed;
            float     _FlowIntensity;

            fixed4 _RimColor;
            float  _RimPower;

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

                // ---- Lighting ----
                float3 normal   = normalize(i.worldNormal);
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 viewDir  = normalize(i.viewDir);
                float  NdotL    = saturate(dot(normal, lightDir));
                float  NdotV    = saturate(dot(normal, viewDir));

                fixed3 ambient = ShadeSH9(half4(normal, 1));
                fixed3 diffuse = _LightColor0.rgb * albedo.rgb * NdotL;

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                diffuse *= atten;

                // Rim light
                float rim = 1.0 - NdotV;
                rim = pow(rim, _RimPower);
                fixed3 rimLight = _RimColor.rgb * rim;

                fixed3 litColor = ambient * albedo.rgb + diffuse + rimLight;

                // ---- Glow gradient (UV-space) ----
                float distToCenter = abs(i.uv.x - 0.5) * 2.0;
                float glowFactor   = pow(distToCenter, _GradientPower);
                fixed3 glowColor   = lerp(_CoreColor.rgb, _EdgeColor.rgb, glowFactor);

                // ---- Pulse ----
                float pulse = sin(_Time.y * _PulseSpeed * 6.283185) * 0.5 + 0.5;
                pulse = lerp(_PulseMin, 1.0, pulse);

                float3 glow = glowColor * _GlowIntensity * pulse;

                // ---- Flow texture ----
                float2 flowUV = i.uv + float2(0, _Time.y * _FlowSpeed);
                fixed  flowMask = tex2D(_FlowTex, flowUV).r;
                glow += flowMask * _EdgeColor.rgb * _FlowIntensity * pulse;

                // ---- Composite ----
                fixed3 finalColor = litColor + glow;
                return fixed4(finalColor, albedo.a);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
