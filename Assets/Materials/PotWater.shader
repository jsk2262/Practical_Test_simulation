// Water surface inside a pot/bowl, drawn on MovableBowl's flat "WaterFill" disc
// (a Unity cylinder primitive - its object-space XZ spans a 0.5-radius circle).
// Still water: clearly blue (readable at the game's small on-screen size) yet
// still see-through, a bright meniscus line with a slightly darker band just
// inside it. _Slosh makes the surface lap - the meniscus climbs one side then
// the other and ripples/reflections sway. _Heat adds the burner's warm glow and
// a few small pre-boil bubbles; _Boil fills the surface with rolling bubbles.
Shader "Kitchen/PotWater"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.72, 0.96, 0.45)
        _DeepColor ("Deep Color", Color) = (0.16, 0.44, 0.86, 0.7)
        _RimColor ("Rim Highlight", Color) = (1.0, 1.0, 1.0, 0.9)
        _HeatColor ("Heat Glow", Color) = (1.0, 0.42, 0.28, 1.0)
        _Heat ("Heat", Range(0, 1)) = 0
        _Boil ("Boil", Range(0, 1)) = 0
        _Slosh ("Slosh", Range(0, 1)) = 0.2
        _BubbleDensity ("Bubble Density", Float) = 7
        _BubbleSpeed ("Bubble Speed", Float) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _RimColor;
                half4 _HeatColor;
                half _Heat;
                half _Boil;
                half _Slosh;
                float _BubbleDensity;
                float _BubbleSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 disc : TEXCOORD0; // -1..1 across the disc
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.disc = v.positionOS.xz * 2.0;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // One layer of bubbles on a jittered grid. Each bubble grows, holds, then
            // pops and respawns on its own random phase. ring = bright bubble skin,
            // core = the thin see-through middle, spec = small highlight dot.
            void Bubbles(float2 p, float scale, float t, float amount,
                         inout float ring, inout float core, inout float spec)
            {
                float2 g = p * scale;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;

                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 cell = id + float2(x, y);
                    float h = Hash21(cell);
                    if (h > amount) continue;

                    float2 jitter = (float2(Hash21(cell + 7.1), Hash21(cell + 3.7)) - 0.5) * 0.6;
                    float phase = frac(t * _BubbleSpeed * (0.6 + h) + Hash21(cell + 1.3));
                    float rad = (0.14 + 0.32 * Hash21(cell + 9.2))
                              * smoothstep(0.0, 0.7, phase) * (1.0 - smoothstep(0.9, 1.0, phase));
                    if (rad < 0.02) continue;

                    float2 d2 = f - float2(x, y) - jitter;
                    float d = length(d2);
                    ring = max(ring, smoothstep(rad * 0.6, rad * 0.88, d) * (1.0 - smoothstep(rad * 0.88, rad, d)));
                    core = max(core, 1.0 - smoothstep(rad * 0.5, rad * 0.8, d));
                    spec = max(spec, 1.0 - smoothstep(0.0, rad * 0.22, length(d2 - float2(-0.35, 0.35) * rad)));
                }
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.disc;

                // Sloshing: the whole surface leans back and forth (two out-of-phase
                // swings so it circles a little instead of rocking on one axis).
                float2 lean = float2(sin(t * 2.1), cos(t * 1.7)) * _Slosh;
                // Rolling surface while boiling - a slight wobble of the whole surface.
                p += sin(p.yx * 9.0 + t * 4.0) * 0.012 * _Boil;
                float r = length(p);
                float side = dot(p, lean); // >0 on the side the water is climbing

                // Clear water, a touch denser toward the middle.
                half4 col = lerp(_DeepColor, _ShallowColor, saturate(r * 0.75 + 0.15));

                // Lapping ripples: rings from two drifting centers interfering.
                float2 c1 = lean * 0.35;
                float2 c2 = -lean * 0.25 + float2(0.2, -0.1);
                float ripple = sin(length(p - c1) * 20.0 - t * 3.0) * sin(length(p - c2) * 17.0 - t * 2.4);
                col.rgb += ripple * (0.02 + 0.06 * _Slosh) * (1.0 - _Boil);
                col.a += abs(ripple) * 0.04 * _Slosh;

                // Broad soft reflection that sways with the slosh.
                float refl = 1.0 - smoothstep(0.0, 0.55, length(p - float2(-0.25, 0.3) - lean * 0.12));
                col.rgb += refl * 0.12;
                col.a += refl * 0.05;

                // Burner glow seen through the water, strongest on the lower side.
                col.rgb += _HeatColor.rgb * _Heat * (0.12 + 0.3 * smoothstep(0.3, -1.0, p.y));
                col.a += _Heat * 0.06;

                // Bubbles: a few small ones while heating, full rolling boil once boiling.
                float amount = saturate(_Heat * 0.15 + _Boil * 0.6);
                if (amount > 0.001)
                {
                    float ring = 0, core = 0, spec = 0;
                    Bubbles(p, _BubbleDensity, t, amount, ring, core, spec);
                    Bubbles(p + 3.1, _BubbleDensity * 1.9, t * 1.3, amount * (0.5 + 0.5 * _Boil), ring, core, spec);

                    float inside = 1.0 - smoothstep(0.82, 0.92, r);
                    ring *= inside; core *= inside; spec *= inside;

                    col.rgb = lerp(col.rgb, col.rgb * 0.8 + _HeatColor.rgb * 0.12 * _Heat, core * 0.5);
                    col.rgb += ring * 0.4 + spec * 0.6;
                    col.a = saturate(col.a + ring * 0.35 + spec * 0.4 - core * 0.05);
                    col.rgb += _Boil * 0.04; // boiling water reads slightly brighter/cloudier
                }

                // Meniscus: a darker band just inside, then a bright thin line where
                // the water meets the wall - wider on the side the slosh is climbing.
                float climb = 0.04 * saturate(side * 1.5);
                float band = smoothstep(0.72 - climb, 0.86 - climb, r) * (1.0 - smoothstep(0.88 - climb, 0.93 - climb, r));
                col.rgb *= 1.0 - band * 0.18;
                col.a += band * 0.12;
                float line_ = smoothstep(0.90 - climb, 0.95 - climb, r);
                col.rgb = lerp(col.rgb, _RimColor.rgb, line_ * _RimColor.a);
                col.a = saturate(col.a + line_ * 0.55);

                // Anti-aliased outer edge.
                col.a *= 1.0 - smoothstep(0.97, 1.0, r);
                return col;
            }
            ENDHLSL
        }
    }
}
