Shader "Elanetic/Tilemap"
{
    Properties
    {
        _TextureAtlas ("Texture Atlas", 2D) = "white" {}
        _TextureIndices ("Texture Data", 2D) = "white" {}
        _CellSize("Cell Size", float) = 36
        _GridSize("Grid Size", float) = 1024 
        _AtlasWidthCount("Atlas Width Count", float) = 3
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

            sampler2D _TextureAtlas;
            sampler2D _TextureIndices;
            float _CellSize;
            float _GridSize;
            float _AtlasWidthCount;
            float _GutterSize = 1.0f;
            float _AtlasSize = 1.0f;
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _TextureIndices);
                return o;
            }

            

            fixed4 frag (v2f i) : SV_Target
            {
                float cellSize = (1.0f/_GridSize);
                float2 sourceUV =  cellSize * floor(i.uv / cellSize);
                int index = tex2D(_TextureIndices, sourceUV).x * 256;

                int xPos = index % _AtlasWidthCount;
                int yPos = index / _AtlasWidthCount;
                float2 uv = float2(xPos, yPos) / _AtlasWidthCount;
                
                // Offset to fragment position inside tile
                float xOffset = frac(i.uv.x * _GridSize) / (_AtlasWidthCount+0.001f);
                float yOffset = frac(i.uv.y * _GridSize) / (_AtlasWidthCount+0.001f);
                uv += float2(xOffset, yOffset);

                return tex2D(_TextureAtlas, uv);
            }
            ENDCG
        }
    }
}
