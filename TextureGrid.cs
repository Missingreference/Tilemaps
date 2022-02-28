using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using Elanetic.Graphics;
using Elanetic.Tools;

namespace Elanetic.Tilemaps
{
    /// <summary>
    /// A square grid based tilemap. Add textures to the texture atlas and set textures for each 
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class TextureGrid : MonoBehaviour
    {
        /// <summary>
        /// How many cells are in a chunk. cellTextureSize times chunkSize is the result of the size of the texture for each chunk.
        /// </summary>
        public int chunkSize
        {
            get => m_ChunkSize;
            set
            {
#if SAFE_EXECUTION
                if(!m_LockSizes)
                    throw new InvalidOperationException("Cannot change the chunk size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_ChunkSize = value;
                m_ChunkTextureSize = cellTextureSize * m_ChunkSize;
                m_WorldCellSize = cellSize * chunkSize;
            }
        }

        /// <summary>
        /// The texture size for each cell.
        /// </summary>
        public int cellTextureSize
        {
            get => m_CellTextureSize;
            set
            {
#if SAFE_EXECUTION
                if(!m_LockSizes)
                    throw new InvalidOperationException("Cannot change the cell texture size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_CellTextureSize = value;
                m_ChunkTextureSize = cellTextureSize * m_ChunkSize;
            }
        }

        /// <summary>
        /// The world size of each cell.
        /// </summary>
        public float cellSize
        {
            get => m_CellSize;
            set
            {
#if SAFE_EXECUTION
                if(!m_LockSizes)
                    throw new InvalidOperationException("Cannot change the cell size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_CellSize = value;
                m_HalfCellSize = m_CellSize * 0.5f;
                m_WorldCellSize = cellSize * chunkSize;
            }
        }

        /// <summary>
        /// The texture format used for cells. Compressed textures means smaller memory sizes and faster texture copies but reduced features.
        /// </summary>
        public TextureFormat textureFormat
        {
            get => m_TextureFormat;
            set
            {
#if SAFE_EXECUTION
                if(!m_LockSizes)
                    throw new InvalidOperationException("Cannot change the texture format for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_TextureFormat = value;
            }
        }

        private int m_ChunkSize = 8;
        private int m_CellTextureSize = 36;
        private float m_CellSize = 2.0f;
        private float m_HalfCellSize = 2.0f * 0.5f;
        private int m_ChunkTextureSize = 36 * 8;
        private float m_WorldCellSize = 2.0f * 8;
        private TextureFormat m_TextureFormat = TextureFormat.BC7;

        protected bool m_LockSizes = false;
#if SAFE_EXECUTION
        //Texture optimal usage check
        private Dictionary<Hash128, int> m_TextureLookup = new Dictionary<Hash128, int>(256);
        private Hash128 m_BlankHash;
#endif

        private TextureAtlas m_TextureAtlas;
        private GridArray<int> m_CellTextures = new GridArray<int>(8, 16);
        private GridArray<GridChunk> m_Chunks = new GridArray<GridChunk>(8, 16);
        private DirectTexture2D m_BlankTexture;
        private Material m_GridMaterial;

        //Called by AddCellTexture upon first texture added.
        private void Init()
        {
            m_TextureAtlas = new TextureAtlas(new Vector2Int(cellTextureSize, cellTextureSize), new Vector2Int(16, 16), textureFormat);

            m_LockSizes = true;

            m_BlankTexture = DirectGraphics.CreateTexture(cellTextureSize, cellTextureSize, textureFormat);
            DirectGraphics.ClearTexture(m_BlankTexture.nativePointer);
            m_TextureAtlas.AddTexture(m_BlankTexture.texture);
#if SAFE_EXECUTION
            //Texture optimal usage check
            m_BlankHash = m_BlankTexture.texture.imageContentsHash;
#endif

            m_GridMaterial = new Material(Shader.Find("Sprites/Default"));
            m_GridMaterial.enableInstancing = true;
            m_GridMaterial.mainTexture = m_TextureAtlas.fullTexture;
            theGrandMesh = new Mesh();

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = theGrandMesh;

            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();

            meshRenderer.material = m_GridMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.allowOcclusionWhenDynamic = true;
        }

        /// <summary>
        /// Add texture to the internal texture atlas. The inputted texture is copied to the atlas so it is okay to change and destroy the texture without affecting the internal atlas.
        /// The inputted texture's format must match the TextureGrid.textureFormat.
        /// </summary>
        public int AddCellTexture(Texture2D cellTexture)
        {
#if SAFE_EXECUTION
            if(cellTexture.width != cellTextureSize || cellTexture.height != cellTextureSize)
                throw new ArgumentException("Inputted tile texture does not match tilemap tile texture size.", nameof(cellTexture));
            if(cellTexture.format != m_TextureFormat)
                throw new ArgumentException("Specified texture format must match tilemap format of '" + m_TextureFormat + "'.", nameof(cellTexture));
#endif
            if(!m_LockSizes)
            {
                Init();
            }

#if SAFE_EXECUTION
            //Texture optimal usage check
            Hash128 hash = cellTexture.imageContentsHash;
            if(hash == m_BlankHash)
                throw new ArgumentException("Inputted texture is blank. Use TextureGrid.ClearCellTexture instead.", nameof(cellTexture));

            int atlasIndex;
            if(!m_TextureLookup.TryGetValue(hash, out atlasIndex))
            {
                atlasIndex = m_TextureAtlas.AddTexture(cellTexture);
                m_TextureLookup.Add(hash, atlasIndex);
            }
            return atlasIndex;
#else
            return m_TextureAtlas.AddTexture(cellTexture);
#endif
        }

        /// <summary>
        /// An alternative way of setting a cell's texture without creating a Tile instance.
        /// </summary>
        public void SetCellTexture(int x, int y, int textureIndex)
        {
#if SAFE_EXECUTION
            if(textureIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(textureIndex), "Texture Index must be 0 or more.");
            if(textureIndex >= m_TextureAtlas.textureCount)
                throw new IndexOutOfRangeException("Tile at (" + x.ToString() + ", " + y.ToString() + ") with invalid texture atlas index of '" + textureIndex.ToString() + "'. Texture atlas only has '" + m_TextureAtlas.textureCount + "' textures added. Add textures with TextureGrid.AddCellTexture.");
#endif
            //if(m_TileTextures.size < FastMath.Abs(x) || m_TileTextures.size > FastMath.Abs(y))
            {
                //GetChunk(x, y).AddDirtyTile(x, y, textureIndex);
                //m_TileTextures.SetItem(x, y, textureIndex);
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

            GridChunk chunk = GetChunk(x, y);

            chunk.SetTexture(x, y, textureIndex);

            m_CellTextures.SetItem(x, y, textureIndex);
        }

        /// <summary>
        /// Set the texture of a cell.
        /// </summary>
        public void SetCellTexture(Vector2Int cell, int textureIndex)
        {
            SetCellTexture(cell.x, cell.y, textureIndex);
        }

        /// <summary>
        /// Set the cell to a clear texture. If the chunk is full of empty textures the mesh will be destroyed.
        /// </summary>
        public void ClearCellTexture(int x, int y)
        {
            GridChunk chunk = TryGetChunk(x, y);
            if(chunk == null) return;

            m_CellTextures.SetItem(x, y, -1);
            chunk.ClearTexture(x, y);
        }

        public void ClearCellTexture(Vector2Int cell)
        {
            ClearCellTexture(cell.x, cell.y);
        }

        //Create chunk if it doesn't exist
        private GridChunk GetChunk(int cellPositionX, int cellPositionY)
        {
            int negativityBoost = (((cellPositionX & int.MinValue) >> 31) & 1); ;
            int chunkPositionX = ((cellPositionX + negativityBoost) / chunkSize) - negativityBoost;
            negativityBoost = (((cellPositionY & int.MinValue) >> 31) & 1); ;
            int chunkPositionY = ((cellPositionY + negativityBoost) / chunkSize) - negativityBoost;

            GridChunk chunk = m_Chunks.GetItem(chunkPositionX, chunkPositionY);
            if(chunk == null)
            {
                chunk = new GridChunk(chunkPositionX, chunkPositionY, this);
                m_Chunks.SetItem(chunkPositionX, chunkPositionY, chunk);
            }

            return chunk;
        }

        //Get chunk if it exists, can be null
        private GridChunk TryGetChunk(int cellPositionX, int cellPositionY)
        {
            int negativityBoost = (((cellPositionX & int.MinValue) >> 31) & 1); ;
            int chunkPositionX = ((cellPositionX + negativityBoost) / chunkSize) - negativityBoost;
            negativityBoost = (((cellPositionY & int.MinValue) >> 31) & 1); ;
            int chunkPositionY = ((cellPositionY + negativityBoost) / chunkSize) - negativityBoost;

            return m_Chunks.GetItem(chunkPositionX, chunkPositionY);
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

        public enum RenderingMode
        {
            CombineMeshes,
            IndividualUVs
        }

        static public RenderingMode renderingMode = RenderingMode.IndividualUVs;

        #endregion
        Mesh theGrandMesh;
        Mesh theTilemap;
        internal class GridChunk
        {

            public int chunkX;
            public int chunkY;
            public TextureGrid textureGrid;
            public Mesh mesh;

            private GameObject m_ChunkObject;
            private DirectTexture2D m_DirectTexture;

            //Mesh stuff
            private Vector3[] vertices;
            private Vector2[] uvs;
            private Color32[] colors;

            public GridChunk(int chunkX, int chunkY, TextureGrid textureGrid)
            {
                this.chunkX = chunkX;
                this.chunkY = chunkY;
                this.textureGrid = textureGrid;

                m_ChunkObject = new GameObject("Tilemap Chunk (" + chunkX.ToString() + ", " + chunkY.ToString() + ")");
                m_ChunkObject.transform.SetParent(textureGrid.transform);
                m_ChunkObject.transform.localPosition = new Vector3(chunkX * textureGrid.m_WorldCellSize, chunkY * textureGrid.m_WorldCellSize, 0.0f);

                m_DirectTexture = DirectGraphics.CreateTexture(textureGrid.m_ChunkTextureSize, textureGrid.m_ChunkTextureSize, textureGrid.textureFormat);
                DirectGraphics.ClearTexture(m_DirectTexture.nativePointer);

                MeshFilter meshFilter = m_ChunkObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = m_ChunkObject.AddComponent<MeshRenderer>();

                mesh = new Mesh();

                if(renderingMode == RenderingMode.CombineMeshes)
                {


                    Vector3[] vertices = new Vector3[4]
                    {
                        new Vector3(0, 0, 0),
                        new Vector3(textureGrid.m_WorldCellSize, 0, 0),
                        new Vector3(0, textureGrid.m_WorldCellSize, 0),
                        new Vector3(textureGrid.m_WorldCellSize, textureGrid.m_WorldCellSize, 0)
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

                    /*Vector3[] normals = new Vector3[4]
                    {
                        -Vector3.forward,
                        -Vector3.forward,
                        -Vector3.forward,
                        -Vector3.forward
                    };
                    mesh.normals = normals;

                    */
                    return;
                    mesh.RecalculateNormals();
                    Vector2[] uv = new Vector2[4]
                    {
                        new Vector2(0, 0),
                        new Vector2(1, 0),
                        new Vector2(0, 1),
                        new Vector2(1, 1)
                    };
                    mesh.uv = uv;
                    int subMeshCount = textureGrid.theGrandMesh.subMeshCount;

                    textureGrid.theGrandMesh.subMeshCount++;
                    SubMeshDescriptor des = new SubMeshDescriptor()
                    {
                        
                    };
                    //textureGrid.theGrandMesh.SetSubMesh(subMeshCount, )
                    
                    CombineInstance combine = new CombineInstance()
                    {
                        mesh = mesh,
                        transform = m_ChunkObject.transform.localToWorldMatrix,
                    };

                    CombineInstance grandCombine = new CombineInstance()
                    {
                        mesh = textureGrid.theGrandMesh,
                        transform = textureGrid.transform.localToWorldMatrix,
                        
                    };

                    CombineInstance[] meshesToCombine = new CombineInstance[2]
                    {
                        combine,
                        grandCombine
                    };
                    textureGrid.theGrandMesh = new Mesh();
                    //textureGrid.theGrandMesh.CombineMeshes(meshesToCombine, false,);
                    textureGrid.GetComponent<MeshFilter>().mesh = textureGrid.theGrandMesh;
                    textureGrid.GetComponent<MeshRenderer>().material.mainTexture = m_DirectTexture.texture;

                    /*
                    meshFilter.mesh = mesh;

                    //mRenderer.sharedMaterial = tilemap.m_TilemapMaterial;
                    meshRenderer.material = textureGrid.m_GridMaterial;
                    meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                    meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                    meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    meshRenderer.allowOcclusionWhenDynamic = true;

                    meshRenderer.material.mainTexture = m_DirectTexture.texture;
                    */
                }
                else if(renderingMode == RenderingMode.IndividualUVs)
                {
                    int cellCount = textureGrid.chunkSize * textureGrid.chunkSize;

                    vertices = new Vector3[4 * cellCount];
                    int[] triangles = new int[6 * cellCount];
                    uvs = new Vector2[vertices.Length];
                    colors = new Color32[vertices.Length];

                    for(int i = 0; i < cellCount; i++)
                    {
                        int index0 = i * 4;
                        int index1 = index0 + 1;
                        int index2 = index0 + 2;
                        int index3 = index0 + 3;

                        //Vertices
                        Vector2Int coord = Utils.IndexToCoord(i, textureGrid.chunkSize);
                        float targetX = coord.x * textureGrid.cellSize;
                        float targetY = coord.y * textureGrid.cellSize;

                        vertices[index0] = new Vector3(targetX, targetY, 0);
                        vertices[index1] = new Vector3(targetX + textureGrid.cellSize,targetY, 0);
                        vertices[index2] = new Vector3(targetX, targetY + textureGrid.cellSize, 0);
                        vertices[index3] = new Vector3(targetX + textureGrid.cellSize, targetY + textureGrid.cellSize, 0);

                        //Triangles
                        int triangleIndex0 = i * 6;
                        triangles[triangleIndex0] = index0;
                        triangles[triangleIndex0 + 1] = index2;
                        triangles[triangleIndex0 + 2] = index1;

                        triangles[triangleIndex0 + 3] = index2;
                        triangles[triangleIndex0 + 4] = index3;
                        triangles[triangleIndex0 + 5] = index1;

                        //UVs
                        //uvs[index0] = Vector2.zero;
                        //uvs[index1] = Vector2.right;
                        //uvs[index2] = Vector2.up;
                        //uvs[index3] = Vector2.one;
                        Vector2 max = new Vector2((textureGrid.m_TextureAtlas.textureSize.x - 0.002f) / (float)textureGrid.m_TextureAtlas.fullTexture.width, (textureGrid.m_TextureAtlas.textureSize.y - 0.002f) / (float)textureGrid.m_TextureAtlas.fullTexture.height);
                        uvs[index0] = Vector2.zero;
                        uvs[index1] = new Vector2(max.x, 0.0f);
                        uvs[index2] = new Vector2(0.0f, max.y);
                        uvs[index3] = max;
                    }
                    mesh.vertices = vertices;
                    mesh.triangles = triangles;
                    mesh.uv = uvs;

                    mesh.RecalculateNormals();
                    meshFilter.mesh = mesh;
                    meshRenderer.sharedMaterial = textureGrid.m_GridMaterial;
                    
                }

            }

            public void SetTexture(int cellX, int cellY, int textureAtlasIndex)
            {
                //Debug.Log("SetTexture: Chunk: " + chunkX.ToString() + ", " + chunkY.ToString() + " Cell: " + cellX.ToString() + ", " + cellY.ToString());
                Vector2Int sourcePosition = textureGrid.m_TextureAtlas.AtlasIndexToPixelCoord(textureAtlasIndex);
                Vector2Int destination = GetTileTexturePosition(cellX, cellY);

                //int localCellX = cellX % textureGrid.chunkSize;
                //int localCellY = cellY % textureGrid.chunkSize;

                int localCellX = cellX - (chunkX * textureGrid.chunkSize);
                int localCellY = cellY - (chunkY * textureGrid.chunkSize);

                //Debug.Log("Local Cell: " + localCellX.ToString() + ", " + localCellY.ToString()); 
                //return;
                //DirectGraphics.CopyTexture(textureGrid.m_TextureAtlas.nativeTexturePointer, sourcePosition.x, sourcePosition.y, textureGrid.cellTextureSize, textureGrid.cellTextureSize, m_DirectTexture.nativePointer, destination.x, destination.y);
                int index = Utils.CoordToIndex(FastMath.Abs(localCellX), FastMath.Abs(localCellY), textureGrid.chunkSize) * 4;
                Vector2Int pixelCoordinate = textureGrid.m_TextureAtlas.AtlasIndexToPixelCoord(textureAtlasIndex);
                
                Vector2 min = new Vector2(pixelCoordinate.x / (float)textureGrid.m_TextureAtlas.fullTexture.width, pixelCoordinate.y / (float)textureGrid.m_TextureAtlas.fullTexture.height);
                Vector2 max = new Vector2(((pixelCoordinate.x + textureGrid.m_TextureAtlas.textureSize.x) - 0.002f) / (float)textureGrid.m_TextureAtlas.fullTexture.width, ((pixelCoordinate.y + textureGrid.m_TextureAtlas.textureSize.y) - 0.002f) / (float)textureGrid.m_TextureAtlas.fullTexture.height);
                uvs[index] = min;
                uvs[index+1] = new Vector2(max.x, min.y);
                uvs[index+2] = new Vector2(min.x, max.y);
                uvs[index+3] = max;

                mesh.uv = uvs;
            }

            public void ClearTexture(int cellX, int cellY)
            {
                Vector2Int destination = GetTileTexturePosition(cellX, cellY);
                //DirectGraphics.CopyTexture(textureGrid.m_BlankTexture.nativePointer, 0, 0, textureGrid.cellTextureSize, textureGrid.cellTextureSize, m_DirectTexture.nativePointer, destination.x, destination.y);

                SetTexture(cellX, cellY, 0);
            }

            private Vector2Int GetTileTexturePosition(int x, int y)
            {
                Vector2Int v = new Vector2Int(Mathf.FloorToInt(x / (float)textureGrid.chunkSize) * textureGrid.chunkSize, Mathf.FloorToInt(y / (float)textureGrid.chunkSize) * textureGrid.chunkSize);

                return new Vector2Int((-(v.x - x)) * textureGrid.cellTextureSize, (-(v.y - y)) * textureGrid.cellTextureSize);
            }
        }
    }
}