using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Unity.Collections;

using Elanetic.Graphics;
using Elanetic.Tools;

namespace Elanetic.Tilemaps
{
    [DefaultExecutionOrder(975)]
    public class Tilemap : MonoBehaviour
    {
        public Vector2 worldPos;
        public Vector2Int cellPos;

        /// <summary>
        ///How many tiles are in a chunk. tileTextureSize times chunkSize is the result of the size of the texture for each chunk of the tile
        /// </summary>
        public int chunkSize { get; set; } = 8;
        //In pixels
        public int tileTextureSize { get; set; } = 36;

        //World size of cell
        public float cellSize
        {
            get => m_CellSize;
            set
            {
                m_CellSize = value;
                m_HalfCellSize = m_CellSize * 0.5f;
            }
        }

        public int tileCount { get; private set; } = 0;

        //private ChunkedGridArray<TilemapChunk> m_Chunks = new ChunkedGridArray<TilemapChunk>(16, 8, 16);
        //private ChunkedGridArray<int> m_TileTextures = new ChunkedGridArray<int>(16, 8, 16);
        //private ChunkedGridArray<Tile> m_Tiles = new ChunkedGridArray<Tile>(16, 8, 16);
        private GridArray<TilemapChunk> m_Chunks = new GridArray<TilemapChunk>(8, 16);
        private GridArray<int> m_TileTextures = new GridArray<int>(8, 16);
        private GridArray<Tile> m_Tiles = new GridArray<Tile>(8, 16);

        private float m_CellSize = 2.0f;
        private float m_HalfCellSize = 0.0f;

        private int m_TotalTilesPerChunk;

        private TextureAtlas m_TextureAtlas;
        private Dictionary<Hash128, int> m_TextureLookup = new Dictionary<Hash128, int>(256);
        private DirectTexture2D m_BlankTexture;
        private Hash128 m_BlankHash;
        private TextureFormat m_TextureFormat;
        private Material m_TilemapMaterial;

        void Awake()
        {
            //It is recommended to use BC7 as the texture format for desktop since it has the best compression size and quality for modern systems.
            m_TextureFormat = TextureFormat.BC7;

            m_BlankTexture = DirectGraphics.CreateTexture(tileTextureSize, tileTextureSize, m_TextureFormat);
            //m_BlankTexture = new Texture2D(tileTextureSize, tileTextureSize, m_TextureFormat, false);
            //m_BlankTexture.filterMode = FilterMode.Point;
            //NativeArray<int> texData = m_BlankTexture.GetRawTextureData<int>();
            //for(int i = 0; i < texData.Length; i++)
            //{
            //    texData[i] = 0;
            //}
            //m_BlankTexture.SetPixels(Color.clear);
            //m_BlankTexture.Apply(false, true);

            m_BlankHash = m_BlankTexture.texture.imageContentsHash;

            m_TotalTilesPerChunk = chunkSize * chunkSize;
            int chunkWidth = chunkSize * tileTextureSize;
            for(int i = 0; i < m_TotalTilesPerChunk; i++)
            {
                Vector2Int destination = Utils.IndexToCoord(i, chunkSize);
                //Graphics.CopyTexture(m_BlankTexture, 0, 0, 0, 0, tileTextureSize, tileTextureSize, m_BlankChunkTexture, 0, 0, destination.x * tileTextureSize, destination.y * tileTextureSize);
            }

            m_TilemapMaterial = new Material(Shader.Find("Sprites/Default"));
            m_TilemapMaterial.enableInstancing = true;

            SpriteRenderer s = new GameObject("Blank Texture").AddComponent<SpriteRenderer>();
            s.sprite = Sprite.Create(m_BlankTexture.texture, new Rect(0, 0, tileTextureSize, tileTextureSize), new Vector2(0.5f, 0.5f), tileTextureSize, 0, SpriteMeshType.FullRect, Vector4.zero, false);

            m_TextureAtlas = new TextureAtlas(new Vector2Int(tileTextureSize, tileTextureSize), new Vector2Int(16, 16), m_TextureFormat);
        }

        public int cellX;
        public int cellY;
        public Vector2Int chunkPosit;
        void Update()
        {

            int negativityBoost = (((cellX & int.MinValue) >> 31) & 1);
            int x = ((cellX + negativityBoost) / chunkSize) - negativityBoost;
            negativityBoost = (((cellY & int.MinValue) >> 31) & 1);
            int y = ((cellY + negativityBoost) / chunkSize) - negativityBoost;
            chunkPosit = new Vector2Int(x, y);
            /*
            for(int x = 0; x < 16; x++)
            {
                float xPos = cellSize * (x - 8);
                Debug.DrawLine(transform.TransformPoint(new Vector3(xPos, -100)), transform.TransformPoint(new Vector3(xPos, 100)), Color.blue);
            }
            for(int y = 0; y < 16; y++)
            {
                float yPos = cellSize * (y - 8);
                Debug.DrawLine(transform.TransformPoint(new Vector3(-100, yPos)), transform.TransformPoint(new Vector3(100, yPos)), Color.blue);
            }

            Utils.DrawRect(new Rect(transform.TransformPoint(new Vector2(cellPos.x * cellSize, cellPos.y * cellSize)), new Vector2(cellSize, cellSize)));
            Utils.DrawPoint(worldPos, Color.green);
            Utils.DrawRect(new Rect(CellToWorld(WorldToCell(worldPos)), new Vector2(cellSize,cellSize)), Color.green);
            */
        }

        public void SetTile<T>(int x, int y) where T : Tile
        {
            SetTile(x, y, typeof(T));
        }

        public void SetTile<T>(Vector2Int cellPosition) where T : Tile
        {
            SetTile(cellPosition.x, cellPosition.y, typeof(T));
        }

        public void SetTile(int x, int y, Type tileType)
        {
            if(tileType == null)
            {
                Tile existingTile = m_Tiles.GetItem(x, y);
                if(existingTile == null)
                {
                    return;
                }

                m_Tiles.SetItem(x, y, null);
                existingTile.Destroy();

                SetTileTexture(x, y, -1);

                tileCount--;
            }
            else
            {
#if SAFE_EXECUTION
                if(!tileType.IsSubclassOf(typeof(Tile)))
                    throw new ArgumentException("Inputted tile type must derive from Tile class.", nameof(tileType));
                if(tileType.IsAbstract)
                    throw new ArgumentException("Inputted tile type must not be abstract.", nameof(tileType));
#endif
                //TilemapChunk chunk = GetChunk(x, y);
                Tile existingTile = m_Tiles.GetItem(x, y);

                //int chunkIndex = m_TileTextures.GetChunkIndex(x, y);

                //int[] textureChunk = m_TileTextures.GetChunk(chunkIndex);
                //Tile[] tilesChunk = m_Tiles.GetChunk(chunkIndex);

                //int index = m_TileTextures.GetCellIndexWithinChunk(x, y);

                //Destroy old tile if one exists
                if(existingTile != null)
                {
                    tileCount--;
                    //tilesChunk[index] = null;
                    existingTile.Destroy();
                }
                /*else if(tilesChunk == null)
                {
                    //Chunk is null on ChunkedGridArray. Calling this ensures the grid grows to fit it for performance increase on repeat calls to direct array.
                    m_TileTextures.SetItem(x, y, -1);
                    m_Tiles.SetItem(x, y, null);
                    textureChunk = m_TileTextures.GetChunk(chunkIndex);
                    tilesChunk = m_Tiles.GetChunk(chunkIndex);
                }
                else
                {
                    textureChunk[index] = -1;
                }
                */

                tileCount++;

                //Set new tile
                Tile tile = (Tile)Activator.CreateInstance(tileType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { this, x, y }, null);

                //if(textureChunk[index] == -1)
                {
                    //Set to blank if tile constructor doesn't set tile texture.
                    //GetChunk(x, y).AddDirtyTile(x, y, -1);
                }

                //tilesChunk[index] = tile;
            }
        }

        public void SetTile(Vector2Int cellPosition, Type tileType)
        {
            SetTile(cellPosition.x, cellPosition.y, tileType);
        }

        public Tile GetTile(Vector2Int cellPosition)
        {
            return m_Tiles.GetItem(cellPosition.x, cellPosition.y);
        }

        internal TilemapChunk GetChunk(int cellPositionX, int cellPositionY)
        {
            int negativityBoost = (((cellPositionX & int.MinValue) >> 31) & 1); ;
            int chunkPositionX = ((cellPositionX + negativityBoost) / chunkSize) - negativityBoost;
            negativityBoost = (((cellPositionY & int.MinValue) >> 31) & 1); ;
            int chunkPositionY = ((cellPositionY + negativityBoost) / chunkSize) - negativityBoost;

            if(cellPositionY == -8)
            {
                //Debug.Log("Y-1 Chunk: " + chunkPositionX.ToString() + ", " + chunkPositionY.ToString());
            }
            Debug.Log("GetChunk: " + cellPositionX.ToString() + ", " + cellPositionY.ToString() + " Chunk: " + chunkPositionX.ToString() + ", " + chunkPositionY.ToString());
            TilemapChunk chunk = m_Chunks.GetItem(chunkPositionX, chunkPositionY);
            if(chunk.IsNull())
            {
                if(chunkPositionX == 1 && chunkPositionY == -1)
                {
                    Debug.Log("CREATION");
                }
#if UNITY_EDITOR
                GameObject chunkObject = new GameObject("Tilemap Chunk (" + chunkPositionX + ", " + chunkPositionY + ")");
#else
                GameObject chunkObject = new GameObject("Tilemap Chunk");
#endif
                chunkObject.transform.SetParent(transform);
                chunk = chunkObject.AddComponent<TilemapChunk>();
                chunk.tileBounds = new BoundsInt2D(chunkPositionX, chunkPositionY, chunkSize, chunkSize);
                chunk.tilemap = this;
                chunk.chunkPosition = new Vector2Int(chunkPositionX, chunkPositionY);
                chunkObject.transform.localPosition = new Vector3(chunkPositionX * chunkSize * cellSize, chunkPositionY * chunkSize * cellSize, 0.0f);
                //chunkObject.transform.localScale = new Vector3(cellSize, cellSize, 1.0f);

                //Debug.Log("Setting: " + chunkPositionX + ", " + chunkPositionY);
                m_Chunks.SetItem(chunkPositionX, chunkPositionY, chunk);
            }
            else
            {
                if(chunkPositionX == 1 && chunkPositionY == -1)
                {
                    Debug.Log("CREATION CANCELLED: " + chunk.ToString());
                }
                //Debug.Log("Chunk already created: Cell: " + cellPositionX.ToString() + ", " + cellPositionY.ToString() + " | Chunk: " + chunkPositionX.ToString() + ", " + chunkPositionY.ToString());
            }
            return chunk;
        }

        public int AddTileTexture(Texture2D tileTexture)
        {
#if SAFE_EXECUTION
            if(tileTexture.width != tileTextureSize || tileTexture.height != tileTextureSize)
                throw new ArgumentException("Inputted tile texture does not match tilemap tile texture size.", nameof(tileTexture));
            if(tileTexture.format != m_TextureFormat)
                throw new ArgumentException("Specified texture format must match tilemap format of '" + m_TextureFormat + "'.", nameof(tileTexture));
#endif
            Hash128 hash = tileTexture.imageContentsHash;
            if(hash == m_BlankHash) return -1;

            int atlasIndex;
            if(!m_TextureLookup.TryGetValue(hash, out atlasIndex))
            {
                atlasIndex = m_TextureAtlas.AddTexture(tileTexture);
                m_TextureLookup.Add(hash, atlasIndex);
            }

            return atlasIndex;
        }

        /// <summary>
        /// An alternative way of setting a cell's texture without creating a Tile instance.
        /// </summary>
        public void SetTileTexture(int x, int y, int textureIndex)
        {
            if(y == -8)
            {
                Debug.Log("YAYAYA X: " + x.ToString());
            }
#if SAFE_EXECUTION
            if(textureIndex < -1)
                throw new IndexOutOfRangeException("Texture Index must be -1 or more. Setting it to -1 will set it to a blank texture.");
#endif
            //if(m_TileTextures.size < FastMath.Abs(x) || m_TileTextures.size > FastMath.Abs(y))
            {
                GetChunk(x, y).AddDirtyTile(x, y, textureIndex);
                m_TileTextures.SetItem(x,y, textureIndex);
            }/*
            else
            {
                int[] textureChunk = m_TileTextures.GetChunk(x, y);
                int index = m_TileTextures.GetCellIndexWithinChunk(x, y);
                if(textureChunk[index] != textureIndex)
                {
                    GetChunk(x, y).AddDirtyTile(x, y, textureIndex);
                    textureChunk[index] = textureIndex;
                }
            }
            */
        }

        /// <summary>
        /// An alternative way of setting a cell's texture without creating a Tile instance.
        /// </summary>
        public void SetTileTexture(Vector2Int cell, int textureIndex)
        {
            SetTileTexture(cell.x, cell.y, textureIndex);
        }

        #region Tranform

        public Vector2Int LocalToCell(Vector2 localPosition)
        {
            return new Vector2Int(Mathf.FloorToInt(localPosition.x / cellSize), Mathf.FloorToInt(localPosition.y / cellSize));
        }

        public Vector2Int WorldToCell(Vector2 worldPosition)
        {
            return LocalToCell(transform.InverseTransformPoint(worldPosition));
        }

        public Vector2 CellToLocal(Vector2Int cellPosition)
        {
            return new Vector2(cellPosition.x * cellSize, cellPosition.y * cellSize);
        }    

        public Vector2 CellToWorld(Vector2Int cellPosition)
        {
            return transform.TransformPoint(CellToLocal(cellPosition));
        }

        public Vector2 CellCenterToLocal(Vector2Int cellPosition)
        {
            return CellToLocal(cellPosition) + new Vector2(m_HalfCellSize, m_HalfCellSize);
        }

        public Vector2 CellCenterToWorld(Vector2Int cellPosition)
        {
             return transform.TransformPoint(CellCenterToLocal(cellPosition));
        }

        #endregion

        Mesh theGrandMesh;
        internal class TilemapChunk : MonoBehaviour
        {
            public BoundsInt2D tileBounds { get; internal set; }
            public Tilemap tilemap { get; internal set; }
            public Vector2Int chunkPosition { get; internal set; }

            public Mesh mesh;
            private SpriteRenderer m_SpriteRenderer;
            
            private DirectTexture2D m_DirectTexture;
            private Sprite m_Sprite;

            private int m_DirtyCount = 0;
            private Vector3Int[] m_DirtyTiles;

            void Awake()
            {
                tilemap = transform.parent.GetComponent<Tilemap>();

                mesh = new Mesh();
                float size = tilemap.m_CellSize * tilemap.chunkSize;
                //float width = 36.0f;
                //float height = 1.0f;
                Vector3[] vertices = new Vector3[4]
                {
            new Vector3(0, 0, 0),
            new Vector3(size, 0, 0),
            new Vector3(0, size, 0),
            new Vector3(size, size, 0)
                };
                mesh.vertices = vertices;

                int[] tris = new int[6]
                {
            // lower left triangle
            0, 2, 1,
            // upper right triangle
            2, 3, 1
                };
                mesh.triangles = tris;

                Vector3[] normals = new Vector3[4]
                {
            -Vector3.forward,
            -Vector3.forward,
            -Vector3.forward,
            -Vector3.forward
                };
                mesh.normals = normals;

                Vector2[] uv = new Vector2[4]
                {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
                };
                mesh.uv = uv;
                MeshFilter mFilter = gameObject.AddComponent<MeshFilter>();
                MeshRenderer mRenderer = gameObject.AddComponent<MeshRenderer>();
                mFilter.mesh = mesh;

                //mRenderer.sharedMaterial = tilemap.m_TilemapMaterial;
                mRenderer.material = tilemap.m_TilemapMaterial;
                mRenderer.shadowCastingMode = ShadowCastingMode.Off;
                mRenderer.receiveShadows = false;
                mRenderer.lightProbeUsage = LightProbeUsage.Off;
                mRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                mRenderer.allowOcclusionWhenDynamic = true;
                //m_SpriteRenderer = GetComponent<SpriteRenderer>();

                m_DirectTexture = DirectGraphics.CreateTexture(tilemap.tileTextureSize * tilemap.chunkSize, tilemap.tileTextureSize * tilemap.chunkSize, tilemap.m_TextureFormat);

                mRenderer.material.mainTexture = m_DirectTexture.texture;

                DirectGraphics.ClearTexture(m_DirectTexture.nativePointer);
                //m_Sprite = Sprite.Create(m_DirectTexture.texture, new Rect(0, 0, m_DirectTexture.texture.width, m_DirectTexture.texture.height), Vector2.zero, 36.0f, 0, SpriteMeshType.FullRect, Vector4.zero, false);
                //m_SpriteRenderer.sprite = m_Sprite;

                //We don't need to Apply the texture because we are assuming that this chunk was created because we are setting a new tile anyways
                m_DirtyTiles = new Vector3Int[FastMath.Min(8, tilemap.m_TotalTilesPerChunk)];
            }

            /*public void AddDirtyTile(Tile tile)
            {

                if(tile.tilemapTextureIndex < 0)
                {
                    Vector2Int destination = GetTileTexturePosition(tile.position.x, tile.position.y);
                    Graphics.CopyTexture(tilemap.m_BlankTexture, 0, 0, 0, 0, tilemap.tileTextureSize, tilemap.tileTextureSize, m_Texture, 0, 0, destination.x, destination.y);
                }
                else
                {
#if SAFE_EXECUTION
                    if(tile.tilemapTextureIndex >= tilemap.m_TextureAtlas.textureCount)
                        throw new IndexOutOfRangeException("Tile '" + tile.GetType().Name + "' with invalid tilemap texture index of '" + tile.tilemapTextureIndex + "'. Tilemap has '" + tilemap.m_TextureAtlas.textureCount + "' textures added.");
#endif
                    Vector2Int sourcePosition = tilemap.m_TextureAtlas.AtlasIndexToPixelCoord(tile.tilemapTextureIndex);
                    Vector2Int destination = GetTileTexturePosition(tile.position.x, tile.position.y);
                    //Debug.Log(sourcePosition + " | " + destination);
                    Graphics.CopyTexture(tilemap.m_TextureAtlas.fullTexture, 0, 0, sourcePosition.x, sourcePosition.y, tilemap.tileTextureSize, tilemap.tileTextureSize, m_Texture, 0, 0, destination.x, destination.y);
                }
            }*/

            //Assumes that the tile has been set to null and will be set to blank pixels
            public void AddDirtyTile(int x, int y, int textureAtlasIndex)
            {
                if(textureAtlasIndex < 0)
                {
                    Vector2Int destination = GetTileTexturePosition(x, y);
                    //Graphics.CopyTexture(tilemap.m_BlankTexture, 0, 0, 0, 0, tilemap.tileTextureSize, tilemap.tileTextureSize, m_Texture, 0, 0, destination.x, destination.y);
                    DirectGraphics.CopyTexture(tilemap.m_BlankTexture.nativePointer, 0, 0, tilemap.tileTextureSize, tilemap.tileTextureSize, m_DirectTexture.nativePointer, destination.x, destination.y);
                }
                else
                {
#if SAFE_EXECUTION
                    if(textureAtlasIndex >= tilemap.m_TextureAtlas.textureCount)
                        throw new IndexOutOfRangeException("Tile at (" + x.ToString() + ", " + y.ToString() + ") with invalid tilemap texture index of '" + textureAtlasIndex.ToString() + "'. Tilemap only has '" + tilemap.m_TextureAtlas.textureCount + "' textures added.");
#endif
                    Vector2Int sourcePosition = tilemap.m_TextureAtlas.AtlasIndexToPixelCoord(textureAtlasIndex);
                    Vector2Int destination = GetTileTexturePosition(x, y);

                    //Graphics.CopyTexture(tilemap.m_TextureAtlas.fullTexture, 0, 0, sourcePosition.x, sourcePosition.y, tilemap.tileTextureSize, tilemap.tileTextureSize, m_Texture, 0, 0, destination.x, destination.y);
                    DirectGraphics.CopyTexture(tilemap.m_TextureAtlas.nativeTexturePointer, sourcePosition.x, sourcePosition.y, tilemap.tileTextureSize, tilemap.tileTextureSize, m_DirectTexture.nativePointer, destination.x, destination.y);
                }
            }

            private Vector2Int GetTileTexturePosition(int x, int y)
            {
                Vector2Int v = new Vector2Int(Mathf.FloorToInt(x / (float)tilemap.chunkSize) * tilemap.chunkSize, Mathf.FloorToInt(y / (float)tilemap.chunkSize) * tilemap.chunkSize);
                
                return new Vector2Int((-(v.x - x)) * tilemap.tileTextureSize, (-(v.y - y)) * tilemap.tileTextureSize);
            }

            private void OnWillRenderObject()
            {
                if(m_DirtyCount > 0)
                {

                }
            }
        }
    }
}