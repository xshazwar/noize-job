using System;
using System.Collections.Generic;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.Assertions;
#endif

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    public struct PlantType {
        public byte typeIdx;
        public float densityModifier;
        public float maxAngle; // max normal.y 
        public float spawnRange;
        public float maxDensity;
        public float maxPoolSurvival;
        public float maxStreamSurvival;
        public int maxSpawnAttempts;

        public Plant? Root(ref WorldTile tile){
            int2 pos = tile.RandomPos();

            Plant p = new Plant {
                typeIdx = this.typeIdx,
                growth = (byte) 20,
                xOff = (byte)((pos.x / tile.ep.TILE_RES.x) * 256),
                zOff = (byte)((pos.y / tile.ep.TILE_RES.y) * 256),
                height = 0f,
                idx = 0,
                dead = false
            };
            int2 probe;
            for(int n = 0; n < maxSpawnAttempts; n++){
                probe = tile.RandomPos();
                if(CanSurvive(probe, ref tile)){
                    p.idx = tile.getIdx(probe);
                    p.height = tile.height[p.idx];
                    return p;
                }
            }
            return null;
        }

        public static void Grow(ref Plant p, ref WorldTile tile){

        }

        public bool CanSurvive(int2 pos, ref WorldTile tile){
            return CanSurvive(tile.getIdx(pos), ref tile);
        }

        public bool CanSurvive(int idx, ref WorldTile tile){
            float3 norm = tile.Normal(tile.getPos(idx));
            if (tile.plants[idx] > maxDensity) return false;
            if (tile.pool[idx] > maxPoolSurvival) return false;
            if (tile.flow[idx] > maxStreamSurvival) return false;
            if (norm.y > maxAngle) return false;
            return true;
        }


    }

    public struct Plant {
        public byte typeIdx;     // array index of the type definition
        public byte growth;      // of 100
        public byte xOff;        // positive X offset from idx point
        public byte zOff;        // positive X offset from idx point
        public float height;     // cache height for change detection / prop
        public int idx;
        public bool dead;
    }
}