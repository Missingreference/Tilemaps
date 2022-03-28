Shader "Elanetic/TilemapAtlas"
{
    Properties
    {
        _TextureAtlas ("Texture Atlas", 2D) = "white" {}
        _CellSize("Cell Size", float) = 36
        _AtlasWidthCount("Atlas Width Count", int) = 16 
        _AtlasHeightCount("Atlas Height Count", int) = 16
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

		Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float4 _TextureIndices_ST;
            float4 _TextureAtlas_ST;
            
            struct MeshProperties {
                float2 position;
                int tileIndex;
            };

            sampler2D _TextureAtlas;
            
            float _CellSize;
            float _AtlasWidthCount;
            float _AtlasHeightCount;
            StructuredBuffer<MeshProperties> _Properties;
            
            v2f vert (appdata v, uint instanceID: SV_InstanceID)
            {
                v2f o;
               o.vertex = UnityObjectToClipPos(v.vertex + float4(_Properties[instanceID].position.x,_Properties[instanceID].position.y,0,0));
                //o.vertex = UnityObjectToClipPos(v.vertex + float4(1,1,0,0));
                o.uv = TRANSFORM_TEX(v.uv, _TextureAtlas);
                int2 coord = int2(_Properties[instanceID].tileIndex % _AtlasWidthCount, _Properties[instanceID].tileIndex / _AtlasWidthCount);
                float2 tileUVSize = float2(_CellSize / (_CellSize * _AtlasWidthCount), _CellSize / (_CellSize * _AtlasHeightCount));
                //float2 tileUVSize = float2(0.5f, 1.0f);
                o.uv *= tileUVSize;
                o.uv += tileUVSize * coord;
                return o;
            }

            

            fixed4 frag (v2f i) : SV_Target
            {
                //return fixed4(1,0,0,1);
                //if(i.uv.x < 0.5f)
                //return fixed4(i.uv.x,i.uv.y,0,1);
                fixed4 col = tex2D(_TextureAtlas, i.uv);
                return col;
            }
            ENDCG
        }
    }
}
