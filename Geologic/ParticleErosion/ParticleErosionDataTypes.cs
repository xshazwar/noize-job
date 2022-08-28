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

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    struct LazyFloatComparer: IComparer<float> {
        public int Compare(float a, float b){
            float diff = b - a;
            if (abs(diff) < .00000000001){
                return 0;
            }
            if (diff > 0){
                return -1;
            }
            return 1;
        }
    }

/*
// 
//   POOLS
// 
*/
    public struct PoolUpdate:  IComparable<PoolUpdate>, IEquatable<PoolUpdate>{
        public int minimaIdx;
        public float volume;

        public int CompareTo(PoolUpdate obj){
            if (obj.Equals(this)){ return 0;}
            return GetHashCode() > obj.GetHashCode() ? 1 : -1;
        }

        public override int GetHashCode(){
            return minimaIdx.GetHashCode() ^ volume.GetHashCode();
        }

        public bool Equals(PoolUpdate other){
            if (minimaIdx != other.minimaIdx){
                return false;
            }
            return true;
        }
    }

    public struct PoolKey : IEquatable<PoolKey>, IComparable<PoolKey> {
        // This can be used to reference a minima or a drain. Anything that is ambigous between pool orders
        public int idx;
        // A minima can host multiple successive pools of different characteristics, so we allow an order here. Zero is the smallest pool
        public byte order;
        public byte n;
    
        public bool Equals(PoolKey other){
            if (idx != other.idx){
                return false;
            }
            return (order == other.order && n == other.n);
        }

        public int CompareTo(PoolKey obj){
            if (obj.Equals(this)){ return 0;}
            return GetHashCode() < obj.GetHashCode() ? 1 : -1;
        }
     
        public override int GetHashCode(){
            return idx.GetHashCode() ^ (order.GetHashCode() + n.GetHashCode());
            // return idx.GetHashCode() + (order.GetHashCode() * n.GetHashCode());
        }

        public PoolKey Clone(){
            return new PoolKey {
                idx = idx,
                order = order,
                n = n
            };
        }

        public bool Exists(){
            return this.idx > -1;
        }
    
    }

    public struct Pool : IEquatable<Pool>, IComparable<Pool> {

        public int indexMinima;
        public float minimaHeight;
        public int indexDrain;
        public float drainHeight;
        public byte order;  // pool order as defined by it's drain

    // Properties for Filling

        // a pool can only have 3 peers in our grid layout 
        // in the worst case of a confluence at a drain and cardinal neighbors
        // distinct from one another by diagonals
 
        public PoolKey peer0;
        public PoolKey peer1;
        public PoolKey peer2;
        
        // In cases of a pool with peers. When all are full a new pool is created. This is the reference
        public PoolKey supercededBy;

        public int memberCount; // total verts
        public float capacity;  // in normalized height units (a single vert can occupy a volume of 0 -> 1)
        public float volume;    // in normalized height units
        public float minVolume; // minimum volume, under this threshold constituent pools are not yet filled

        // Beta1 for regression
        public float b1;
        // Beta2 for regression
        public float b2;

        public void Init(int indexMinima_, float minimaHeight_, int indexDrain_, float drainHeight_, byte order_){
            indexMinima = indexMinima_;
            minimaHeight = minimaHeight_;
            indexDrain = indexDrain_;
            drainHeight = drainHeight_;
            order = order_;
            volume = 0f;
            minVolume = 0f;
            peer0 = new PoolKey {idx = -1}; // idx : -1 === DNE
            peer1 = new PoolKey {idx = -1};
            peer2 = new PoolKey {idx = -1};
            supercededBy = new PoolKey {idx = -1};
        }

        public bool Exists(){
            return indexMinima > -1;
        }
        
        public void EstimateHeight(float cellHeight, out float waterHeight){
            // wh == surfaceHeightAtMinima(volume) - cellHeight 
            waterHeight = (b1 + (b2 * log(math.max(volume + 1f, 1f)))) - cellHeight;
        }

        public void SetMinimumVolume(float confluenceHeight){
            // for higher order pools, a confluence must be met
            // before the pool will fill
            // confluenceHeight = (b1 + (b2 * log(math.max(volume + 1f, 1f)))) - minimaHeight;
            minVolume = exp((confluenceHeight - minimaHeight) / b2) - 1f;
            // float checksum = (b1 + (b2 * log(math.max(minVolume + 1f, 1f)))) - confluenceHeight;
            // Debug.LogWarning($"min volume {minVolume} / {capacity} ->> {checksum}");

        }
        
        public void SolvePool(NativeArray<float> heights){
            memberCount = heights.Length;
            capacity = drainHeight - minimaHeight; // just to start;
            heights.Sort<float, LazyFloatComparer>(new LazyFloatComparer());
            for (int i = 1 ; i < heights.Length; i++){
                capacity += (drainHeight - heights[i]);
            }
            b1 = minimaHeight;
            b2 = (drainHeight - minimaHeight) / log(capacity + 1f);
        }

        public bool HasParent(){
            return this.supercededBy.Exists();
        }

        public bool HasPeer(PoolKey peer){
            return peer.Equals(peer0) || peer.Equals(peer1) || peer.Equals(peer2);
        }

        public void AddPeer(int peerIdx, PoolKey key){
            if(peerIdx == 0) peer0 = key;
            else if(peerIdx == 1) peer1 = key;
            else if(peerIdx == 2) peer2 = key;
        }

        public PoolKey GetPeer(int peerIdx){
            if(peerIdx == 0) return peer0;
            else if(peerIdx == 1) return peer1;
            else if(peerIdx == 2) return peer2;
            throw new ArgumentException("only three valid peers");
        }

        public int PeerCount(){
            if(!peer0.Exists()) return 0;
            if(!peer1.Exists()) return 1;
            if(!peer2.Exists()) return 2;
            return 3;
        }

        // equals / compares

        public bool Equals(Pool other){
            if (indexMinima != other.indexMinima){
                return false;
            }
            return (order == other.order && indexDrain == other.indexDrain);
        }

        public int CompareTo(Pool obj){
            if (obj.Equals(this)){ return 0;}
            return GetHashCode() > obj.GetHashCode() ? 1 : -1;
        }
     
        public override int GetHashCode(){
            return indexMinima.GetHashCode() + (order.GetHashCode());
            // return idx.GetHashCode() + (order.GetHashCode() * n.GetHashCode());
        }
    }

    // Erosive Events

    public struct ErosiveEvent : IEquatable<ErosiveEvent> {
        public int idx; // the tile space idx affected
        public float deltaWaterTrack;
        public float deltaPoolMap;
        public float deltaSediment;

        public static implicit operator ErosiveEvent(int idx){
            return new ErosiveEvent() {
                idx = idx,
                deltaWaterTrack = 0,
                deltaPoolMap = 0,
                deltaSediment = 0
            };
        }

        public bool Equals(ErosiveEvent other){
            // TODO use lazy comparer for the floats
            if (idx != other.idx){
                return false;
            }
            return (deltaWaterTrack == other.deltaWaterTrack && deltaPoolMap == other.deltaPoolMap && deltaSediment == other.deltaSediment);
        }
    }

    public struct Particle : IEquatable<Particle> {
        
        // Parameters
        public static readonly float density = 1.0f;  //This gives varying amounts of inertia and stuff...
        public static readonly float evapRate = 0.001f;
        public static readonly float depositionRate = 1.2f*0.08f;
        public static readonly float minVol = 0.01f;
        public static readonly float friction = 0.25f;
        public static readonly float volumeFactor = 0.5f;
        private static readonly bool2 TRUE2 = new bool2(true, true);
        
        // Fields
        public int2 pos;
        public float2 speed;
        public float volume; // = 1f;
        public float sediment; // = 0f;
        public bool isDead;

        public bool Equals(Particle other){
            // TODO use lazy comparer for the floats
            if (!(pos == other.pos).Equals(TRUE2)){
                return false;
            }
            if (!(speed == other.speed).Equals(TRUE2)){
                return false;
            }
            return (volume == other.volume && sediment == other.sediment);
        }
        
        public void Reset(int2 pos){
            this.pos = pos;
            this.speed = new float2(0f, 0f);
            this.volume = 1f;
            this.sediment = 0;
            isDead = false;
            // Debug.Log($"particle reset to {pos.x}, {pos.y}");
        }

        public void SetPosition(int x, int y){
            pos.x = x;
            pos.y = y;
        }

        public int2 GetPosition(){
            return pos;
        }

        public bool DescentComplete(ref WorldTile tile, out ErosiveEvent evt){
            int idx = tile.getIdx(pos);
            evt = idx;
            if (volume < minVol) return true;
            evt.deltaWaterTrack = volume;
            float3 norm = tile.Normal(pos);
            float2 horiz = new float2(norm.x, norm.z);
            float effF = friction*(1f - tile.flow[idx]);
            if(length(horiz *effF) < 1E-5){
                // Should this write to the pool map? Not sure how it wouldn't
                evt.deltaPoolMap = volume;
                return true;
            }
            speed = lerp(horiz, speed, effF);
            speed = sqrt(2.0f) * normalize(speed);
            pos.x += (int) speed.x;
            pos.y += (int) speed.y;
            if(tile.OutOfBounds(pos)){
                // TODO transfer to other tile queue
                volume = 0f;
                return true;
            }
            int nextIdx = tile.getIdx(pos);
            if(tile.StandingWater(pos)){
                evt.idx = nextIdx;
                evt.deltaPoolMap = volume;
                return true;
            };
            //Mass-Transfer (in MASS)
            // effD(plantDensity[pos]) -> local erosion strength (based on plant density) ***Can ignore? / Punt?***
            float effD = 1f;
            float c_eq = max(0f, tile.height[idx] - tile.height[nextIdx]);
            float cdiff = c_eq - sediment;
            sediment += effD * cdiff;
            evt.deltaSediment = -effD * cdiff;

            //Evaporate (Mass Conservative)
            float effR = evapRate*(1f - 0.2f * tile.flow[idx]);
            sediment /= (1.0f - effR);
            volume *= (1.0f - effR);
            return false;
        }
    }

    public struct WorldTile {
    
        // public float SCALE;
        public int2 res;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> height;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> pool;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> flow;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> track;

        static readonly int2 left = new int2(-1, 0);
        static readonly int2 right = new int2(1, 0);
        static readonly int2 up = new int2(0, 1);
        static readonly int2 down = new int2(0, -1);

        static readonly int[] nx =  new int[] {-1,-1,-1, 0, 0, 1, 1, 1};
        static readonly int[] ny =  new int[] {-1, 0, 1,-1, 1,-1, 0, 1};

        static readonly float maxdiff = 0.01f;  // maximum diff under which no modification will be made in either direction
        static readonly float settling = 0.1f;

        public float3 Normal(int2 pos){
            // returns normal of the (WIH)
            
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

        public bool StandingWater(int2 pos){
            return pool[SafeIdx(pos)] > 0f;
        }

        public float3 diff(float x, float z, int2 pos, int2 dir){
            return new float3(x, (WIH(pos + dir) - WIH(pos)), z);
        }

        public float WIH(int idx){
            return height[idx] + pool[idx];
        }

        public float WIH(int2 pos){
            return WIH(SafeIdx(pos));
        }

        public int SafeIdx(int2 pos){
            int idx = getIdx(pos);
            if (idx >= 0){
                return idx;
            }
            // TODO interpolate a normal off the edge
            if(pos.x < 0) pos.x = 0;
            if(pos.y < 0) pos.y = 0;
            if(pos.x >= res.x) pos.x = res.x - 1;
            if(pos.y >= res.y) pos.y = res.y - 1;
            return getIdx(pos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(int x, int z){
            if(x >= res.x || z >= res.y || x < 0 || z < 0){
                return -1;
            }
            return x * res.x + z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(int2 pos){
            return getIdx(pos.x, pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 getPos(int idx){
            // int x = idx / res.x;
            // int z = idx % res.x;
            return new int2((idx / res.x), (idx % res.x));
        }

        public bool OutOfBounds(int2 pos){
            if(pos.x < 0 || pos.y < 0 || pos.x >= res.x || pos.y >= res.y) return true;
            return false;
        }

        public void UpdateFlowMapFromTrack(int x, int z){
            int i = getIdx(x, z);
            flow[i] = (0.99f * flow[i]) + (0.5f * track[i] * (1f / ( 1f + 50f * track[i])));
        }

        public void CascadeHeightMapChange(int idx){
            if(pool[idx] > 0f) return;
            int idxn = 0;
            int2 pos = getPos(idx);
            int2 n = new int2();
            float diff = 0f;
            float transfer = 0f;
            float value = 0f;
            float excess = 0f;
            for(int i = 0; i < 8; i ++){
                n.x = pos.x + nx[i];
                n.y = pos.y + ny[i];
                if (OutOfBounds(n)) continue;
                idxn = getIdx(n);
                if(pool[idxn] > 0f) continue;
                diff = height[idx] - height[idxn];
                if (diff == 0f) continue;
                excess = abs(diff) - maxdiff;
                if (excess <= 0f) continue;
                transfer = settling * excess / 2.0f;
                if (diff > 0f){
                    value = height[idx];
                    value -= transfer;
                    height[idx] = value;
                    value = height[idxn];
                    value += transfer;
                    height[idxn] = value;
                }
                else {
                    value = height[idxn];
                    value -= transfer;
                    height[idxn] = value;
                    value = height[idx];
                    value += transfer;
                    height[idx] = value;
                }
            }

        }
    }

    public static class CardinalExtension {

        // Stuck this in here to see if we could predict catchment borders using hamming weight
        // or orientation of cardinals. 

        public static readonly int[] NIBBLE_LU = new int[] {
            0, 1, 1, 2, 1, 2, 2, 3, 
            1, 2, 2, 3, 2, 3, 3, 4
        };

        public static readonly Cardinal WE = (Cardinal.E | Cardinal.W);
        public static readonly Cardinal NS = Cardinal.N | Cardinal.S;
        public static readonly Cardinal D1 = Cardinal.NE | Cardinal.SW;
        public static readonly Cardinal D2 = Cardinal.NW | Cardinal.SE;
        
        public static int HammingW(this Cardinal b){
            return NIBBLE_LU[((int) b) & 0x0F] + NIBBLE_LU[((int) b) >> 4];
        }

        public static bool StraightLine(this Cardinal b){
            return ((Cardinal)(b | WE)) == b || (((Cardinal) b | NS) == b);
        }

        public static bool Diagonal(this Cardinal b){
            return ((Cardinal)(b | D1)) == b || (((Cardinal) b | D2) == b);
        }

    }
    

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

}