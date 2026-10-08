Shader "CuteMagic/Mobile_CelShader"
{
    Properties
    {
        [Header(Surface Textures)]
        [MainColor] _BaseColorTint("Base Color Tint", Color) = (1,1,1,1)
        [MainTexture] _AlbedoMap("Albedo Map (RGB)", 2D) = "white" {}
        
        [NoScaleOffset] _NormalMapInflow("Normal Map", 2D) = "bump" {}
        _NormalIntensity("Normal Intensity", Range(0, 2)) = 1.0
        
        [Header(PBR Mask Settings)]
        [NoScaleOffset] _CombinedMask("PBR Mask (R:Met, G:AO, B:Rough)", 2D) = "white" {}

        [Header(Toon Lighting Logic)]
        _ShadingThreshold("Shading Cutoff", Range(0, 1)) = 0.5
        _ShadingSoftness("Shading Softness", Range(0, 0.1)) = 0.01
        _ShadowTintMultiplier("Shadow Color Tint", Color) = (0.7, 0.7, 0.9, 1)

        [Header(Specular Highlights)]
        _SpecularGlowIntensity("Specular Strength", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardToon"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex VertexStage
            #pragma fragment FragmentStage
            
            // Standard URP multi-compile keywords for lighting and shadows
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct MeshData
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
            };

            struct Interpolators
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD3;
                float3 viewDirWS  : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _AlbedoMap_ST;
                half4 _BaseColorTint;
                half4 _ShadowTintMultiplier;
                half _ShadingThreshold;
                half _ShadingSoftness;
                half _NormalIntensity;
                half _SpecularGlowIntensity;
            CBUFFER_END

            TEXTURE2D(_AlbedoMap);      SAMPLER(sampler_AlbedoMap);
            TEXTURE2D(_NormalMapInflow); SAMPLER(sampler_NormalMapInflow);
            TEXTURE2D(_CombinedMask);    SAMPLER(sampler_CombinedMask);

            Interpolators VertexStage(MeshData v)
            {
                Interpolators o = (Interpolators)0;
                
                VertexPositionInputs posInput = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = posInput.positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _AlbedoMap);

                VertexNormalInputs normInput = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.normalWS = normInput.normalWS;
                o.tangentWS = float4(normInput.tangentWS, v.tangentOS.w);
                
                o.viewDirWS = GetWorldSpaceViewDir(posInput.positionWS);
                o.shadowCoord = GetShadowCoord(posInput);
                
                return o;
            }

            half4 FragmentStage(Interpolators i) : SV_Target
            {
                // 1. Texture Fetching
                half4 albedo = SAMPLE_TEXTURE2D(_AlbedoMap, sampler_AlbedoMap, i.uv) * _BaseColorTint;
                half4 mask = SAMPLE_TEXTURE2D(_CombinedMask, sampler_CombinedMask, i.uv);
                
                // 2. Normal Reconstruction (TBN Matrix)
                half3 sampledNormal = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMapInflow, sampler_NormalMapInflow, i.uv), _NormalIntensity);
                half3 bitangent = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                half3 worldNormal = normalize(mul(sampledNormal, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                // 3. Cel Shading Logic (Main Light)
                Light light = GetMainLight(i.shadowCoord);
                half lightDotNormal = dot(worldNormal, light.direction);
                
                // Stepped lighting for Anime look
                half lightStep = smoothstep(_ShadingThreshold - _ShadingSoftness, _ShadingThreshold + _ShadingSoftness, lightDotNormal);
                lightStep *= light.shadowAttenuation; 

                // 4. Color Blending
                half3 diffuseColor = albedo.rgb;
                half3 shadeColor = diffuseColor * _ShadowTintMultiplier.rgb;
                half3 finalRGB = lerp(shadeColor, diffuseColor, lightStep);

                // 5. Stylized Specular Highlights
                // Uses Mask Red channel for Metallic reflection areas
                half3 halfVector = normalize(light.direction + normalize(i.viewDirWS));
                float normalDotHalf = max(0, dot(worldNormal, halfVector));
                
                // Roughness (Mask Blue) controls the sharpness of the highlight
                float specSize = pow(normalDotHalf, 100.0 * (1.1 - mask.b)); 
                half specArea = smoothstep(0.5, 0.51, specSize) * mask.r * _SpecularGlowIntensity;
                
                finalRGB += (specArea * light.color);
                
                // 6. Ambient Occlusion (Mask Green)
                finalRGB *= lerp(0.5, 1.0, mask.g);

                return half4(finalRGB, albedo.a);
            }
            ENDHLSL
        }

        // Required passes for shadow casting and depth pre-pass
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}