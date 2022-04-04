using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Elanetic.Tools;

namespace Elanetic.Tilemaps
{
    /// <summary>
    /// The base tile class. Has minimal features and small allocation size as possible for performance.
    /// </summary>
    public abstract class Tile
    {
        //The position of the tile on the tilemap. No two tiles share the same position
        public Vector2Int position { get; internal set; }
        //The tilemap that this tile belongs to
        public Tilemap tilemap { get; internal set; }

        public int chunkIndex { get; private set; }
        public int tileIndex { get; private set; }

        public int tilemapTextureIndex { get; internal set; } = 0;

        public RenderType renderType
        {
            get => m_RenderType;
            set
            {
                if(m_RenderType == value) return;
                m_RenderType = value;
                if(m_RenderType == RenderType.Individual)
                {
#if SAFE_EXECUTION
                    m_IndivualRenderer = new GameObject("Individual Tile (" + position.x + ", " + position.y + ")").AddComponent<SpriteRenderer>();
#else
                    m_IndivualRenderer = new GameObject("Individual Tile").AddComponent<SpriteRenderer>();
#endif
                    m_IndivualRenderer.transform.SetParent(tilemap.transform);
                }
                else if(m_RenderType == RenderType.Group)
                {

                }
                else //RenderType.Chunk
                {
                    //TODO Destroy Individual/Group mesh parts
                    tilemap.SetCellTexture(position, tilemapTextureIndex);
                }
            }
        }

        private RenderType m_RenderType = RenderType.Chunk;
        private SpriteRenderer m_IndivualRenderer;

        protected Tile(Tilemap tilemap, int positionX, int positionY)
        {
            this.tilemap = tilemap;
            this.position = new Vector2Int(positionX, positionY);
        }

        internal void Destroy()
        {
            OnDestroyed();
        }

        //Called by the tilemap when the tilemap has been destroyed or the cell position of this tile has been replaced by another tile or set to null
        protected virtual void OnDestroyed() { }

        /// <summary>
        /// Individual: Tile is place as it's own GameObject and SpriteRenderer. Less peformance but allows for different rendering possibilities such as individual sorting
        /// Group: Try to group similar tiles when placed in lines or rectangles. Otherwise render individually.
        /// Chunk: This tile is rendered with the Texture Grid class where it is rendered within a chunk's quad mesh for maximum performance
        /// </summary>
        public enum RenderType
        {
            Individual,
            Group,
            Chunk
        }

        /// <summary>
        /// None: No collision
        /// Tile: Collision encapsulates the whole tile. Is added to custom Tilemap Collider when possible for maximum performance
        /// Custom: Collision is of a custom shape
        /// </summary>
        public enum CollisionType
        {
            None,
            Tile,
            Custom
        }
    }
}