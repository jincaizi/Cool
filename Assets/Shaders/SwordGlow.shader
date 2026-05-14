Shader "Custom/SwordGlow"
{
    Properties
    {
        // 模型基础颜色，与 MainTex 相乘
        _Color ("Main Color", Color) = (1, 1, 1, 1)
        // 武器漫反射贴图
        _MainTex ("Main Texture", 2D) = "white" {}

        [Header(Gradient)]
        // 剑面核心辉光颜色（正对摄像机的面），通常亮白
        _CoreColor ("Core Color (center)", Color) = (1, 1, 1, 1)
        // 剑刃边缘辉光颜色（掠射角）。蓄力时设为冰蓝，熄辉时设为黑色
        _EdgeColor ("Glow Color (edge)", Color) = (0.2, 0.5, 1, 1)
        // 辉光整体亮度倍率。0=无辉光，1.5=标准，5=极亮
        _GlowIntensity ("Glow Intensity", Range(0, 5)) = 1.5
        // 边缘辉光锐度。越大辉光越集中在物理刃边。3=锐利刀刃感
        _EdgePower ("Edge Power", Range(0.5, 8)) = 3.0
        // 核心辉光收敛度。越大核心越集中在正对摄像机的面中心。1=均匀
        _CorePower ("Core Power", Range(0.5, 8)) = 1.0

        [Header(Pulse)]
        // 呼吸脉冲频率(Hz)。0=不脉冲常亮，2=每秒两个完整周期
        _PulseSpeed ("Pulse Speed", Range(0, 5)) = 2.0
        // 脉冲最低亮度比例。0=完全暗掉，0.3=最暗时30%亮度，1=不脉冲
        _PulseMin ("Pulse Min", Range(0, 1)) = 0.3

        [Header(Flow)]
        // 沿世界空间流动的光纹遮罩(R通道)。黑色纹理=无流动效果
        [NoScaleOffset] _FlowTex ("Flow Texture", 2D) = "black" {}
        // 光纹颜色。独立于辉光颜色，通常设为亮白或淡蓝
        _FlowColor ("Flow Color", Color) = (1, 1, 1, 1)
        // 光纹在世界空间中的流动方向。(0,1,0)=竖直，(1,0,0)=水平，(0,0,1)=纵深
        _FlowDirection ("Flow Direction", Vector) = (0, 1, 0)
        // 光纹密度。值越大条纹越密集。0.5=宽条纹，3=细密光纹
        _FlowDensity ("Flow Density", Range(0.1, 5)) = 0.8
        // 光纹流动速度。正=沿 Direction 正向，负=反向
        _FlowSpeed ("Flow Speed", Range(-2, 2)) = 0.35
        // 光纹叠加强度。0=关闭（跳过采样），1.2=清晰可见，3=强烈
        _FlowIntensity ("Flow Intensity", Range(0, 3)) = 1.2

        [Header(Rim)]
        // 边缘补光颜色。模拟环境光在边缘的反射，防止暗面死黑
        _RimColor ("Rim Color", Color) = (0.3, 0.5, 0.8, 1)
        // 边缘补光锐度。越小范围越宽，越大越集中在掠射角
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
                half3  worldPos    : TEXCOORD2;
                half3  viewDir     : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4    _MainTex_ST;
            half4     _Color;

            half4 _CoreColor;
            half4 _EdgeColor;
            half  _GlowIntensity;
            half  _EdgePower;
            half  _CorePower;

            half  _PulseSpeed;
            half  _PulseMin;

            sampler2D _FlowTex;
            half4     _FlowColor;
            half3     _FlowDirection;
            half      _FlowDensity;
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
                o.worldPos    = (half3)mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir     = (half3)WorldSpaceViewDir(v.vertex);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 albedo = tex2D(_MainTex, i.uv) * _Color;

                // ---- Lighting ----
                half3 normal   = normalize(i.worldNormal);
                half3 lightDir = (half3)_WorldSpaceLightPos0.xyz;
                half3 viewDir  = normalize(i.viewDir);
                half  NdotL    = saturate(dot(normal, lightDir));
                half  NdotV    = saturate(dot(normal, viewDir));

                half3 ambient  = _AmbientColor.rgb;
                half3 diffuse  = _LightColor0.rgb * albedo.rgb * NdotL;

                // Rim light
                half rim       = 1.0 - NdotV;
                half3 rimLight = _RimColor.rgb * pow(rim, _RimPower);

                half3 litColor = ambient * albedo.rgb + diffuse + rimLight;

                // ---- Glow: NdotV-based (完全不依赖 UV) ----
                // 剑面(正对摄像机) = NdotV 高 → 亮白核心辉光
                // 剑刃(物理边缘) = NdotV 低 → 冰蓝边缘辉光
                half edgeFactor  = pow(1.0 - NdotV, _EdgePower);
                half coreFactor  = pow(NdotV, _CorePower);
                half3 glowColor  = _CoreColor.rgb * coreFactor
                                 + _EdgeColor.rgb * edgeFactor;

                // ---- Pulse ----
                half pulse = sin(_Time.y * _PulseSpeed * 6.283185) * 0.5 + 0.5;
                pulse = lerp(_PulseMin, 1.0, pulse);

                half3 glow = glowColor * _GlowIntensity * pulse;

                // ---- Flow: world-space (完全不依赖 UV) ----
                if (_FlowIntensity > 0.001)
                {
                    half flowCoord = dot(i.worldPos, normalize(_FlowDirection))
                                   * _FlowDensity + _Time.y * _FlowSpeed;
                    half2 flowUV   = half2(0.5, frac(flowCoord));
                    half  flowMask = tex2D(_FlowTex, flowUV).r;
                    glow += flowMask * _FlowColor.rgb * _FlowIntensity * pulse;
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
