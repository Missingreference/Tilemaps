using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;

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
        static private Dictionary<Type, ConstructorDelegate> tileConstructors;

        private ChunkedGridArray<Tile> m_Tiles = new ChunkedGridArray<Tile>(16, 8);

        private const BindingFlags TILE_CREATION_BINDING_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public delegate object ConstructorDelegate(params object[] args);

        protected override void Awake()
        {
            base.Awake();

            if(tileConstructors == null)
            {
                //A solution for more performant tile instantation. Since were using generics in SetTile there is no easy way to create a new Tile. Creating a method at runtime to create these Tiles is as performant as we can get under the circumstances.
                //10x Faster than Activator.CreateInstance from performance tests. Instead since we created methods to instantiate a tile for every Tile type, we do a quick dictionary lookup of the target type of tile and call that constructor.
                //UPDATE: It appears only newer versions of Unity (~2021+) support Expressions where constructors take value-type parameters.
                //Unfortunately from Unity employees claim that general runtime performance is slightly reduced throughout game execution if this feature is enabled rather than just the execution of the expression which in my opinion does not make it worth it.
                //So for now we will use generic boxing version of Expression which seems to work well enough.
                tileConstructors = new Dictionary<Type, ConstructorDelegate>(255);
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                Type[] parameterArray = new Type[] { typeof(Tilemap), typeof(int), typeof(int) };
                for(int i = 0; i < assemblies.Length; i++)
                {
                    Type[] types = assemblies[i].GetTypes();
                    for(int h = 0; h < types.Length; h++)
                    {
                        Type targetType = types[h];
                        if(targetType.IsSubclassOf(typeof(Tile)) && !targetType.IsAbstract)
                        {
                            //The generic boxed version of constructor create. Technically slower than the value type version but defintely faster than Activator.CreateInstance.
                            ConstructorInfo constructorInfo = targetType.GetConstructor(TILE_CREATION_BINDING_FLAGS, null, parameterArray, null);

                            ParameterExpression paramExpr = Expression.Parameter(typeof(System.Object[]));

                            UnaryExpression paramU1 = Expression.Convert(Expression.ArrayAccess(paramExpr, Expression.Constant(0)), parameterArray[0]);
                            UnaryExpression paramU2 = Expression.Convert(Expression.ArrayAccess(paramExpr, Expression.Constant(1)), parameterArray[1]);
                            UnaryExpression paramU3 = Expression.Convert(Expression.ArrayAccess(paramExpr, Expression.Constant(2)), parameterArray[2]);

                            NewExpression body = Expression.New(constructorInfo, paramU1, paramU2, paramU3);

                            Expression<ConstructorDelegate> constructor = Expression.Lambda<ConstructorDelegate>(body, paramExpr);
                            ConstructorDelegate construct = constructor.Compile();
                            tileConstructors.Add(targetType, construct);

                            /* The value type version of constructor creator. Only supported in newer specific Unity versions and specific settings enabled that reduce general performance
                            ConstructorInfo constructorInfo = targetType.GetConstructor(TILE_CREATION_BINDING_FLAGS, null, parameterArray, null);
                            Debug.Log(targetType.ToString() + " | "  + constructorInfo);
                            ParameterExpression parameterExpression1 = Expression.Parameter(typeof(Tilemap), "tilemap");
                            ParameterExpression parameterExpression2 = Expression.Parameter(typeof(System.Object), "cellPositionX");
                            ParameterExpression parameterExpression3 = Expression.Parameter(typeof(System.Object), "cellPositionY");

                            NewExpression body = Expression.New(constructorInfo, parameterExpression1, parameterExpression2, parameterExpression3);

                            LambdaExpression constructor = Expression.Lambda<Func<Tilemap, System.Object, System.Object, Tile>>(body, parameterExpression1, parameterExpression2, parameterExpression3);

                            tileConstructors.Add(targetType, (Func<Tilemap, System.Object, System.Object, Tile>)constructor.Compile(true));
                            */
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Set the tile at the specified position. Uses reflection to instantiate the class. Can pass null as the tile type to clear the position.
        /// </summary>
        public Tile SetTile(int x, int y, Type tileType)
        {
            if(tileType == null)
            {
                ClearTile(x, y);

                return null;
            }
            else
            {
#if SAFE_EXECUTION
                if(!tileType.IsSubclassOf(typeof(Tile)))
                    throw new ArgumentException("Inputted tile type must derive from Tile class. Inputted: '" + tileType.ToString() + "'.", nameof(tileType));
                if(tileType.IsAbstract)
                    throw new ArgumentException("Inputted tile type must not be abstract. Inputted: '" + tileType.ToString() + "'.", nameof(tileType));
#endif
                Tile existingTile = m_Tiles.GetItem(x, y);

                //Destroy old tile if one exists
                if(existingTile != null)
                {
                    existingTile.Destroy();
                }

                //Set new tile using Reflection
                Tile tile = (Tile)tileConstructors[tileType](this, x, y);
                m_Tiles.SetItem(x, y, tile);
                return tile;
            }
        }

        /// <summary>
        /// Set the tile at the specified position. Uses reflection to instantiate the class. Can pass null as the tile type to clear the position.
        /// </summary>
        public Tile SetTile(Vector2Int cellPosition, Type tileType)
        {
            return SetTile(cellPosition.x, cellPosition.y, tileType);
        }

        /// <summary>
        /// Set the tile at the specified position.
        /// </summary>
        public T SetTile<T>(int x, int y) where T : Tile
        {
#if SAFE_EXECUTION
            if(typeof(T).IsAbstract)
                throw new ArgumentException("Inputted tile type must not be abstract.", nameof(T));
#endif

            return (T)SetTile(x, y, typeof(T));
        }

        /// <summary>
        /// Set the tile at the specified position.
        /// </summary>
        public T SetTile<T>(Vector2Int cellPosition) where T : Tile
        {
            return SetTile<T>(cellPosition.x, cellPosition.y);
        }

        /// <summary>
        /// Destroy the tile at the specified position. Also clears the texture at the texture position.
        /// </summary>
        public void ClearTile(int x, int y)
        {
            Tile existingTile = m_Tiles.GetItem(x, y);
            if(existingTile != null)
            {
                m_Tiles.SetItem(x, y, null);
                existingTile.Destroy();

                ClearCellTexture(x, y);
            }
        }

        /// <summary>
        /// Destroy the tile at the specified position. Also clears the texture at the texture position.
        /// </summary>
        public void ClearTile(Vector2Int cellPosition)
        {
            ClearTile(cellPosition.x, cellPosition.y);
        }

        /// <summary>
        /// Retrieve the tile of any type at the specified position.
        /// </summary>
        public Tile GetTile(int x, int y)
        {
            return m_Tiles.GetItem(x, y);
        }

        /// <summary>
        /// Retrieve the tile of any type at the specified position.
        /// </summary>
        public Tile GetTile(Vector2Int cellPosition)
        {
            return GetTile(cellPosition.x, cellPosition.y);
        }

        /// <summary>
        /// Retrieve the tile at the specified position. Returns null if the existing tile does not match the generic input.
        /// </summary>
        public T GetTile<T>(int x, int y) where T : Tile
        {
            return GetTile(x, y) as T;
        }

        /// <summary>
        /// Retrieve the tile at the specified position. Returns null if the existing tile does not match the generic input.
        /// </summary>
        public T GetTile<T>(Vector2Int cellPosition) where T : Tile
        {
            return GetTile(cellPosition.x, cellPosition.y) as T;
        }
    }
}