Shader "Cool/VFX/Sword Qi Energy"
{
    Properties
    {
        _Color ("Energy Color", Color) = (0.82, 0.89, 0.96, 1)
        _Profile ("Element (0 default, 1 ice, 2 fire, 3 thunder, 4 wind)", Float) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        [PerRendererData] _WaveFlight ("Flight (progress, distance, remaining)", Vector) = (0.35, 4.2, 1, 0)
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

            struct Interpolators
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            half4 _Color;
            half _Profile;
            half _Opacity;
            float4 _WaveFlight;

            float Triangle(float x)
            {
                return abs(frac(x) * 2.0 - 1.0);
            }

            Interpolators Vert(appdata_base input)
            {
                float u = input.texcoord.x;
                float wake = input.texcoord.y;
                float side = u * 2.0 - 1.0;
                float belly = saturate(1.0 - side * side);
                float taper = pow(belly, 0.72);
                float flight = _WaveFlight.x;
                float opening = smoothstep(0.0, 0.23, flight);
                float decay = smoothstep(0.63, 1.0, flight);
                float span = 0.71 + opening * 0.33 - decay * 0.19;
                float bank = 0.54 - opening * 0.12 + decay * 0.10;
                float sweep = 0.61 * belly - 0.24 + side * (0.06 - 0.16 * opening + 0.12 * decay);
                float depth = lerp(0.24, 1.89, smoothstep(0.0, 2.35, _WaveFlight.y));
                float turbulence = 0.0;

                if (_Profile > 3.5h)
                {
                    depth *= 1.14;
                    sweep += 0.08 * sin(u * 6.283);
                    bank *= 0.83;
                }
                else if (_Profile > 2.5h)
                {
                    turbulence = (Triangle(u * 5.3 + 0.24 * sin(u * 9.5) - flight * 2.0) - 0.5) * 0.10;
                }
                else if (_Profile > 1.5h)
                {
                    depth *= 1.05;
                    turbulence = (sin(u * 21.0 - flight * 10.0) * 0.045
                        + sin(u * 11.0 + flight * 8.0) * 0.028) * (0.42 + wake);
                }
                else if (_Profile > 0.5h)
                {
                    turbulence = (Triangle(u * 8.0) - 0.5) * 0.052;
                    depth *= 0.91;
                }

                // The trailing surface follows the full cutting edge and curls inward at the tips.
                float3 position;
                position.x = 1.49 * side * (span - wake * 0.18) - wake * (0.14 + wake * 0.14) * belly;
                position.y = 0.91 + side * bank + belly * (0.30 + side * 0.14) - decay * 0.10
                    - wake * (0.48 + 0.12 * side) * taper + turbulence * taper;
                position.z = sweep + turbulence * taper - wake * depth * taper * (0.88 + side * 0.23);

                Interpolators output;
                output.position = UnityObjectToClipPos(float4(position, 1.0));
                output.uv = input.texcoord.xy;
                return output;
            }

            half Band(float coordinate, float center, float width)
            {
                return 1.0h - smoothstep(0.0, width, abs(coordinate - center));
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                float u = input.uv.x;
                float wake = input.uv.y;
                float flight = _WaveFlight.x;
                float phase = u * 25.0 + wake * 10.0 - flight * 13.0;
                half pulse = sin(phase) * 0.5h + 0.5h;

                half rimCenter = 0.095h + 0.034h * u;
                half rimWidth = 0.105h;
                half density = 0.41h;
                half strands = Band(wake, 0.40h + sin(u * 6.5 - flight * 5.0) * 0.10h, 0.125h) * 0.10h;
                half breakup = sin(u * 15.0 - wake * 7.0 - flight * 6.0) * 0.045h;
                half3 color = _Color.rgb;

                if (_Profile > 3.5h)
                {
                    float curl = sin(u * 5.8 - flight * 6.0) * 0.10;
                    strands = Band(wake, 0.44 + curl, 0.08) * 0.27h
                        + Band(wake, 0.63 - curl, 0.055) * 0.14h;
                    rimWidth = 0.105h;
                    density = 0.24h;
                }
                else if (_Profile > 2.5h)
                {
                    float current = Triangle(u * 6.0 + wake * 0.42 - flight * 3.0);
                    rimCenter += (current - 0.5) * 0.065h;
                    strands = Band(wake, 0.27 + current * 0.32, 0.085)
                        * smoothstep(0.34, 0.65, pulse) * 0.31h;
                    density = 0.32h;
                }
                else if (_Profile > 1.5h)
                {
                    breakup += (pulse - 0.5h) * 0.20h;
                    strands = Band(wake, 0.30 + pulse * 0.43, 0.14) * pulse * 0.22h;
                    color = lerp(color, half3(1.0, 0.49, 0.095), pulse * wake * 0.58h);
                    density = 0.55h;
                }
                else if (_Profile > 0.5h)
                {
                    float crystal = Triangle(u * 12.0 - wake * 1.25);
                    strands = smoothstep(0.72, 0.98, crystal) * Band(wake, 0.41, 0.27) * 0.29h;
                    density = 0.51h;
                }

                half tipFade = smoothstep(0.0, 0.105, u) * (1.0h - smoothstep(0.88, 1.0, u));
                half envelope = smoothstep(0.0, 0.10, wake)
                    * (1.0h - smoothstep(0.43h + breakup, 1.0h + min(0.0h, breakup), wake));
                half edge = Band(wake, rimCenter, rimWidth);
                half body = density * (1.0h - wake) * (0.85h + 0.15h * pulse);
                half alpha = saturate((edge * 1.18h + body + strands) * envelope
                    * tipFade * _Color.a * _Opacity * smoothstep(0.0, 1.0, _WaveFlight.z));
                half3 radiance = lerp(color * (0.91h - wake * 0.15h), half3(1.0, 0.99, 0.96),
                    saturate(edge * 0.92h + strands * 0.55h));
                return half4(radiance * alpha, alpha);
            }
            ENDCG
        }
    }
}
