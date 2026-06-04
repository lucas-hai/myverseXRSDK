// Made with Amplify Shader Editor v1.9.1.5
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "MyVerse/FX/ASE/Additive_Mask1"
{
	Properties
	{
		[Enum(UnityEngine.Rendering.CullMode)]_CullMode("剔除模式(显示模式)", Float) = 0
		[Enum(UnityEngine.Rendering.CompareFunction)]_ZT("是否显示在前", Float) = 0
		[HDR]_Color("Color", Color) = (1,1,1,1)
		_Color_Strength("颜色强度",Range(0,10)) = 1
		_Main_Texture("Main_Texture", 2D) = "white" {}
		
		_UVSpeed("UVSpeed", Vector) = (0,0,0,0)
		_Float0("软粒子数值", Range( 0 , 100)) = 0
		_Mask_Texture("Mask_Texture", 2D) = "white" {}
		_Mask_speed("Mask_speed", Vector) = (0,0,0,0)
		_Mask2_Texture("Mask2_Texture", 2D) = "white" {}
		_Mask2_speed("Mask2_speed", Vector) = (0,0,0,0)
		_Mask3_Texture("Mask3_Texture", 2D) = "white" {}
		_Mask3_speed("Mask3_speed", Vector) = (0,0,0,0)
	}

	SubShader
	{
		LOD 0

		

		Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "UniversalMaterialType"="Unlit" }

		Cull [_CullMode]
		AlphaToMask Off

		

		HLSLINCLUDE
		#pragma only_renderers gles gles3 glcore metal d3d11 vulkan
		#pragma target 2.5
		ENDHLSL

		
		Pass
		{
			
			Name "Forward"
			Tags { "LightMode"="UniversalForward" }

			Blend One OneMinusSrcAlpha
			ZWrite Off
			ZTest [_ZT]
			Offset 0 , 0
			ColorMask RGBA

			

			HLSLPROGRAM


			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma instancing_options renderinglayer
			
			#define REQUIRE_DEPTH_TEXTURE 1

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureStack.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Editor/ShaderGraph/Includes/ShaderPass.hlsl"

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"



			struct VertexInput
			{
				float4 vertex : POSITION;
				float3 ase_normal : NORMAL;
				float4 ase_color : COLOR;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct VertexOutput
			{
				float4 clipPos : SV_POSITION;
				float fogFactor : TEXCOORD2;
				float4 ase_color : COLOR;
				float4 ase_texcoord3 : TEXCOORD3;
				float4 ase_texcoord4 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				float4 _Color;
				float4 _Main_Texture_ST;
				half _Color_Strength;
				float4 _Mask_Texture_ST;
				float4 _Mask2_Texture_ST;
				float4 _Mask3_Texture_ST;
				float2 _UVSpeed;
				float2 _Mask_speed;
				float2 _Mask2_speed;
				float2 _Mask3_speed;
				float _CullMode;
				float _ZT;
				float _Float0;
				
			CBUFFER_END

			SAMPLER(sampler_Main_Texture);
			TEXTURE2D(_Main_Texture);

			SAMPLER(sampler_Mask_Texture);
			TEXTURE2D(_Mask_Texture);

			SAMPLER(sampler_Mask2_Texture);
			TEXTURE2D(_Mask2_Texture);

			SAMPLER(sampler_Mask3_Texture);
			TEXTURE2D(_Mask3_Texture);
			
			VertexOutput vert ( VertexInput v )
			{
				VertexOutput o = (VertexOutput)0;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_TRANSFER_INSTANCE_ID(v, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				float4 ase_clipPos = TransformObjectToHClip((v.vertex).xyz);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord4 = screenPos;
				
				o.ase_color = v.ase_color;
				o.ase_texcoord3.xy = v.ase_texcoord.xy;
				
				//setting value to unused interpolator channels and avoid initialization warnings
				o.ase_texcoord3.zw = 0;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					float3 defaultVertexValue = v.vertex.xyz;
				#else
					float3 defaultVertexValue = float3(0, 0, 0);
				#endif

				float3 vertexValue = defaultVertexValue;

				#ifdef ASE_ABSOLUTE_VERTEX_POS
					v.vertex.xyz = vertexValue;
				#else
					v.vertex.xyz += vertexValue;
				#endif

				v.ase_normal = v.ase_normal;

				float3 positionWS = TransformObjectToWorld( v.vertex.xyz );
				float4 positionCS = TransformWorldToHClip( positionWS );

				
				o.fogFactor = ComputeFogFactor( positionCS.z );
				o.clipPos = positionCS;

				return o;
			}
			

			half4 frag ( VertexOutput IN  ) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );

				
				float4 ShadowCoords = float4( 0, 0, 0, 0 );


				float2 uv_Main_Texture = IN.ase_texcoord3.xy * _Main_Texture_ST.xy + _Main_Texture_ST.zw;
				float2 panner13 = ( 1.0 * _Time.y * _UVSpeed + uv_Main_Texture);
				float4 tex2DNode2 = SAMPLE_TEXTURE2D( _Main_Texture,sampler_Main_Texture, panner13 );
				float4 screenPos = IN.ase_texcoord4;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float screenDepth10 = LinearEyeDepth(SHADERGRAPH_SAMPLE_SCENE_DEPTH( ase_screenPosNorm.xy ),_ZBufferParams);
				float distanceDepth10 = abs( ( screenDepth10 - LinearEyeDepth( ase_screenPosNorm.z,_ZBufferParams ) ) / ( _Float0 ) );

				float2 uv_Mask_Texture = IN.ase_texcoord3.xy * _Mask_Texture_ST.xy + _Mask_Texture_ST.zw;
				float2 panner19 = ( 1.0 * _Time.y * _Mask_speed + uv_Mask_Texture);

				float2 uv_Mask2_Texture = IN.ase_texcoord3.xy * _Mask2_Texture_ST.xy + _Mask2_Texture_ST.zw;
				float2 panner27 = ( 1.0 * _Time.y * _Mask2_speed + uv_Mask2_Texture);

				float2 uv_Mask3_Texture = IN.ase_texcoord3.xy * _Mask3_Texture_ST.xy + _Mask3_Texture_ST.zw;
				float2 panner37 = ( 1.0 * _Time.y * _Mask3_speed + uv_Mask3_Texture);


				float3 desaturateInitialColor23 = ( SAMPLE_TEXTURE2D( _Mask_Texture,sampler_Mask_Texture, panner19 ) * SAMPLE_TEXTURE2D( _Mask2_Texture,sampler_Mask2_Texture, panner27 ) * SAMPLE_TEXTURE2D( _Mask3_Texture,sampler_Mask3_Texture, panner37 )).rgb;
				float desaturateDot23 = dot( desaturateInitialColor23, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar23 = lerp( desaturateInitialColor23, desaturateDot23.xxx, 0.0 );
				
				float3 BakedAlbedo = 0;
				float3 BakedEmission = 0;
				float4 Color = saturate( IN.ase_color * (_Color * _Color_Strength)* tex2DNode2 * saturate( distanceDepth10 ) * IN.ase_color.a * tex2DNode2.a * _Color.a * (desaturateVar23).x );
				float AlphaClipThreshold = 0.5;
				float AlphaClipThresholdShadow = 0.5;

				
				//Color *= Alpha;
				
				Color.rgb = MixFog( Color.rgb, IN.fogFactor );
				return Color;
			}
			ENDHLSL
		}

		
		
		
	}
	

	Fallback off
}
