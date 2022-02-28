Shader "Elanetic/Tilemap"
{
    Properties
    {
        _TextureAtlas ("Texture", 2D) = "white" {}
        _TextureIndices ("Texture", 2D) = "white" {}
        _CellSize("Cell Size", float) = 36
        _GridSize("Grid Size", float) = 1024 
        _AtlasWidthCount("Atlas Width Count", float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _TextureIndices);
                return o;
            }

            

            fixed4 frag (v2f i) : SV_Target
            {
	            float index = floor(tex2D( _TextureIndices, i.uv ).r * 256.0);
                
                float coordX = floor(index % _AtlasWidthCount);
                float coordY = floor(index / _AtlasWidthCount);

                float uvSize = 1.0f / _GridSize;
                float2 div = i.uv / uvSize;
                float2 min = (div - frac(div)) * uvSize;
                float2 max = float2(min.x + uvSize, min.y + uvSize);
                float2 percent = (i.uv - min) / (max - min);

                //return fixed4(percent.x, percent.y, 0,1);

                float cellUVSize = _CellSize / (_CellSize * _AtlasWidthCount);
                float2 targetUV = float2(coordX * cellUVSize, coordY * cellUVSize) + (percent * cellUVSize);
                
                fixed4 col = tex2D(_TextureAtlas, targetUV);
                return col;
            }
            ENDCG
        }
    }
}
