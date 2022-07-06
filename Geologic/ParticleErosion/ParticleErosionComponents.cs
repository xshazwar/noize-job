using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    // [Flags]
    public enum Cardinal : byte {
        // Cardinal[i] where i < 8 === (Cardinal)( ( ((byte) 1) << i ) )
        
        // cardinals
        NW = 0b_00000001,
        SE = 0b_00000010,
        N  = 0b_00000100,
        S  = 0b_00001000,
        NE = 0b_00010000,
        SW = 0b_00100000,
        E  = 0b_01000000,
        W  = 0b_10000000,
        // convenience
        X  = 0b_00000000,
        A  = 0b_11111111
    }

    struct FlowSuperPosition {
        public static readonly int2[] neighbors = new int2[] {
            // matches Cardinal convention
            // opposite d === (d * -1)
            new int2(-1,  1),
            new int2( 1, -1),
            new int2( 0,  1),
            new int2( 0, -1),
            new int2( 1,  1),
            new int2(-1, -1),
            new int2( 1,  0),
            new int2(-1,  0),   
        };
        
        int2 res;
        NativeArray<Cardinal> flow;
        
        int getIdx(int x, int z){
            return x + (res.x * z);
        }

        void CreateSuperpositions(int z, NativeArray<float> heightMap){
            int idxN = 0;
            float height = 0f;
            for(int x = 0; x < res.x; x++){
                int idx = getIdx(x, z);
                height = heightMap[idx];
                Cardinal v = Cardinal.X;
                for(int d = 0; d < 8; d++){
                    // TODO make off the edge always show as a valid down.
                    idxN = getIdx(neighbors[d].x + x, neighbors[d].y + z);
                    if (height > heightMap[idxN]){
                        v = v | (Cardinal)( ( ((byte)1) << d ) );
                    }
                }
                flow[idx] = v;
            }
        }

        bool CollectNeighbors(int2 pos, ref NativeParallelHashMap<int, Cardinal> neighborhood){
            bool foundNeighbor = false;
            int2 nKey;
            int idxN;
            int reciprocal = 1;
            int inverseIdx = 0;
            for(int d = 0; d < 8; d++){
                nKey = neighbors[d] + pos;
                idxN = getIdx(nKey.x, nKey.y);
                // TODO validate if off edge?
                if (!neighborhood.ContainsKey(idxN)){
                    inverseIdx = d + reciprocal;
                    Cardinal mask = flow[idxN];
                    mask = PruneMask(inverseIdx, mask, ref foundNeighbor);
                    neighborhood.Add(idxN, mask);
                }
                reciprocal *= -1;
            }
            return foundNeighbor;
        }

        bool PruneNeighbors(int2 pos, ref NativeParallelHashMap<int, Cardinal> neighborhood){
            bool pruned = false;
            int2 nKey;
            int idxN;
            int reciprocal = 1;
            int inverseIdx = 0;
            for(int d = 0; d < 8; d++){
                nKey = neighbors[d] + pos;
                idxN = getIdx(nKey.x, nKey.y);
                if (neighborhood.ContainsKey(idxN)){
                    inverseIdx = d + reciprocal;
                    // h -> n = neighbors[n] || Cardinal[n]
                    // n -> h = neighbors[n + reciprocal] || Cardinal[n + reciprocal]
                    // remove self from neighbors mask, indicate pruned

                    neighborhood[idxN] = PruneMask(inverseIdx, neighborhood[idxN], ref pruned);  
                }
                // the opposite direction flops between ahead and behind the current neighbor direction
                // [d == 0] NW ( + 1) === SE
                // [d == 1] SE ( - 1) === NW
                reciprocal *= -1;
            }
            return pruned;
        }

        Cardinal PruneMask(int directionIdx, Cardinal mask, ref bool changed){
            if ((mask & (Cardinal) ( (byte) 1 << directionIdx)) != 0){
                mask &= ~ (Cardinal) ( (byte) 1 << directionIdx);
                changed = true;
            }
            return mask;
        }

        void CollapseMinima(int2 pos){


        }


    }
    
    // struct PoolPosition {
    //     float waterHeight; // current height of pool here
    //     ushort floodStart; // level at which flow stops and flooding starts
    //     ushort floodEnd;  // level at which cascading starts
    //     uint minimaIndex; // where to find the minima this is linked to
    // }

    // struct Pool {
    //     public uint indexMinima;
    //     public uint indexDrain;
    //     public float capacity;
    //     public float volume;
    // }

    // struct PoolMap {
        
    //     static readonly float drainTolerance = .001f;
    //     int2 res;
    //     NativeArray<float> heightMap;
    //     NativeArray<PoolPosition> pool;
    //     NativeList<int> minima;

    //     public static readonly int2[] neighbors = new int2[] {
    //         // matches Cardinal convention
    //         // opposite d === (d * -1)
    //         new int2(-1,  1),
    //         new int2( 1, -1),
    //         new int2( 0,  1),
    //         new int2( 0, -1),
    //         new int2( 1,  1),
    //         new int2(-1, -1),
    //         new int2( 1,  0),
    //         new int2(-1,  0),   
    //     };

    //     int getIdx(int x, int z){
    //         if(x < 0 || z < 0 || x >= res.x || z >= res.y){
    //             return -1;
    //         }
    //         return x + (res.x * z);
    //     }
        
    //     // parallel over zRes 
    //     void FindMinima(int z){
    //         for (int x = 0; x < res.x; x++){
    //             int idx = getIdx(x, z);
    //             int drainCount = 0;
    //             float height = heightMap[idx];
    //             int idxN = 0;
    //             for(int nC = 0; nC < neighbors.Length; nC ++) {
    //                 idxN = getIdx(x + neighbors[nC].x, z + neighbors[nC].y);
    //                 if (idxN == -1) continue;
    //                 if (height - heightMap[idxN] > drainTolerance){
    //                     drainCount += 1;
    //                 }
    //             }
    //             if (drainCount == 0){
    //                 minima.Add(idx);
    //             }
    //         }
    //     }
    //     void SolvePool(Pool pool){
    //         float height = heightMap[(int) pool.indexMinima];
    //     }

    //     void AddNeighborsToPool(Pool pool, int idx, float level){

    //     }


    // }


    struct WorldTile {
    
        float SCALE;
        int2 res;
        
        NativeArray<float> height;
        NativeArray<float> flow;
        NativeArray<float> pool;
        NativeArray<float> track;

        public float3 Normal(int2 pos){
            // returns normal of the (WIH)
            
            int2 left = new int2(-1, 0);
            int2 right = new int2(1, 0);
            int2 up = new int2(0, 1);
            int2 down = new int2(0, -1);

            float3 n = cross(
                diff(0, 1, pos, right),
                diff(1, 0, pos, up)
            );
            n += cross(
                diff(0, -1, pos, left),
                diff(-1, 0, pos, down)
            );

            n += cross(
                diff(1, 0, pos, up),
                diff(0, -1, pos, left)
            );

            n += cross(
                diff(-1, 0, pos, up),
                diff(0, 1, pos, right)
            );
            return normalize(n);
        }

        public float3 diff(float x, float z, int2 pos, int2 dir){
            return new float3(x, (WIH(pos + dir) - WIH(pos)), z);
        }

        public float WIH(int idx){
            return height[idx] + pool[idx];
        }

        public float WIH(int2 pos){
            return WIH(getIdx(pos));
        }

        public int getIdx(int2 pos){
            return pos.x + (res.x * pos.y);
        }
    }
    
    public struct Particle: IParticle {
        
        // Parameters
        const float density = 1.0f;  //This gives varying amounts of inertia and stuff...
        const float evapRate = 0.001f;
        const float depositionRate = 1.2f*0.08f;
        const float minVol = 0.01f;
        const float friction = 0.25f;
        const float volumeFactor = 0.5f;
        
        // Fields

        int index;
        int2 pos;
        float2 speed;
        float volume; // = 1f;
        float sediment; // = 0f;
        
        public void Reset<P>(int2 maxPos, P prototype) where P : struct, IParticle{

        }

        public void SetPosition(int x, int y){
            pos.x = x;
            pos.y = y;
        }

        public int2 GetPosition(){
            return pos;
        }

        public void Consume<P>(P part) where P : struct, IParticle{

        }

        public void Effect<RW>(RW tile)
            where RW: struct, IRWTile {

        }
    }

    public struct ParticleMergeStep : IParticleManager {
        // Merges superimposed particles from a sorted list
        public void Execute<P>(NativeSlice<P> particles) where P : struct, IParticle{
            int current = -1;
            int2 dead = new int2(-1, -1);
            bool2 same;
            for (int i = 0; i < particles.Length; i++){
                same = (particles[i].GetPosition() == dead);
                if (same.x && same.y){
                    continue;
                }
                if (current < 0){
                    current = i;
                    continue;
                }
                same = (particles[current].GetPosition() == particles[i].GetPosition());
                if (same.x && same.y){
                    particles[current].Consume<P>(particles[i]);
                    particles[i].SetPosition(dead.x, dead.y);
                }else{
                    current = i;
                }
            }
        }
    }

    public struct ParticleSortStep : IParticleManager {
        // Merges superimposed particles from a sorted list
        public void Execute<P>(NativeSlice<P> particles) where P : struct, IParticle{
            // use the sort extension
        }
    }

    public struct ParticleErosionStep: IParticleErode {
        public int Resolution {get; set;}
        public int JobLength {get; set;}
        private const float TIMESTEP = 0.2f;

        void Effect<P, RW>(P particle, RW tile) where RW: struct, IRWTile {

        }

        public void Execute<P, RW>(int i, NativeSlice<P> particles, RW tile)
            where P : struct, IParticle
            where RW: struct, IRWTile {

        }

    }

}