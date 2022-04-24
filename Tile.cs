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
        public Vector2Int position { get; private set; }
        //The tilemap that this tile belongs to
        public Tilemap tilemap { get; private set; }

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
    }
}