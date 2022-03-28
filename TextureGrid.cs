using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;

using Elanetic.Graphics;
using Elanetic.Tools;

namespace Elanetic.Tilemaps
{
    /// <summary>
    /// A square grid based tilemap. Add textures to the texture atlas and set textures for each 
    /// </summary>
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
                if(m_LockSizes)
                    throw new InvalidOperationException("Cannot change the chunk size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_ChunkSize = value;
                m_ChunkTextureSize = cellTextureSize * m_ChunkSize;
                m_WorldCellSize = cellSize * chunkSize;
                m_TotalCellCountPerChunk = m_ChunkSize * m_ChunkSize;
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
                if(m_LockSizes)
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
                if(m_LockSizes)
                    throw new InvalidOperationException("Cannot change the cell size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_CellSize = value;
                m_HalfCellSize = m_CellSize * 0.5f;
                m_WorldCellSize = cellSize * chunkSize;
            }
        }

        /// <summary>
        /// The texture format used for cells. Compressed textures means smaller memory sizes and faster texture copies but reduced features. Default is BC7.
        /// </summary>
        public TextureFormat textureFormat
        {
            get => m_TextureFormat;
            set
            {
#if SAFE_EXECUTION
                if(m_LockSizes)
                    throw new InvalidOperationException("Cannot change the texture format for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_TextureFormat = value;
            }
        }

        internal TextureAtlas textureAtlas;

        private int m_ChunkSize = 8;
        private int m_CellTextureSize = 36;
        private float m_CellSize = 2.0f;
        private float m_HalfCellSize = 2.0f * 0.5f;
        private int m_ChunkTextureSize = 36 * 8;
        private float m_WorldCellSize = 2.0f * 8;
        private int m_TotalCellCountPerChunk = 64;
        private TextureFormat m_TextureFormat = TextureFormat.BC7;

        protected bool m_LockSizes = false;
#if SAFE_EXECUTION
        //Texture optimal usage check
        private Dictionary<Hash128, int> m_TextureLookup = new Dictionary<Hash128, int>(256);
        private Hash128 m_BlankHash;
#endif

        private ChunkedGridArray<int> m_CellTextures = new ChunkedGridArray<int>(16, 8, 16);
        private ChunkedGridArray<GridChunk> m_Chunks = new ChunkedGridArray<GridChunk>(16, 8, 16);
        private DirectTexture2D m_BlankTexture;
        private Material m_GridMaterial;
        private Mesh m_ChunkMesh;
        private Texture2D m_IndexCopyTexture;
        private IntPtr m_IndexCopyPointer;

        //Called by AddCellTexture upon first texture added.
        private void Init()
        {
            textureAtlas = new TextureAtlas(new Vector2Int(cellTextureSize, cellTextureSize), new Vector2Int(16, 16), textureFormat);
            m_LockSizes = true;

            m_BlankTexture = DirectGraphics.CreateTexture(cellTextureSize, cellTextureSize, textureFormat);
            DirectGraphics.ClearTexture(m_BlankTexture.nativePointer);
            textureAtlas.AddTexture(m_BlankTexture.texture);

#if SAFE_EXECUTION
            //Texture optimal usage check
            m_BlankHash = m_BlankTexture.texture.imageContentsHash;
#endif

            m_GridMaterial = new Material(Shader.Find("Elanetic/Tilemap"));
            m_GridMaterial.SetTexture("_TextureAtlas", textureAtlas.fullTexture);
            m_GridMaterial.SetFloat("_CellSize", cellSize);
            m_GridMaterial.SetFloat("_GridSize", chunkSize);
            m_GridMaterial.SetFloat("_AtlasWidthCount", textureAtlas.maxTextureCount.x);

            m_ChunkMesh = new Mesh();

            //Scale the meshes to be a little bigger that way seams between chunks are, well, seamless.
            float bleedFixAmount = 0.0005f;

            Vector3[] vertices = new Vector3[4]
            {
                    new Vector3(-bleedFixAmount, -bleedFixAmount, 0),
                    new Vector3(m_WorldCellSize+bleedFixAmount, -bleedFixAmount, 0),
                    new Vector3(-bleedFixAmount, m_WorldCellSize+bleedFixAmount, 0),
                    new Vector3(m_WorldCellSize+bleedFixAmount, m_WorldCellSize+bleedFixAmount, 0)
            };
            m_ChunkMesh.vertices = vertices;

            int[] tris = new int[6]
            {
                    // lower left triangle
                    0, 2, 1,
                    // upper right triangle
                    2, 3, 1
            };
            m_ChunkMesh.triangles = tris;

            m_ChunkMesh.RecalculateNormals();
            Vector2[] uv = new Vector2[4]
            {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
            };
            m_ChunkMesh.uv = uv;

            m_IndexCopyTexture = new Texture2D(256, 1, TextureFormat.R8, false, true);
            NativeArray<byte> indexData = m_IndexCopyTexture.GetRawTextureData<byte>();
            for(int i = 0; i < indexData.Length; i++)
            {
                indexData[i] = (byte)i;
            }
            m_IndexCopyTexture.anisoLevel = 0;
            m_IndexCopyTexture.filterMode = FilterMode.Point;
            m_IndexCopyTexture.wrapMode = TextureWrapMode.Clamp;
            m_IndexCopyTexture.Apply(false, true);
            m_IndexCopyPointer = m_IndexCopyTexture.GetNativeTexturePtr();

#if UNITY_EDITOR
            //Editor only optimization
            m_ClearData = new byte[m_TotalCellCountPerChunk];
#endif
        }

        /// <summary>
        /// Add texture to the internal texture atlas. The inputted texture is copied to the atlas so it is okay to change and destroy the texture without affecting the internal atlas.
        /// The inputted texture's format must match the TextureGrid.textureFormat.
        /// </summary>
        public int AddCellTexture(Texture2D cellTexture)
        {
#if SAFE_EXECUTION
            if(cellTexture.width != cellTextureSize || cellTexture.height != cellTextureSize)
                throw new ArgumentException("Inputted texture does not match texture grid texture size.", nameof(cellTexture));
            if(cellTexture.format != m_TextureFormat)
                throw new ArgumentException("Specified texture format must match texture grid format of '" + m_TextureFormat + "'.", nameof(cellTexture));
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
                atlasIndex = textureAtlas.AddTexture(cellTexture);
                m_TextureLookup.Add(hash, atlasIndex);
            }
            return atlasIndex;
#else
            return m_TextureAtlas.AddTexture(cellTexture);
#endif
        }

        /// <summary>
        /// Set the texture of a cell.
        /// </summary>
        public void SetCellTexture(int x, int y, int textureIndex)
        {
#if SAFE_EXECUTION
            if(textureIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(textureIndex), "Inputted texture Index must be 0 or more.");
            if(textureIndex >= textureAtlas.textureCount)
                throw new IndexOutOfRangeException("Inputted invalid texture atlas index '" + textureIndex.ToString() + "'. Texture atlas only has '" + textureAtlas.textureCount + "' textures added. Add textures with TextureGrid.AddCellTexture.");
#endif

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
                //chunk = new GridChunk(chunkPositionX, chunkPositionY, this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                GameObject chunkObject = new GameObject("Chunk (" + chunkPositionX.ToString() + ", " + chunkPositionY.ToString() + ")");
#else
                GameObject chunkObject = new GameObject("Chunk");
#endif
                chunkObject.transform.SetParent(transform);
                chunkObject.transform.localPosition = new Vector3(chunkPositionX * m_WorldCellSize, chunkPositionY * m_WorldCellSize, 0.0f);

                chunk = chunkObject.AddComponent<GridChunk>();
                chunk.chunkX = chunkPositionX;
                chunk.chunkY = chunkPositionY;

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

        #endregion

#if UNITY_EDITOR
        private byte[] m_ClearData;
#endif
        internal class GridChunk : MonoBehaviour
        {
            public int chunkX;
            public int chunkY;
            public TextureGrid textureGrid;

            private Texture2D m_DataTexture;
            private IntPtr m_DataTexturePointer;

            private bool m_IsVisible = false;
            private bool m_IsDirty = false;

            private NativeArray<byte> m_Data;

            void Awake()
            {
                textureGrid = transform.parent.GetComponent<TextureGrid>();

                m_DataTexture = new Texture2D(textureGrid.chunkSize, textureGrid.chunkSize, TextureFormat.R8, false, true);
                m_DataTexture.wrapMode = TextureWrapMode.Clamp;
                m_DataTexture.anisoLevel = 0;
                m_DataTexture.filterMode = FilterMode.Point;
                m_Data = m_DataTexture.GetRawTextureData<byte>();

#if UNITY_EDITOR
                //We load data the "slow" way in the editor since NativeArray's safety checks are even slower in the editor but fast as heck in build
                m_DataTexture.LoadRawTextureData(textureGrid.m_ClearData);
#else
                for(int i = 0; i < textureGrid.m_TotalCellCountPerChunk; i++)
                {
                    m_Data[i] = 0;
                }
#endif
                m_DataTexture.Apply(false, false);
                m_DataTexturePointer = m_DataTexture.GetNativeTexturePtr();
                m_Data = m_DataTexture.GetRawTextureData<byte>();

                

                MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();

                meshFilter.sharedMesh = textureGrid.m_ChunkMesh;
                meshRenderer.material = textureGrid.m_GridMaterial;
                meshRenderer.material.SetTexture("_TextureIndices", m_DataTexture);

                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                meshRenderer.allowOcclusionWhenDynamic = true;                
            }

            private void OnDestroy()
            {
                Destroy(m_DataTexture);
            }

            public void SetTexture(int cellX, int cellY, int textureAtlasIndex)
            {
                int localCellX = cellX - (chunkX * textureGrid.chunkSize);
                int localCellY = cellY - (chunkY * textureGrid.chunkSize);

                int textureCoordinateX = FastMath.Abs(localCellX);
                int textureCoordinateY = FastMath.Abs(localCellY);
                int index = Utils.CoordToIndex(textureCoordinateX, textureCoordinateY, textureGrid.chunkSize);

                m_Data[index] = (byte)textureAtlasIndex;
                if(!m_IsVisible)
                {
                    m_IsDirty = true;
                }
                else
                {
                    DirectGraphics.CopyTexture(textureGrid.m_IndexCopyPointer, textureAtlasIndex, 0, 1, 1, m_DataTexturePointer, textureCoordinateX, textureCoordinateY);
                }
            }

            public void ClearTexture(int cellX, int cellY)
            {
                SetTexture(cellX, cellY, 0);
            }


            private void OnBecameVisible()
            {
                if(m_IsVisible) return;

                UpdateVisibility();
            }

            private void OnBecameInvisible()
            {
                m_IsVisible = false;
            }

            private void OnWillRenderObject()
            {
                if(m_IsVisible) return;

                UpdateVisibility();
            }

            private void UpdateVisibility()
            {
                m_IsVisible = true;

                if(!m_IsDirty) return;

                m_DataTexture.Apply(false, false);
                m_DataTexturePointer = m_DataTexture.GetNativeTexturePtr();
                m_Data = m_DataTexture.GetRawTextureData<byte>();
            }
        }
    }
}