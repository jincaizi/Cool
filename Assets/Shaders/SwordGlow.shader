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
        // 沿剑脊流动的符文/光纹遮罩(R通道)。黑色纹理=无流动效果
        [NoScaleOffset] _FlowTex ("Flow Texture", 2D) = "black" {}
        // 符文流动方向和速度。正=向UV上方，负=反向
        _FlowSpeed ("Flow Speed", Range(-2, 2)) = 0.5
        // 符文流光叠加强度。0=关闭（跳过纹理采样），0.8=微妙流动，3=强烈光纹
        _FlowIntensity ("Flow Intensity", Range(0, 3)) = 0.0

        [Header(Rim)]
        // 边缘光颜色。模拟环境补光，防止暗面死黑
        _RimColor ("Rim Color", Color) = (0.3, 0.5, 0.8, 1)
        // 边缘光锐度。越小范围越宽，越大越集中。3=典型武器边缘补光
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0

        [Header(Lighting)]
        // 环境光常量。替代昂贵的 SH 球谐求值，移动端友好
        _AmbientColor ("Ambient Color", Color) = (0.15, 0.15, 0.18, 1)
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

            // 最小变体集：仅方向光，无阴影/Lightmap/SH
            #pragma multi_compile _ DIRECTIONAL

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                half2  uv          : TEXCOORD0;
                half3  worldNormal : TEXCOORD1;
                half3  viewDir     : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            half4     _Color;

            half4 _CoreColor;
            half4 _EdgeColor;
            half  _GlowIntensity;
            half  _GradientPower;

            half  _PulseSpeed;
            half  _PulseMin;

            sampler2D _FlowTex;
            half      _FlowSpeed;
            half      _FlowIntensity;

            half4 _RimColor;
            half  _RimPower;

            half4 _AmbientColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos         = UnityObjectToClipPos(v.vertex);
                o.uv          = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = (half3)UnityObjectToWorldNormal(v.normal);
                o.viewDir     = (half3)WorldSpaceViewDir(v.vertex);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 albedo = tex2D(_MainTex, i.uv) * _Color;

                // ---- Lighting (移动端精简) ----
                half3 normal   = normalize(i.worldNormal);
                half3 lightDir = (half3)_WorldSpaceLightPos0.xyz;
                half3 viewDir  = normalize(i.viewDir);
                half  NdotL    = saturate(dot(normal, lightDir));
                half  NdotV    = saturate(dot(normal, viewDir));

                half3 ambient  = _AmbientColor.rgb;
                half3 diffuse  = _LightColor0.rgb * albedo.rgb * NdotL;

                // Rim light — half-precision pow, 移动端硬件加速
                half rim       = 1.0 - NdotV;
                half3 rimLight = _RimColor.rgb * pow(rim, _RimPower);

                half3 litColor = ambient * albedo.rgb + diffuse + rimLight;

                // ---- Glow gradient (UV-space) ----
                half distToCenter = abs(i.uv.x - 0.5) * 2.0;
                // mobile fast pow: exp2(log2(x) * power)
                half glowFactor = exp2(log2(max(distToCenter, 0.0001)) * _GradientPower);
                half3 glowColor = lerp(_CoreColor.rgb, _EdgeColor.rgb, glowFactor);

                // ---- Pulse ----
                half pulse = sin(_Time.y * _PulseSpeed * 6.283185) * 0.5 + 0.5;
                pulse = lerp(_PulseMin, 1.0, pulse);

                half3 glow = glowColor * _GlowIntensity * pulse;

                // ---- Flow texture (运行时跳过：FlowIntensity=0 时不采样) ----
                if (_FlowIntensity > 0.001)
                {
                    half2 flowUV  = i.uv + half2(0, _Time.y * _FlowSpeed);
                    half  flowMask = tex2D(_FlowTex, flowUV).r;
                    glow += flowMask * _CoreColor.rgb * _FlowIntensity * pulse;
                }

                // ---- Composite ----
                half3 finalColor = litColor + glow;
                return half4(finalColor, albedo.a);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
