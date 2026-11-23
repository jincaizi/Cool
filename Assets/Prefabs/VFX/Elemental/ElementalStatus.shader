Shader "Cool/VFX/Elemental Status"
{
    Properties
    {
        _Tint ("Element", Color) = (0.3, 0.8, 1.0, 1.0)
        [Enum(Ice,0,Fire,1,Thunder,2,Wind,3)] _Element ("Pattern", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Element;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * 2.0 - 1.0;
                return o;
            }

            float Band(float distance, float width)
            {
                return 1.0 - smoothstep(width * 0.15, width, abs(distance));
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv;
                float radius = length(p);
                float angle = atan2(p.y, p.x + 0.0001);
                float time = _Time.y;
                float intensity = 0.0;

                if (_Element < 0.5)
                {
                    // Ice contracts into a faceted hexagon with six radial fractures.
                    float2 axis = abs(p);
                    float hex = max(axis.x, dot(axis, float2(0.5, 0.866)));
                    float fracture = pow(abs(cos(angle * 3.0)), 18.0);
                    intensity = Band(hex - 0.52, 0.05)
                        + fracture * Band(radius - 0.49, 0.23) * 0.75;
                }
                else if (_Element < 1.5)
                {
                    // Fire ripples out from the center with a broken, animated edge.
                    float flames = sin(angle * 9.0 - time * 6.0) * 0.08;
                    intensity = Band(radius - 0.48 - flames, 0.105) * 0.9
                        + Band(radius - 0.68 - flames, 0.045) * saturate(sin(angle * 13.0 + time * 8.0));
                }
                else if (_Element < 2.5)
                {
                    // Thunder orbits in short jagged arcs.
                    float phase = angle * 7.0 + time * 1.5;
                    float zigzag = abs(frac(phase) - 0.5) * 0.32;
                    intensity = Band(radius - 0.45 - zigzag, 0.046)
                        * smoothstep(-0.35, 0.78, sin(angle * 3.0 - time * 9.0));
                }
                else
                {
                    // Wind follows three rotating, tapered currents.
                    float sweep = angle * 3.0 + time * 3.5;
                    float curl = sin(sweep) * 0.09;
                    intensity = Band(radius - 0.64 - curl, 0.08) * smoothstep(-0.2, 0.9, cos(sweep))
                        + Band(radius - 0.43 - curl, 0.035) * smoothstep(0.15, 0.9, sin(sweep));
                }

                float alpha = saturate(intensity) * _Tint.a;
                return fixed4(lerp(_Tint.rgb, fixed3(1.0, 1.0, 1.0), alpha * 0.27), alpha);
            }
            ENDCG
        }
    }
}
