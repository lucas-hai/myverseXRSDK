// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "MyVerse/Role/MR/Transparent"
{
	Properties
	{
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
		[HDR]_Color( "颜色",Color) = (1,1,1,1)
		_Cutoff("裁剪",Range(0,1)) = 0.5
		_NoiseTex("噪点贴图",2D) = "white" {}
		_NoiseTexStrength("噪点贴图强度",Range(0.1,10)) = 2
		_FresnelPeripheryStrength("菲尼尔边缘强度",Range(0,10)) = 1
		_FresnelPeripheryRange("菲尼尔边缘范围",Range(0,10)) = 2
		[Space(20)]
		_FresnelStrength("噪点菲尼尔强度",Range(0,10)) = 1
		_FresnelRange("噪点菲尼尔范围",Range(0,10)) = 1.5
		_NoiseScale("噪点大小",Range(0,5000)) = 10
	}

	SubShader
	{
		
		Tags{"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Transparent" 
			"Queue"="Transparent"
			"lgnoreProjector" = "True"
		}
		LOD 0

		HLSLINCLUDE
		#pragma only_renderers gles gles3 glcore metal d3d11 vulkan
		#pragma target 2.5
		ENDHLSL 
		
		Pass
		{
			
			Name "SRPDefaultUnlit"
			Tags { "LightMode"="SRPDefaultUnlit" }

			Cull Back
			Blend One Zero
			ZWrite On
			ZTest [_ZTest]
			
			ColorMask 0
			HLSLPROGRAM
			
			
			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			//	#pragma multi_compile _ DOTS_INSTANCING_ON
			


			#pragma vertex vert
			#pragma fragment frag

			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			//	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			struct VertexInput
			{
				float4 positionOS   : POSITION;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			
			
			VertexOutput vert ( VertexInput v )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				
				
				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS.xyz);
				float3 positionWS = vertexInput.positionWS;
				
				o.positionWS = positionWS;
				o.positionCS = TransformWorldToHClip(positionWS);
				
				return o;
			} 
			
			half4 frag ( VertexOutput IN) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
				
				return 0;
				
			}
			ENDHLSL
		}


		Pass
		{
			
			Name "UniversalForward"
			Tags { "LightMode"="UniversalForward" }

			Cull Back
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			ZTest [_ZTest]
			
			ColorMask RGBA
			HLSLPROGRAM
			
			//--------------------------------------
			// GPU Instancing
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			//	#pragma multi_compile _ DOTS_INSTANCING_ON
			


			#pragma vertex vert
			#pragma fragment frag

			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			//#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			//#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			struct VertexInput
			{
				float4 positionOS   : POSITION;
				float3 normalOS     : NORMAL;
				float4 tangentOS    : TANGENT;
				float2 uv     : TEXCOORD0;
				
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
				float3 tangentWS : TEXCOORD2; 
				float3 bitangentWS : TEXCOORD3; 
				float4 uv : TEXCOORD4; 

				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				half4 _Color;
				half _Cutoff;
				float4 _NoiseTex_ST;
				half _NoiseTexStrength;
				half _FresnelPeripheryStrength;
				half _FresnelPeripheryRange;
				half _FresnelStrength;
				half _FresnelRange;
				half _NoiseScale;
			CBUFFER_END
			TEXTURE2D(_NoiseTex);
			SAMPLER(sampler_NoiseTex);


			VertexOutput vert ( VertexInput v )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				
				
				
				VertexNormalInputs normalInput = GetVertexNormalInputs(v.normalOS, v.tangentOS);
				o.normalWS = normalInput.normalWS;
				o.tangentWS = normalInput.tangentWS;
				o.bitangentWS = normalInput.bitangentWS;

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.positionOS.xyz);
				float3 positionWS = vertexInput.positionWS;
				
				o.positionWS = positionWS;
				o.positionCS = TransformWorldToHClip(positionWS);
				o.uv.xy = TRANSFORM_TEX(v.uv,_NoiseTex);
				o.uv.zw = v.uv;

				return o;
			} 
			
			/**
			
			float2 voronoihash( float2 p )
			{
				
				p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
				return frac( sin( p ) *43758.5453);
			}
			
			float GetNoise( float2 Scale)
			{
				float2 n = floor( Scale );
				float2 f = frac( Scale );
				half F1 = 8.0;
				float F2 = 8.0; float2 mg = 0;
				float2 id = 0;
				float2 mr = 0;
				float2 smoothId = 0;
				for ( int j = -1; j <= 1; j++ )
				{
					for ( int i = -1; i <= 1; i++ )
					{
						float2 g = float2( i, j );
						float2 o = voronoihash( n + g );
						o = ( sin( o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
						float d = 0.5 * dot( r, r );
						if( d<F1 ) {
							F2 = F1;
							F1 = d; mg = g; mr = r; id = o;
							} else if( d<F2 ) {
							F2 = d;
							
						}
					}
				}
				return F1;
			}
			
			**/
			half4 frag ( VertexOutput IN) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(IN);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

				float noiseTex = saturate(SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,IN.uv.xy).r);
				half noise = noiseTex * _NoiseTexStrength;

				half3 N = IN.normalWS;
				half3 L = normalize(_WorldSpaceCameraPos - IN.positionWS);
				half NdotL = saturate(1 - dot(N,L));
				half4 fresnel = pow(NdotL,_FresnelRange) * _FresnelStrength;
				half4 fresnelPeriphery = pow(NdotL,_FresnelPeripheryRange) * _FresnelPeripheryStrength;
				//float noise = saturate(1 - GetNoise(IN.uv.zw * _NoiseScale));
				//noise = smoothstep(0.9,1,noise);
				float4 color = (fresnel * noise + fresnelPeriphery) * _Color;
				return color;
				
			}
			ENDHLSL
		}




		
	}
	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}