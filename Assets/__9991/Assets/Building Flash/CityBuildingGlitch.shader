Shader "OfficeFX/CityBuildingGlitch"
{
    Properties
    {
        [NoScaleOffset] _CityAtlas ("Two-band RGBA artwork", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+3" "RenderType"="Transparent" }
        Pass
        {
            Name "Facade tears"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CityAtlas);
            SAMPLER(sampler_CityAtlas);
            CBUFFER_START(UnityPerMaterial)
                float4x4 _CityUnit;
                float4 _CityRight, _CityBeat, _CityTuning;
            CBUFFER_END

            struct Attributes {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 unitPos : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.vertex.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.unitPos = mul(_CityUnit, float4(ws, 1.0)).xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normal);
                return o;
            }

            // Cheap stable spatial hash. No noise texture or frame-dependent random seed.
            float Hash1(float v)
            {
                v = frac(v * 0.1031);
                v *= v + 33.33;
                return frac(v * (v + v));
            }

            float3 ChipColor(float h)
            {
                if (h < 0.27) return float3(0.02, 0.83, 0.82);
                if (h < 0.48) return float3(0.10, 0.34, 0.98);
                if (h < 0.66) return float3(0.44, 0.17, 0.91);
                if (h < 0.83) return float3(0.80, 0.10, 0.55);
                if (h < 0.94) return float3(0.05, 0.67, 0.34);
                return float3(0.88, 0.59, 0.12);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                clip(0.67 - abs(n.y));  // dark roofs stay roofs; only overlay the sides
                float3 fwd = cross(_CityRight.xyz, float3(0, 1, 0));
                float nx = dot(n, _CityRight.xyz), nz = dot(n, fwd);
                bool side = abs(nx) > abs(nz);
                float u = side ? i.unitPos.z : i.unitPos.x;
                if ((side && nx > 0.0) || (!side && nz < 0.0)) u = 1.0 - u;
                float y = i.unitPos.y;

                float age = _CityBeat.x, duration = _CityBeat.y;
                float seed = _CityBeat.z + (side ? 17.0 : 0.0);
                float burst = _CityBeat.w;
                float upper = step(0.50, y);
                float k = seed + upper * 31.3;
                float cy = lerp(0.315, 0.707, upper) + (Hash1(k + 1.1) - 0.5) * 0.065;
                float h = lerp(0.223, 0.239, upper);
                float w = lerp(0.86, 0.94, upper);
                float cx = lerp(0.55, 0.48, upper) + (Hash1(k + 2.3) - 0.5) * 0.045;
                float2 uv = float2((u - cx) / w + 0.5, (cy - y) / h + 0.5);

                // Upper and lower ribbons enter at different times; each slice has its own clock.
                float delay = lerp(0.26, 0.02, upper) + 0.12 * Hash1(k + burst);
                float t = age - delay;
                float endTime = duration - lerp(0.04, 0.25, upper);
                float env = smoothstep(0.0, 0.16, t) * (1.0 - smoothstep(endTime - 0.23, endTime, age));
                float row = min(4.0, floor(saturate(uv.y) * 4.99));
                float col = floor(uv.x * 4.0 + 0.45 * Hash1(k + row * 4.3));
                float tile = k + 19.13 * row + 6.71 * col;
                float rate = lerp(1.16, 1.77, upper) * _CityTuning.z * lerp(0.85, 1.09, Hash1(tile));
                float ph = frac(t * rate + Hash1(tile + 3.8 + burst));
                float lit = smoothstep(0.035, 0.165, abs(ph - 0.5));
                float shift = (Hash1(tile + burst + 9.1) - 0.5) * 0.052 * (1.0 - lit);
                float2 uvArt = float2(uv.x + shift, uv.y);
                float2 pad = min(uvArt, 1.0 - uvArt);
                float inside = step(0.0, min(pad.x, pad.y));
                float tileTop = lerp(132.0, 4.0, upper);
                float2 atlasUv = float2(saturate(uvArt.x), 1.0 - (tileTop + 120.0 * saturate(uvArt.y)) / 256.0);
                half4 art = SAMPLE_TEXTURE2D(_CityAtlas, sampler_CityAtlas, atlasUv);
                float body = inside * art.a * lit;

                // Isolated chips in a halo around the two tears. At the outer edge both
                // probability and opacity fall; don't fill the gap between the two bands.
                float2 q = abs((float2(u,y) - float2(cx,cy)) / (0.5 * float2(w,h)));
                float radial = max(q.x, q.y);
                float halo = smoothstep(0.67, 1.02, radial) * (1.0 - smoothstep(1.02, 1.65, radial));
                float2 grid = float2(u * 12.0, y * 77.0) + float2(Hash1(k + 8.4), Hash1(k + 5.1));
                float2 g = floor(grid), f = frac(grid);
                float cell = k + 13.17 * g.x + 7.11 * g.y + burst;
                float choice = Hash1(cell);
                float density = saturate(_CityTuning.y);
                float occupied = step(1.0 - 0.42 * halo * density, choice) * step(0.001, density);
                float2 center = float2(0.5, 0.5) + (float2(Hash1(cell + 4.2), Hash1(cell + 2.5)) - 0.5) * 0.32;
                float2 ext = float2(lerp(0.10, 0.32, Hash1(cell + 6.7)), lerp(0.12, 0.28, Hash1(cell + 1.3)));
                float2 aa = clamp(fwidth(grid) * 0.5, 0.015, 0.49);
                float2 coverage = 1.0 - smoothstep(ext - aa, ext + aa, abs(f - center));
                float chipPhase = frac(t * _CityTuning.z * lerp(0.86, 1.91, Hash1(cell + 8.5)) + Hash1(cell + 11.1));
                float chip = occupied * coverage.x * coverage.y * halo * 0.79 *
                    smoothstep(0.06, 0.20, abs(chipPhase - 0.47));

                float alpha = max(body, chip) * env * saturate(_CityTuning.x);
                clip(alpha - 0.004);
                return half4(lerp(art.rgb, ChipColor(Hash1(cell + 6.1)), step(body, chip)), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
