using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Random = UnityEngine.Random;

using Elanetic.Graphics;
using Elanetic.Tools;

namespace Elanetic.Tilemaps
{
    public class TextureGridCompute : MonoBehaviour
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

        ComputeBuffer argumentBuffer;
        ComputeBuffer dataBuffer;
        TextureAtlas textureAtlas;
        Material material;
        Mesh tileMesh;
        NativeArray<byte> chunkData;


        private int m_ChunkSize = 8;
        private int m_CellTextureSize = 36;
        private float m_CellSize = 2.0f;
        private float m_HalfCellSize = 2.0f * 0.5f;
        private int m_ChunkTextureSize = 36 * 8;
        private float m_WorldCellSize = 2.0f * 8;
        private int m_TotalCellCountPerChunk = 64;
        private TextureFormat m_TextureFormat = TextureFormat.BC7;

        const int m_StrideSize = (sizeof(float) * 2) + (sizeof(uint) * (64 / 4));

        protected bool m_LockSizes = false;

        void Awake()
        {
            textureAtlas = new TextureAtlas(new Vector2Int(36, 36), new Vector2Int(16, 16), TextureFormat.BC7);
            DirectTexture2D clearTexture = DirectGraphics.CreateTexture(36, 36, TextureFormat.BC7);
            DirectGraphics.ClearTexture(clearTexture.nativePointer);
            textureAtlas.AddTexture(clearTexture.texture);
            textureAtlas.AddTexture(Resources.Load<Texture2D>("FlatMetalFloor"));
            textureAtlas.AddTexture(Resources.Load<Texture2D>("Hull"));
            clearTexture.Destroy();
            material = Resources.Load<Material>("Materials/Texture Grid Compute");
            material.SetTexture("_TextureAtlas", textureAtlas.fullTexture);
            //Set the world size of the chunk
            material.SetFloat("_ChunkSize", m_WorldCellSize);
            material.SetInt("_GridSize", chunkSize);
            material.SetInt("_AtlasWidthCount", textureAtlas.maxTextureCount.x);

            tileMesh = new Mesh();
            Vector3[] vertices = new Vector3[4]
            {
                    new Vector3(0, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(1, 1, 0)
            };
            tileMesh.vertices = vertices;

            int[] tris = new int[6]
            {
                    // lower left triangle
                    0, 2, 1,
                    // upper right triangle
                    2, 3, 1
            };
            tileMesh.triangles = tris;

            tileMesh.RecalculateNormals();
            Vector2[] uv = new Vector2[4]
            {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
            };
            tileMesh.uv = uv;

            chunkData = new NativeArray<byte>(m_MaxChunkCount * m_StrideSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        void Start()
        {

            new GameObject("Texture Atlas Reference").AddComponent<SpriteRenderer>().sprite = Sprite.Create(textureAtlas.fullTexture, new Rect(0,0,textureAtlas.fullTexture.width, textureAtlas.fullTexture.height), Vector2.down, 36.0f, 0, SpriteMeshType.FullRect);
            SetCellTexture(0, 0, 1);
        }

        void OnEnable()
        {
            argumentBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments, ComputeBufferMode.SubUpdates);
            argumentBuffer.SetData(new uint[5] { tileMesh.GetIndexCount(0), m_RenderCount, tileMesh.GetIndexStart(0), tileMesh.GetBaseVertex(0), 0 });

            dataBuffer = new ComputeBuffer(m_MaxChunkCount, m_StrideSize, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            NativeArray<byte> targetData = dataBuffer.BeginWrite<byte>(0, chunkData.Length);
            targetData.CopyFrom(chunkData);
            dataBuffer.EndWrite<byte>(chunkData.Length);
            material.SetBuffer("_ChunkData", dataBuffer);

            Camera.onPreCull += OnCameraPreCull;
        }

        void OnDisable()
        {
            argumentBuffer?.Dispose();
            dataBuffer?.Dispose();

            Camera.onPreCull -= OnCameraPreCull;
        }

        void OnDestroy()
        {
            if(chunkData != null)
            {
                chunkData.Dispose();
            }
        }

        private int m_MaxChunkCount = 10;
        private int m_ExistingCount = 0;
        private uint m_RenderCount = 0;

        private void ResizeLocalData()
        {
            int oldChunkDataSize = chunkData.Length;

            //Allocate new space
            NativeArray<byte> newNativeData = new NativeArray<byte>(m_MaxChunkCount * m_StrideSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if(chunkData != null)
            {
                //Copy existing data to new allocated space and dispose of old
                NativeSlice<byte> slice = newNativeData.Slice<byte>(0, chunkData.Length);
                slice.CopyFrom(chunkData);
                chunkData.Dispose();
            }
            chunkData = newNativeData;

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
            dataBuffer.Dispose();
            dataBuffer = new ComputeBuffer(m_MaxChunkCount, m_StrideSize, ComputeBufferType.Default, ComputeBufferMode.SubUpdates);

            //Copy local data to GPU buffer
            NativeArray<byte> targetData = dataBuffer.BeginWrite<byte>(0, chunkData.Length);
            targetData.CopyFrom(chunkData);
            dataBuffer.EndWrite<byte>(chunkData.Length);

            material.SetBuffer("_ChunkData", dataBuffer);
        }

        public int randomTileCount = 1;
        System.Random r = new System.Random();
        void Update()
        {

            int width = 1000;
            int height = 1000;
            for(int i = 0; i < randomTileCount; i++)
            {
                SetCellTexture(r.Next(-(width / 2) * 8, (width / 2) * 8), r.Next(-(height / 2) * 8, (height / 2) * 8), r.Next(0, 3));
            }

            if(!Input.GetKeyDown(KeyCode.W)) return;
            for(int y = 0; y < height; y++)
            {
                for(int x = 0; x < width; x++)
                {
                    int chunkX = x - (width / 2);
                    int chunkY = y - (height / 2);

                    int targetX = (chunkX * chunkSize);
                    int targetY = (chunkY * chunkSize);

                    int xNegativeBoost = ((chunkX & int.MinValue) >> 31) & 1;
                    int yNegativeBoost = ((chunkY & int.MinValue) >> 31) & 1;

                    SetCellTexture(new Vector2Int(targetX + 1, targetY + 1), 1);
                    SetCellTexture(new Vector2Int(targetX + 1, targetY + chunkSize - 2), 1);
                    SetCellTexture(new Vector2Int(targetX + chunkSize - 2, targetY + 1), 1);
                    SetCellTexture(new Vector2Int(targetX + chunkSize - 2, targetY + chunkSize - 2), 1);

                    SetCellTexture(new Vector2Int(targetX, targetY), 1);
                    SetCellTexture(new Vector2Int(targetX, targetY + chunkSize - 1), 1);
                    SetCellTexture(new Vector2Int(targetX + chunkSize - 1, targetY), 1);
                    SetCellTexture(new Vector2Int(targetX + chunkSize - 1, targetY + chunkSize - 1), 1);

                    SetCellTexture(new Vector2Int(targetX + 1, targetY), 1);
                    SetCellTexture(new Vector2Int(targetX, targetY + 1), 1);
                }
            }
        }


        ChunkedGridArray<int?> chunkDataArray = new ChunkedGridArray<int?>();

        public void SetCellTexture(Vector2Int cellPosition, int textureIndex)
        {
            SetCellTexture(cellPosition.x, cellPosition.y, textureIndex);
        }

        public void SetCellTexture(int x, int y, int textureIndex)
        {
#if SAFE_EXECUTION
            if(textureIndex > 255)
                throw new ArgumentOutOfRangeException("Up to index 255(size of byte) is supported for texture atlas indexs. Inputted '" + textureIndex.ToString() + "'.");
#endif
            int negativityBoost = (((x & int.MinValue) >> 31) & 1);
            int chunkPositionX = ((x + negativityBoost) / chunkSize) - negativityBoost;
            negativityBoost = (((y & int.MinValue) >> 31) & 1);
            int chunkPositionY = ((y + negativityBoost) / chunkSize) - negativityBoost;

            int? chunkDataRef = chunkDataArray.GetItem(chunkPositionX, chunkPositionY);


            if(chunkDataRef == null)
            {
                int targetIndex = m_ExistingCount * m_StrideSize;

                renderBounds.Encapsulate(new Vector3(chunkPositionX * m_WorldCellSize, chunkPositionY * m_WorldCellSize, 1.0f));

                if(m_ExistingCount == m_MaxChunkCount)
                {
                    //Do memory resizes
                    m_MaxChunkCount *= 2;

                    ResizeLocalData();

                    //Do local write
                    chunkData.ReinterpretStore<Vector2>(targetIndex, new Vector2(chunkPositionX, chunkPositionY));

                    //We are doing the local write for the cell texture here since the local copy will be copied to the GPU side anyways in ResizeGPUData
                    int localCellX = FastMath.Abs(x - (chunkPositionX * chunkSize));
                    int localCellY = FastMath.Abs(y - (chunkPositionY * chunkSize));

                    int cellIndex = Utils.CoordToIndex(localCellX, localCellY, chunkSize);

                    int targetWriteIndex = targetIndex + 8 + cellIndex;

                    chunkData[targetWriteIndex] = (byte)textureIndex;

                    ResizeGPUData();
                }
                else
                {
                    int localCellX = FastMath.Abs(x - (chunkPositionX * chunkSize));
                    int localCellY = FastMath.Abs(y - (chunkPositionY * chunkSize));

                    int cellIndex = Utils.CoordToIndex(localCellX, localCellY, chunkSize);
                    
                    int targetWriteIndex = targetIndex + 8 + cellIndex;

                    Vector2 chunkPositionVector = new Vector2(chunkPositionX, chunkPositionY);

                    //Do local write
                    chunkData.ReinterpretStore(targetIndex, chunkPositionVector);
                    chunkData[targetWriteIndex] = (byte)textureIndex;

                    //Do GPU write
                    NativeArray<byte> targetData = dataBuffer.BeginWrite<byte>(targetIndex, 8);
                    targetData.ReinterpretStore<Vector2>(0, chunkPositionVector);
                    dataBuffer.EndWrite<byte>(8);

                    targetData = dataBuffer.BeginWrite<byte>(targetWriteIndex, 1);
                    targetData[0] = (byte)textureIndex;
                    dataBuffer.EndWrite<byte>(1);
                }

                m_ExistingCount++;
                chunkDataRef = targetIndex;
                chunkDataArray.SetItem(chunkPositionX, chunkPositionY, targetIndex);
            }
            else
            {
                int localCellX = FastMath.Abs(x - (chunkPositionX * chunkSize));
                int localCellY = FastMath.Abs(y - (chunkPositionY * chunkSize));

                int cellIndex = Utils.CoordToIndex(localCellX, localCellY, chunkSize);

                int targetWriteIndex = chunkDataRef.Value + 8 + cellIndex;

                //Do local write
                chunkData[targetWriteIndex] = (byte)textureIndex;

                //Do GPU write
                NativeArray<byte> targetData = dataBuffer.BeginWrite<byte>(targetWriteIndex, 1);
                targetData[0] = (byte)textureIndex;
                dataBuffer.EndWrite<byte>(1);
            }
        }

        private Bounds renderBounds = new Bounds(Vector3.zero, new Vector3(0, 0, 1));
        private uint m_LastRenderCount = 0;
        private void OnCameraPreCull(Camera camera)
        {
            //Only test culling on cameras that are on the same layer
            if((camera.cullingMask & (1 << gameObject.layer)) == 0)
            {
                return;
            }


            //TODO Cull
            //Plane p = new Plane(Vector3.zero, new Vector3(1.0f, 1.0f, 0.0f), Vector3.right);
            Plane p = new Plane(transform.position, transform.position + transform.up, transform.position + transform.right);

            Ray bottomLeftRay = camera.ViewportPointToRay(new Vector3(-0.01f, -0.01f,0.0f));
            Ray topRightRay = camera.ViewportPointToRay(new Vector3(1.01f,1.01f,0.0f));
            Debug.DrawRay(bottomLeftRay.origin, bottomLeftRay.direction, Color.yellow, 0.1f, false);
            Debug.DrawRay(topRightRay.origin, topRightRay.direction, Color.green, 0.1f, false);


            float enter = 0.0f;
            if(p.Raycast(bottomLeftRay, out enter))
            {
                Utils.DrawPoint(bottomLeftRay.GetPoint(enter), 1.0f, Color.blue, 0.1f);
            }
            if(p.Raycast(topRightRay, out enter))
            {
                Utils.DrawPoint(topRightRay.GetPoint(enter), 1.0f, Color.blue, 0.1f);
            }


            if(m_LastRenderCount != m_RenderCount)
            {
                m_LastRenderCount = m_RenderCount;
                NativeArray<uint> argumentSetter = argumentBuffer.BeginWrite<uint>(1, 1);
                argumentSetter[0] = m_LastRenderCount;
                argumentBuffer.EndWrite<uint>(1);
            }

            UnityEngine.Graphics.DrawMeshInstancedIndirect(tileMesh, 0, material, renderBounds, argumentBuffer, 0, null, ShadowCastingMode.Off, false, gameObject.layer, camera, LightProbeUsage.Off);
        }
    }
}