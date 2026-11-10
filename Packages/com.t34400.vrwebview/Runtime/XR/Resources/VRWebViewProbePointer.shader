Shader "Hidden/VRWebView/Probe Pointer"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Overlay+100" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct Input
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Output
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            fixed4 _Color;
            Output vert(Input value)
            {
                Output result;
                UNITY_SETUP_INSTANCE_ID(value);
                UNITY_INITIALIZE_OUTPUT(Output, result);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(result);
                result.position = UnityObjectToClipPos(value.vertex);
                result.color = value.color * _Color;
                return result;
            }
            fixed4 frag(Output value) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(value);
                return value.color;
            }
            ENDCG
        }
    }
}
