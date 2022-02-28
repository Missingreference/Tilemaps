using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Elanetic.Tools;

namespace Elanetic.Tilemaps
{
    public abstract class Tile
    {
        //The position of the tile on the tilemap. No two tiles share the same position
        public Vector2Int position { get; internal set; }
        //The tilemap that this tile belongs to
        public Tilemap tilemap { get; internal set; }

        public int chunkIndex { get; private set; }
        public int tileIndex { get; private set; }

        public int tilemapTextureIndex { get; private set; } = -1;

        public RenderType renderType { get; set; }

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
        /// Set the tile texture. Uses the index of the Tilemap's internal texture atlas. Setting to a negative number means a blank texture.
        /// Make sure to call Tilemap.AddTexture to fill and add.
        /// </summary>
        public void SetTexture(int tilemapTextureIndex)
        {
            this.tilemapTextureIndex = tilemapTextureIndex;
            tilemap.SetTileTexture(position.x, position.y, tilemapTextureIndex);
        }

        //Individual means this tile is place as it's own GameObject and SpriteRenderer. Less peformance but allows for different rendering possibilities such as individual sorting
        //Chunk means this tile is rendered upon a texture shared with other tiles that have their render type set to chunk improving performance
        public enum RenderType
        {
            Individual,
            Chunk
        }

        //None: No collision or handled elsewhere
        //Tile: Collision encapsulates the whole tile. Is added to custom Tilemap Collider when possible for maximum performance
        //Custom: Collision is of a custom shape
        public enum CollisionType
        {
            None,
            Tile,
            Custom
        }
    }
}