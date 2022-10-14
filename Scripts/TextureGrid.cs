using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

using Elanetic.Graphics;
using Elanetic.Tools;
using Elanetic.Tools.Unity;

namespace Elanetic.Tilemaps
{
    /// <summary>
    /// A grid of square textures. Add textures to the texture atlas and set textures for individual cells.
    /// TODO: Support Unity Transform rotation and scaling.
    /// </summary>
    public class TextureGrid : MonoBehaviour
    {
        /// <summary>
        /// The texture size for each cell.
        /// </summary>
        public int cellTextureSize
        {
            get => m_CellTextureSize;
            set
            {
#if DEBUG
                if(m_LockSizes)
                    throw new InvalidOperationException("Cannot change the cell texture size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_CellTextureSize = value;
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
#if DEBUG
                if(m_LockSizes)
                    throw new InvalidOperationException("Cannot change the cell size for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_CellSize = value;
                m_HalfCellSize = m_CellSize * 0.5f;
                m_WorldCellSize = m_CellSize * m_ChunkSize;
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
#if DEBUG
                if(m_LockSizes)
                    throw new InvalidOperationException("Cannot change the texture format for " + GetType().Name + " as it has been initialized. Create a new instance instead.");
#endif
                m_TextureFormat = value;
            }
        }

        /// <summary>
        /// The render queue for the material.
        /// </summary>
        public int renderQueue
        {
            get => m_Material.renderQueue;
            set
            {
                m_Material.renderQueue = value;
            }
        }

        private ComputeBuffer m_ArgumentBuffer;
        private ComputeBuffer m_DataBuffer;
        private TextureAtlas m_TextureAtlas;
        private Material m_Material;
        private NativeArray<byte> m_ChunkData;

        //Chunk size is hard coded in the shader due to shader data structs needing to be defined at compile time.
        //8x8 chunk size is an arbitruary choice
        private const int m_ChunkSize = 8;
        private int m_CellTextureSize = 36;
        private float m_CellSize = 2.0f;
        private float m_HalfCellSize = 2.0f * 0.5f;
        private float m_WorldCellSize = 2.0f * 8;
        private TextureFormat m_TextureFormat = TextureFormat.RGBA32;

        const int m_StrideSize = (sizeof(float) * 2) + (sizeof(uint) * (64 / 4));

        protected bool m_LockSizes = false;
#if UNITY_EDITOR && DEBUG
        //Texture optimal usage check
        //Texture.imageContentHash only exists in the Unity Editor.
        private Dictionary<Hash128, int> m_TextureLookup = new Dictionary<Hash128, int>(256);
        private Hash128 m_BlankHash;
#endif

        static private Mesh m_TileMesh = null;
        static private Shader m_Shader = null;
        static private int m_ShaderTextureAtlasID;
        static private int m_ShaderChunkSizeID;
        static private int m_ShaderAtlasWidthCountID;
        static private int m_ShaderChunkDataID;

        protected virtual void Awake()
        {
            if(m_TileMesh.IsNull())
            {
                m_TileMesh = new Mesh();
                Vector3[] vertices = new Vector3[4]
                {
                    new Vector3(0, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(1, 1, 0)
                };
                m_TileMesh.vertices = vertices;

                int[] tris = new int[6]
                {
                    // lower left triangle
                    0, 2, 1,
                    // upper right triangle
                    2, 3, 1
                };
                m_TileMesh.triangles = tris;

                m_TileMesh.RecalculateNormals();
                Vector2[] uv = new Vector2[4]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
                };
                m_TileMesh.uv = uv;

                //Instantiate material/shader
                m_Shader = Shader.Find("Elanetic/TextureGrid");
                m_ShaderTextureAtlasID = Shader.PropertyToID("_TextureAtlas");
                m_ShaderChunkSizeID = Shader.PropertyToID("_ChunkWorldSize");
                m_ShaderAtlasWidthCountID = Shader.PropertyToID("_AtlasWidthCount");
                m_ShaderChunkDataID = Shader.PropertyToID("_ChunkData");
            }

            m_Material = new Material(m_Shader);
            m_Material.enableInstancing = true;
            m_Material.renderQueue = (int)RenderQueue.Transparent;

            m_ChunkData = new NativeArray<byte>(m_MaxChunkCount * m_StrideSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        void OnEnable()
        {
            m_ArgumentBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
            m_ArgumentBuffer.SetData(new uint[5] { m_TileMesh.GetIndexCount(0), m_RenderCount, m_TileMesh.GetIndexStart(0), m_TileMesh.GetBaseVertex(0), 0 });

            m_DataBuffer = new ComputeBuffer(m_MaxChunkCount, m_StrideSize, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            NativeArray<byte> targetData = m_DataBuffer.BeginWrite<byte>(0, m_ChunkData.Length);
            targetData.CopyFrom(m_ChunkData);
            m_DataBuffer.EndWrite<byte>(m_ChunkData.Length);
            m_Material.SetBuffer(m_ShaderChunkDataID, m_DataBuffer);

            Camera.onPreCull += OnCameraPreCull;
        }

        void OnDisable()
        {
            m_ArgumentBuffer?.Dispose();
            m_DataBuffer?.Dispose();

            Camera.onPreCull -= OnCameraPreCull;
        }

        void OnDestroy()
        {
            if(m_ChunkData != null)
            {
                m_ChunkData.Dispose();
            }
        }

        //Called by AddCellTexture upon first texture added.
        private void Init()
        {
            m_TextureAtlas = new TextureAtlas(new Vector2Int(m_CellTextureSize, m_CellTextureSize), new Vector2Int(16, 16), m_TextureFormat);

            m_LockSizes = true;

            DirectTexture2D blankTexture = DirectGraphics.CreateTexture(m_CellTextureSize, m_CellTextureSize, m_TextureFormat);
            DirectGraphics.ClearTexture(blankTexture.nativePointer);
            m_TextureAtlas.AddTexture(blankTexture.texture);
            blankTexture.Destroy();

            //Set material properties
            m_Material.SetTexture(m_ShaderTextureAtlasID, m_TextureAtlas.fullTexture);
            m_Material.SetFloat(m_ShaderChunkSizeID, m_WorldCellSize);
            m_Material.SetInt(m_ShaderAtlasWidthCountID, m_TextureAtlas.maxTextureCount.x);
            m_Material.SetBuffer(m_ShaderChunkDataID, m_DataBuffer);

#if UNITY_EDITOR && DEBUG
            //Texture optimal usage check
            //Texture.imageContentHash only exists in the Unity Editor.
            m_BlankHash = blankTexture.texture.imageContentsHash;
#endif
        }

        #region Tranform

        public Vector2Int LocalToCell(Vector2 localPosition)
        {
            return new Vector2Int(Mathf.FloorToInt(localPosition.x / m_CellSize), Mathf.FloorToInt(localPosition.y / m_CellSize));
        }

        public Vector2Int WorldToCell(Vector2 worldPosition)
        {
            return LocalToCell(transform.InverseTransformPoint(worldPosition));
        }

        public Vector2 CellToLocal(Vector2Int cellPosition)
        {
            return new Vector2(cellPosition.x * m_CellSize, cellPosition.y * m_CellSize);
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

        /// <summary>
        /// Add texture to the internal texture atlas. The inputted texture is copied to the atlas so it is okay to change and destroy the texture without affecting the internal atlas.
        /// The inputted texture's format must match the TextureGrid.textureFormat.
        /// </summary>
        public int AddCellTexture(Texture2D cellTexture)
        {
#if DEBUG
            if(cellTexture == null)
                throw new NullReferenceException("Inputted texture is null.");
            if(cellTexture.width != m_CellTextureSize || cellTexture.height != m_CellTextureSize)
                throw new ArgumentException("Inputted texture does not match texture grid texture size.", nameof(cellTexture));
            if(cellTexture.format != m_TextureFormat)
                throw new ArgumentException("Specified texture format must match texture grid format of '" + m_TextureFormat + "'.", nameof(cellTexture));
#endif
            if(!m_LockSizes)
            {
                Init();
            }

#if UNITY_EDITOR && DEBUG
            //Texture optimal usage check
            //Texture.imageContentHash only exists in the Unity Editor.
            Hash128 hash = cellTexture.imageContentsHash;
            if(hash == m_BlankHash)
                throw new ArgumentException("Inputted texture is blank. Use TextureGrid.ClearCellTexture instead.", nameof(cellTexture));

            int atlasIndex;
            if(!m_TextureLookup.TryGetValue(hash, out atlasIndex))
            {
                atlasIndex = m_TextureAtlas.AddTexture(cellTexture);
                m_TextureLookup.Add(hash, atlasIndex);
            }
            else
            {
                Debug.LogWarning("Texture '" + cellTexture.name + "' appears to have already been added to the texture atlas for the texture grid. Expect unexpected execution in builds.");
            }
            return atlasIndex;
#else
            return m_TextureAtlas.AddTexture(cellTexture);
#endif
        }

        ChunkedGridArray<int> m_ChunkDataArray = new ChunkedGridArray<int>();

        public void SetCellTexture(Vector2Int cellPosition, int textureIndex)
        {
            SetCellTexture(cellPosition.x, cellPosition.y, textureIndex);
        }

        public void SetCellTexture(int x, int y, int textureIndex)
        {
#if DEBUG
            if(m_TextureAtlas == null)
                throw new InvalidOperationException("Inputted invalid texture atlas index '" + textureIndex.ToString() + "'. Texture atlas has no textures added. Add textures with TextureGrid.AddCellTexture.");
            if(textureIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(textureIndex), "Inputted texture Index must be 0 or more.");
            if(textureIndex >= m_TextureAtlas.textureCount)
                throw new IndexOutOfRangeException("Inputted invalid texture atlas index '" + textureIndex.ToString() + "'. Texture atlas only has '" + m_TextureAtlas.textureCount + "' textures added. Add textures with TextureGrid.AddCellTexture.");
#endif
            int negativityBoost = (((x & int.MinValue) >> 31) & 1);
            int chunkPositionX = ((x + negativityBoost) / m_ChunkSize) - negativityBoost;
            negativityBoost = (((y & int.MinValue) >> 31) & 1);
            int chunkPositionY = ((y + negativityBoost) / m_ChunkSize) - negativityBoost;

            int chunkDataRef = m_ChunkDataArray.GetItem(chunkPositionX, chunkPositionY) - 1;

            if(chunkDataRef < 0)
            {
                if(textureIndex == 0) return;

                int targetIndex = m_ExistingCount * m_StrideSize;

                float absolutePosition = (FastMath.Abs(chunkPositionX)+1) * m_WorldCellSize;
                if(m_RenderBounds.extents.x < absolutePosition)
                {
                    m_RenderBounds.extents = new Vector3(absolutePosition, m_RenderBounds.extents.y, 1.0f);
                }
                absolutePosition = (FastMath.Abs(chunkPositionY)+1) * m_WorldCellSize;
                if(m_RenderBounds.extents.y < absolutePosition)
                {
                    m_RenderBounds.extents = new Vector3(m_RenderBounds.extents.x, absolutePosition, 1.0f);
                }

                if(m_ExistingCount == m_MaxChunkCount)
                {
                    //Do memory resizes
                    m_MaxChunkCount *= 2;

                    ResizeLocalData();

                    //Do local write
                    m_ChunkData.ReinterpretStore<Vector2>(targetIndex, new Vector2(chunkPositionX, chunkPositionY));

                    //We are doing the local write for the cell texture here since the local copy will be copied to the GPU side anyways in ResizeGPUData
                    int localCellX = FastMath.Abs(x - (chunkPositionX * m_ChunkSize));
                    int localCellY = FastMath.Abs(y - (chunkPositionY * m_ChunkSize));

                    int cellIndex = CoreUtilities.CoordToIndex(localCellX, localCellY, m_ChunkSize);

                    int targetWriteIndex = targetIndex + 8 + cellIndex;

                    m_ChunkData[targetWriteIndex] = (byte)textureIndex;

                    ResizeGPUData();
                }
                else
                {
                    int localCellX = FastMath.Abs(x - (chunkPositionX * m_ChunkSize));
                    int localCellY = FastMath.Abs(y - (chunkPositionY * m_ChunkSize));

                    int cellIndex = CoreUtilities.CoordToIndex(localCellX, localCellY, m_ChunkSize);
                    
                    int targetWriteIndex = targetIndex + 8 + cellIndex;

                    Vector2 chunkPositionVector = new Vector2(chunkPositionX, chunkPositionY);

                    //Do local write
                    m_ChunkData.ReinterpretStore(targetIndex, chunkPositionVector);
                    m_ChunkData[targetWriteIndex] = (byte)textureIndex;

                    //Only do GPU write due to data buffer being disposed
                    if(enabled)
                    {
                        //Do GPU write
                        NativeArray<byte> targetData = m_DataBuffer.BeginWrite<byte>(targetIndex, 8);
                        targetData.ReinterpretStore<Vector2>(0, chunkPositionVector);
                        m_DataBuffer.EndWrite<byte>(8);

                        targetData = m_DataBuffer.BeginWrite<byte>(targetWriteIndex, 1);
                        targetData[0] = (byte)textureIndex;
                        m_DataBuffer.EndWrite<byte>(1);
                    }
                }

                m_ExistingCount++;
                chunkDataRef = targetIndex;
                m_ChunkDataArray.SetItem(chunkPositionX, chunkPositionY, targetIndex + 1);
            }
            else
            {
                //TODO Destroy completely blank chunk
                int localCellX = FastMath.Abs(x - (chunkPositionX * m_ChunkSize));
                int localCellY = FastMath.Abs(y - (chunkPositionY * m_ChunkSize));

                int cellIndex = CoreUtilities.CoordToIndex(localCellX, localCellY, m_ChunkSize);

                int targetWriteIndex = chunkDataRef + 8 + cellIndex;

                //Do local write
                m_ChunkData[targetWriteIndex] = (byte)textureIndex;

                //Only do GPU write due to data buffer being disposed
                if(enabled)
                {
                    //Do GPU write
                    NativeArray<byte> targetData = m_DataBuffer.BeginWrite<byte>(targetWriteIndex, 1);
                    targetData[0] = (byte)textureIndex;
                    m_DataBuffer.EndWrite<byte>(1);
                }
            }
        }

        /// <summary>
        /// Set the cell to a clear texture. If the chunk is full of empty textures the mesh will be destroyed.
        /// </summary>
        public void ClearCellTexture(int x, int y)
        {
            SetCellTexture(x, y, 0);
        }

        public void ClearCellTexture(Vector2Int cell)
        {
            SetCellTexture(cell.x, cell.y, 0);
        }

        private int m_MaxChunkCount = 10;
        private int m_ExistingCount = 0;
        private uint m_RenderCount = 0;

        private void ResizeLocalData()
        {
            int oldChunkDataSize = m_ChunkData.Length;

            //Allocate new space
            NativeArray<byte> newNativeData = new NativeArray<byte>(m_MaxChunkCount * m_StrideSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if(m_ChunkData != null)
            {
                //Copy existing data to new allocated space and dispose of old
                NativeSlice<byte> slice = newNativeData.Slice<byte>(0, m_ChunkData.Length);
                slice.CopyFrom(m_ChunkData);
                m_ChunkData.Dispose();
            }
            m_ChunkData = newNativeData;

            //Set remaining newly allocated space to 0
            int newNativeDataLength = newNativeData.Length;
            for(int i = oldChunkDataSize; i < newNativeDataLength; i++)
            {
                newNativeData[i] = 0;
            }
        }

        private void ResizeGPUData()
        {
            //Data buffer and argument buffer are currently disposed if this script is not enabled. Chunk data will be applied OnEnable.
            if(!enabled) return;

            //Dispose and allocate new buffer
            m_DataBuffer.Dispose();
            m_DataBuffer = new ComputeBuffer(m_MaxChunkCount, m_StrideSize, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);

            //Copy local data to GPU buffer
            NativeArray<byte> targetData = m_DataBuffer.BeginWrite<byte>(0, m_ChunkData.Length);
            targetData.CopyFrom(m_ChunkData);
            m_DataBuffer.EndWrite<byte>(m_ChunkData.Length);

            m_Material.SetBuffer(m_ShaderChunkDataID, m_DataBuffer);
        }

        private Bounds m_RenderBounds = new Bounds(Vector3.zero, new Vector3(0, 0, 1));
        private uint m_LastRenderCount = 0;
        private void OnCameraPreCull(Camera camera)
        {
            //Only test culling on cameras that are on the same layer
            if((camera.cullingMask & (1 << gameObject.layer)) == 0)
            {
                return;
            }

            //Update Render Bounds and position
            m_RenderBounds.center = transform.position;

            //TODO Cull
            /*
            Plane p = new Plane(transform.position, transform.position + transform.up, transform.position + transform.right);

            Ray bottomLeftRay = camera.ViewportPointToRay(new Vector3(-0.01f, -0.01f,0.0f));
            Ray topRightRay = camera.ViewportPointToRay(new Vector3(1.01f,1.01f,0.0f));
            //Debug.DrawRay(bottomLeftRay.origin, bottomLeftRay.direction, Color.yellow, 0.1f, false);
            //Debug.DrawRay(topRightRay.origin, topRightRay.direction, Color.green, 0.1f, false);

            float enter = 0.0f;
            if(p.Raycast(bottomLeftRay, out enter))
            {
                //Utils.DrawPoint(bottomLeftRay.GetPoint(enter), 1.0f, Color.blue, 0.1f);
            }
            if(p.Raycast(topRightRay, out enter))
            {
                //Utils.DrawPoint(topRightRay.GetPoint(enter), 1.0f, Color.blue, 0.1f);
            }
            */

            m_RenderCount = (uint)m_ExistingCount;
            if(m_LastRenderCount != m_RenderCount)
            {
                m_LastRenderCount = m_RenderCount;
                NativeArray<uint> argumentSetter = m_ArgumentBuffer.BeginWrite<uint>(1, 1);
                argumentSetter[0] = m_LastRenderCount;
                m_ArgumentBuffer.EndWrite<uint>(1);
            }

            UnityEngine.Graphics.DrawMeshInstancedIndirect(m_TileMesh, 0, m_Material, m_RenderBounds, m_ArgumentBuffer, 0, null, ShadowCastingMode.Off, false, gameObject.layer, camera, LightProbeUsage.Off);
        }
    }
}