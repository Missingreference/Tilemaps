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
    public class Tilemap : TextureGrid
    {
        private ChunkedGridArray<Tile> m_Tiles = new ChunkedGridArray<Tile>(16, 8, 16);


        void Awake()
        {

        }

        public T SetTile<T>(int x, int y) where T : Tile
        {
            return (T)SetTile(x, y, typeof(T));
        }

        public T SetTile<T>(Vector2Int cellPosition) where T : Tile
        {
            return (T)SetTile(cellPosition.x, cellPosition.y, typeof(T));
        }

        public Tile SetTile(int x, int y, Type tileType)
        {
            if(tileType == null)
            {
                Tile existingTile = m_Tiles.GetItem(x, y);
                if(existingTile != null)
                {
                    m_Tiles.SetItem(x, y, null);
                    existingTile.Destroy();

                    ClearCellTexture(x, y);
                }

                return null;
            }
            else
            {
#if SAFE_EXECUTION
                if(!tileType.IsSubclassOf(typeof(Tile)))
                    throw new ArgumentException("Inputted tile type must derive from Tile class.", nameof(tileType));
                if(tileType.IsAbstract)
                    throw new ArgumentException("Inputted tile type must not be abstract.", nameof(tileType));
#endif
                Tile existingTile = m_Tiles.GetItem(x, y);

                //Destroy old tile if one exists
                if(existingTile != null)
                {
                    existingTile.Destroy();
                }

                //Set new tile
                Tile tile = (Tile)Activator.CreateInstance(tileType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { this, x, y }, null);
                return tile;
            }
        }

        public Tile SetTile(Vector2Int cellPosition, Type tileType)
        {
            return SetTile(cellPosition.x, cellPosition.y, tileType);
        }

        public Tile GetTile(Vector2Int cellPosition)
        {
            return m_Tiles.GetItem(cellPosition.x, cellPosition.y);
        }
    }
}