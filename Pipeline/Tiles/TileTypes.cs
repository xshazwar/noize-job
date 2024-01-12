using System;

using UnityEngine;

using static Unity.Mathematics.math;
using xshazwar.noize.mesh;

namespace xshazwar.noize.tile {
    using Unity.Mathematics;

    [Serializable]
    public struct TileRequest: IEquatable<TileRequest>{
        public string uuid;
        public Vector2Int pos;

        public bool Equals(TileRequest other){
            return uuid.Equals(other.uuid);
        }
    }

    public struct TileSetMeta : IEquatable<TileSetMeta>{
        public int2 TILE_RES;
        public int2 TILE_SIZE;
        public int2 GENERATOR_RES;
        public float2 PATCH_RES;
        public int HEIGHT;
        public float HEIGHT_F;
        public int MARGIN;

        public bool Equals(TileSetMeta other){
            return false;
        }
    }

}