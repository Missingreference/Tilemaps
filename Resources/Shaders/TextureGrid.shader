Shader "Elanetic/TextureGrid"
{
    Properties
    {
        _TextureAtlas ("Texture Atlas", 2D) = "white" {}
        _ChunkWorldSize("Chunk World Size", float) = 8
        _AtlasWidthCount("Atlas Width Count", float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

		Blend SrcAlpha OneMinusSrcAlpha
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
                uint instanceIndex : SV_InstanceID;
            };

            float4 _TextureIndices_ST;
            
            struct ChunkProperties
            {
                float2 position;
                //64 tiles per chunk. Each uint holds 4 indexs(4 bytes).
                //Indexs range from 0 to 255 since each byte in a uint represents a tile's texture index
                uint cellData[64 / 4];
            };

            //TODO An idea for culling to be implemented
            struct RenderIndex
            {
                int index;
            };            

            StructuredBuffer<ChunkProperties> _ChunkData;
            StructuredBuffer<RenderIndex> _AllRenderIndexs;

            sampler2D _TextureAtlas;
            sampler2D _TextureIndices;
            float _ChunkWorldSize;
            float _ChunkCellWidthCount;
            float _AtlasWidthCount;

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float2 chunkPosition = _ChunkData[instanceID].position * _ChunkWorldSize;
                o.vertex = UnityObjectToClipPos((v.vertex * _ChunkWorldSize) + float4(chunkPosition.x,chunkPosition.y,0,0));
                o.uv = v.uv;
                o.instanceIndex = instanceID;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                //8 in a lot of these cases represents the chunk size in tiles since were working with 8x8 chunks.
                const float cellSize = 1.0f / 8;
                float2 sourceUV = cellSize * floor(i.uv / cellSize);

                uint xInt = sourceUV.x * 8;
                uint yInt = sourceUV.y * 8;

                uint index = (yInt * 8) + xInt;
                
                ChunkProperties data = _ChunkData[i.instanceIndex];
                index = data.cellData[index/4] >> ((index % 4) * 8);
                index &= 0xFF;
                
                int xPos = index % _AtlasWidthCount;
                int yPos = index / _AtlasWidthCount;

                float2 uv = float2(xPos, yPos) / _AtlasWidthCount;
                
                //Offset to fragment position inside tile
                float xOffset = frac(i.uv.x * 8) / (_AtlasWidthCount+0.001f);
                float yOffset = frac(i.uv.y * 8) / (_AtlasWidthCount+0.001f);
                
                uv += float2(xOffset, yOffset);
                
                return tex2D(_TextureAtlas, uv);
            }
            ENDCG
        }
    }
}
