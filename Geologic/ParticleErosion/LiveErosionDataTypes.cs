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

using xshazwar.noize.tile;
using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    public enum ErosionMode {
        ALL_EROSION,
        ONLY_THERMAL_EROSION,
        THERMAL_FLOW_WATER,
        ONLY_FLOW_WATER
    }

    // Erosive Events
    public struct ErosiveEvent : IEquatable<ErosiveEvent> {
        public int idx; // the tile space idx affected
        public ushort actor;
        public ushort age;
        // public Heading dir; // the current direction of movement
        public float deltaWaterTrack;
        public float deltaPoolMap;
        public float deltaSediment;

        public static implicit operator ErosiveEvent(int idx){
            return new ErosiveEvent() {
                idx = idx,
                actor = 0,
                age = 0,
                // dir = Heading.NONE,
                deltaWaterTrack = 0,
                deltaPoolMap = 0,
                deltaSediment = 0
            };
        }

        public bool Equals(ErosiveEvent other){
            // TODO use lazy comparer for the floats
            if(idx != other.idx) return false;
            if(actor != actor) return false;
            if(age != age) return false;
            return (deltaWaterTrack == other.deltaWaterTrack && deltaPoolMap == other.deltaPoolMap && deltaSediment == other.deltaSediment);
        }
    }

    public class SortErosiveEventsAgeHelper : IComparer<ErosiveEvent>{
        public int Compare(ErosiveEvent a, ErosiveEvent b){
            if(a.actor == b.actor){
                if (a.age == b.age) return 0;
                return a.age > b.age ? 1 : -1;
            }
            return a.actor > b.actor ? 1 : -1;
        }
    }

    public struct ErosionParameters{
        public float INERTIA;
        public float GRAVITY;
        public float DRAG;
        public float FRICTION;
        public float EVAP;
        public float EROSION;
        public float DEPOSITION;
        public float FLOW_HEIGHT_CONTRIBUTION;

        public float SLOW_CULL_ANGLE;
        public float SLOW_CULL_SPEED;
        public float CAPACITY;
        public int MAXAGE;
        public float TERMINAL_VELOCITY;

        public float SURFACE_EVAPORATION_RATE;
        public float POOL_PLACEMENT_MULTIPLIER;
        public float TRACK_PLACEMENT_MULTIPLIER;
        public float FLOW_LOSS_RATE;

        public int PILING_RADIUS;
        public float MIN_PILE_INCREMENT;
        public float PILE_THRESHOLD;

        public static ErosionParameters Default(){
            return new ErosionParameters() {
                INERTIA = 0.7f,
                GRAVITY = 1f,
                DRAG = .001f,
                FRICTION = .001f,
                EVAP = .001f,
                EROSION = 0.2f,
                FLOW_HEIGHT_CONTRIBUTION = 25f,

                DEPOSITION = 0.05f,
                SLOW_CULL_ANGLE = 3f,
                SLOW_CULL_SPEED = .1f,
                CAPACITY = 3f,
                MAXAGE = 64,
                TERMINAL_VELOCITY = 1f / .001f, // vel = DRAG * pow(vel, 2)

                SURFACE_EVAPORATION_RATE = 0.1f,
                POOL_PLACEMENT_MULTIPLIER = 0.5f,
                TRACK_PLACEMENT_MULTIPLIER = 80f,
                FLOW_LOSS_RATE = 0.05f,

                PILING_RADIUS = 15,
                MIN_PILE_INCREMENT = 1f,
                PILE_THRESHOLD = 2f
            };
        }
    }

    public struct NeighborhoodHelper {
        // These are ints because floats weren't properly matching themselves 
        // when calling .IndexOf, returning -1
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<int> nb;
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<int> nbSort;
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<int2> nbDir;

        public static NativeArray<int2> generateLookupDir(){
            NativeArray<int2> d = new NativeArray<int2>(8, Allocator.Temp);
            d[0] = WorldTile.up;
            d[1] = WorldTile.right;
            d[2] = WorldTile.down;
            d[3] = WorldTile.left;
            d[4] = WorldTile.ne;
            d[5] = WorldTile.se;
            d[6] = WorldTile.sw;
            d[7] = WorldTile.nw;
            return d;
        }

        public void CollectNeighbors(int x, int z, ref WorldTile tile, float maxFlowHeight = 0.25f){
            // tile.CollectNeighbors(x, z, ref nb);
            tile.CollectNeighborsAllHeights(x, z, ref nb, maxFlowHeight);
            nb.CopyTo(nbSort);
            nbSort.Sort<int>();
        }

        public int2 NaturalHeading(out float height){
            int h = nbSort[0];
            int idx = nb.IndexOf<int, int>(h);
            height = (float) h / 100f;
            return nbDir[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float HeadingHeight(Heading dir){
            int idx = dir.ToWorldTileIdx();
            return ((float) nb[idx]) / 100f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float HeadingHeight(int2 dir){
            Heading h = HeadingExt.FromInt2(dir);
            return HeadingHeight(h);
        }

        public void ChooseHeading(Heading center, out int2 newDir, out float height){
            Heading left;
            Heading right;
            center.AdjacentHeadings(out left, out right);
            float3 h = new float3(
                HeadingHeight(left),
                HeadingHeight(center),
                HeadingHeight(right)
            );
            if(h.x < h.y && h.x < h.z){
                height = h.x;
                newDir = left.ToInt2();
            }else if( h.z < h.x && h.z < h.y){
                height = h.z;
                newDir = right.ToInt2();
            }else{
                height = h.y;
                newDir = center.ToInt2();
            }
        }

    }

    public struct BeyerParticle : IEquatable<BeyerParticle> {
        public ushort pid;
        public float2 pos;
        public float2 dir; // norm
        public Heading heading;
        public float vel;
        public float water;
        public float sediment;
        public bool isDead;
        public ushort age;

        public ErosionParameters ep;
        public TileSetMeta tm;

        static readonly float2 left = new float2(-1, 0);
        static readonly float2 right = new float2(1, 0);
        static readonly float2 up = new float2(0, 1);
        static readonly float2 down = new float2(0, -1);
        private static readonly float2 ZERO2 = new float2(0);
        private static readonly bool2 TRUE2 = new bool2(true);

        public BeyerParticle(ushort pid, int2 pos, ErosionParameters ep, TileSetMeta tm, bool dead){
            this.pid = pid;
            this.pos = new float2(pos);
            this.dir = new float2(0f, 0f);
            this.heading = Heading.NONE;
            this.vel = .01f;
            this.water = 1f;
            this.sediment = 0f;
            this.isDead = dead;
            this.age = 0;
            this.ep = ep;
            this.tm = tm;
        }

        public BeyerParticle(ushort pid, int2 pos, ErosionParameters ep, TileSetMeta tm, float water): this(pid, pos, ep, tm, false){
            this.water = water;
        }

        public bool Equals(BeyerParticle other){
            // TODO use lazy comparer for the floats
            if (pid != other.pid) return false;
            if (!(pos == other.pos).Equals(TRUE2)){
                return false;
            }
            if (!(vel == other.vel)){
                return false;
            }
            return (water == other.water && sediment == other.sediment);
        }

        public float UphillVelocityLoss(float vDiff, float effectiveFriction, out float loss){
            float theta = atan(vDiff / tm.PATCH_RES.x);
            float thetaD = theta * 180f / 3.14159f;
            float accelerationSign = 1f;
            float acceleration = (ep.GRAVITY * sin(theta)) + effectiveFriction;
            loss = sqrt(2 * abs(acceleration) * (vDiff / sin(theta)));
            return loss;
        }

        public float DownhillVelocityGain(float vDiff, float effectiveFriction){
            float theta = atan(vDiff / tm.PATCH_RES.x);
            float thetaD = theta * 180f / 3.14159f;
            float accelerationSign = 1f;
            float acceleration = (ep.GRAVITY * sin(theta)) - effectiveFriction;
            return sqrt(2 * abs(acceleration) * (vDiff / sin(theta)));
        }

        public bool DescendSimultaneous(ref WorldTile tile, ref NeighborhoodHelper nbh, out ErosiveEvent evt){
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{pid} ~~~~~~~~~~~ RND :{age}");
            #endif
            int2 intPos = tile.getPos(pos);
            int idx = tile.getIdx(pos);
            evt = idx;
            evt.age = age;
            evt.actor = pid;
            Heading heading = HeadingExt.FromFloat2(dir);
            // evt.dir = heading;
            if(water < .01f){
                #if NJ_DBG_PARTFLOW
                    Debug.Log($"{pid} dead from dehydration {water}: {age}");
                #endif
                isDead = true;
                evt.deltaSediment = sediment / tm.HEIGHT;
                return false;
            }
            if(age >= ep.MAXAGE){
                #if NJ_DBG_PARTFLOW
                    Debug.Log($"{pid} dead from old age {age}");
                #endif
                isDead = true;
                evt.deltaPoolMap = water / tm.HEIGHT;
                evt.deltaSediment = sediment / tm.HEIGHT;
                return false;
            }
            
            float currentHeight = tile.WIH(pos);
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{pid} CH : {currentHeight}");
            #endif
            
            nbh.CollectNeighbors(intPos.x, intPos.y, ref tile, ep.FLOW_HEIGHT_CONTRIBUTION);
            float drainHeight;
            int2 drainDir = nbh.NaturalHeading(out drainHeight);
            
            bool isNone = (heading == Heading.NONE);
            if(heading == Heading.NONE){
                heading = HeadingExt.FromInt2(drainDir);
            }
            float effectiveDrag = ep.DRAG * (1f - max(tile.flow[idx], 0f));
            float effectiveFriction =  ep.FRICTION * (1f - max(tile.flow[idx], 0f));
            
            float headingHeight;
            int2 flowDir;
            nbh.ChooseHeading(heading, out flowDir, out headingHeight);
            float hDiff = headingHeight - currentHeight;
            float velocityLoss = 0f;
            // apply drag
            vel = vel - (vel * effectiveDrag);
            if(hDiff < 0f){
                // All good >> downhill
                drainDir = flowDir;                
            }else if(UphillVelocityLoss(hDiff, effectiveFriction, out velocityLoss) <= vel){
                // Also all good, can overcome the gravity
                drainDir = flowDir;
            }else{
                velocityLoss = 0f;
                hDiff = drainHeight - currentHeight;
                if(hDiff > 0f){
                    #if NJ_DBG_PARTFLOW
                        Debug.Log($"{pid} dead no drain {age}");
                    #endif
                    isDead = true;
                    evt.deltaPoolMap = water / tm.HEIGHT;
                    evt.deltaSediment = sediment / tm.HEIGHT;
                    return false;
                }
            }
            
            heading = HeadingExt.FromInt2(drainDir);
            dir = new float2(drainDir.x, drainDir.y);
            float2 posN = pos + dir;
            #if NJ_DBG_PARTFLOW
                Debug.Log($"HERE: {pos.x}, {pos.y} next {posN.x}, {posN.y} change {drainDir.x}, {drainDir.y} a:{age}");
            #endif
            if (tile.OutOfBounds(posN)) {
                #if NJ_DBG_PARTFLOW
                    Debug.Log($"{pid} dead from oob {age} {posN.x}, {posN.y}, {tile.tm.GENERATOR_RES.x}");
                #endif
                isDead = true;
                return false;
            }
            float vDiff = abs(hDiff);
            float acceleration = 0f;
            float depositionAmount = 0f;
            float currentCapacity = 0f;
            float thetaD = 0f;
            float deltaV = 0f;
            
            if(vDiff > 0f){
                float theta = atan(vDiff / tm.PATCH_RES.x);
                thetaD = theta * 180f / 3.14159f;
                float accelerationSign = 1f;
                if(hDiff > 0f){
                    // uphill
                    #if NJ_DBG_PARTFLOW
                        Debug.Log($"{pid} uphill >> {hDiff}");
                    #endif
                    deltaV = -1f * velocityLoss;
                }else{
                    #if NJ_DBG_PARTFLOW
                        Debug.Log($"{pid} downhill  >> {hDiff}");
                    #endif
                    deltaV = DownhillVelocityGain(vDiff, effectiveFriction);
                }
                #if NJ_DBG_PARTFLOW
                    Debug.Log($"{pid} V: (({vel})) dV: {deltaV}");
                #endif
            }
            // add acceleration
            vel = max((vel + deltaV), 0f);
            // factor in terminal velocity
            vel = vel - max(
                min(
                    vel - ep.TERMINAL_VELOCITY,
                    max(
                        effectiveDrag * 0.25f * (vel - ep.TERMINAL_VELOCITY) * (vel - ep.TERMINAL_VELOCITY),
                        0f)
                    ),
                0f);
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{pid} new v : {vel}");
            #endif
            if( thetaD < 3f && vel < 1f){
                #if NJ_DBG_PARTFLOW
                    Debug.Log($"{pid} dead from slow speed {age}");
                #endif
                evt.deltaPoolMap += water / tm.HEIGHT;
                evt.deltaSediment += sediment / tm.HEIGHT;
                isDead = true;
                return false;
            }
            currentCapacity = vel * water * ep.CAPACITY;
            if (sediment < currentCapacity){
                // erode
                depositionAmount = -1f * ep.EROSION * (currentCapacity - sediment);
            }
            else{
                // deposit
                depositionAmount = ep.DEPOSITION * (sediment - currentCapacity);
            }
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{pid}:: (Sed) {sediment} / (Cap){currentCapacity} >> {depositionAmount}");
            #endif
            if(abs(depositionAmount) > 0f){
                evt.deltaSediment += depositionAmount / tm.HEIGHT;
                sediment -= depositionAmount;
            }
            #if NJ_DBG_PARTFLOW
                Debug.Log($"{pid} deposition {evt.deltaSediment}");
            #endif
            evt.deltaWaterTrack = water;
            water = water * (1 - ep.EVAP);
            pos = posN;
            age++;
            return true;
        }

    }

    public struct WorldTile {

        public TileSetMeta tm;
        // TODO move these params to ErosionParameters
        static public readonly float MINFLOWPOOL = .00005f;
        /*
            *******Data Structures******
            (populated as needed by jobs)
        */
        
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<float> height;
        
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<float> pool;
        
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<float> flow;

        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<float> track;

        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        public NativeArray<float> plants;

        private Unity.Mathematics.Random random;

        // Constants
        public static readonly int2 ZERO2 = new int2(0);

        public static readonly int2 left = new int2(-1, 0);
        public static readonly int2 right = new int2(1, 0);
        public static readonly int2 up = new int2(0, 1);
        public static readonly int2 down = new int2(0, -1);
        public static readonly int2 ne = new int2(1, 1);
        public static readonly int2 nw = new int2(-1, 1);
        public static readonly int2 sw = new int2(-1, -1);
        public static readonly int2 se = new int2(1, -1);

        static public readonly int[] normX =  new int[] {-1,-1,-1, 0, 0, 1, 1, 1};
        static public readonly int[] normY =  new int[] {-1, 0, 1,-1, 1,-1, 0, 1};

        // Random

        public int2 RandomPos(){
            return random.NextInt2(ZERO2, tm.GENERATOR_RES);
        }

        public void SetRandomSeed(int seed){
            random = new Unity.Mathematics.Random((uint) seed);
        }

        // Normal

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 Normal(float2 p, out float4 neighbor){
            int2 pos = new int2(p);
            return Normal(pos, out neighbor);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 Normal(int2 pos, out float4 neighbor){
            float h = WIH(pos);
            // float4 neighbor;
            if(UnsafeNeighborhood(pos)){
                neighbor = new float4(
                    WIH(pos + up),
                    WIH(pos + right),
                    WIH(pos + down),
                    WIH(pos + left)
                );
            }else{
                neighbor = new float4(
                    WIH_Unsafe(pos + up),
                    WIH_Unsafe(pos + right),
                    WIH_Unsafe(pos + down),
                    WIH_Unsafe(pos + left)
                );
            }
            float3 a = cross(new float3(0, h - neighbor.x, tm.PATCH_RES.y), new float3(tm.PATCH_RES.x, h - neighbor.y, 0));
            float3 b = cross(new float3(0, h - neighbor.z, -tm.PATCH_RES.y), new float3(-tm.PATCH_RES.x, h - neighbor.w, 0));
            return new float3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        // Standing Water

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool StandingWater(int x, int z){
            return pool[SafeIdx(x, z)] > 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool StandingWater(int2 pos){
            return pool[SafeIdx(pos)] > 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool StandingWater(float2 pos){
            return StandingWater(new int2(pos));
        }

        // Water Inclusive Value

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIV(int idx){
            return height[idx] + pool[idx];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float AllHeights(int idx, float maxFlowHeight = 25f){ //maxFlowHeight in WS m
            return tm.HEIGHT * (height[idx] + pool[idx]) + maxFlowHeight * (flow[idx]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(int idx){
            return tm.HEIGHT * (height[idx] + pool[idx]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(int2 pos){
            return WIH(SafeIdx(pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(float2 pos){
            return WIH(SafeIdx(pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH_Unsafe(int2 pos){
            return WIH(getIdx(pos.x, pos.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(int x, int z){
            return WIH(getIdx(x, z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(int2 pos, int dx, int dz){
            return WIH(SafeIdx(pos.x + dx, pos.y + dz));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SafeIdx(int x, int z){
            return getIdx(
                clamp(x, 0, tm.GENERATOR_RES.x - 1),
                clamp(z, 0, tm.GENERATOR_RES.y - 1));
        }        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SafeIdx(int2 pos){
            return getIdx(clamp(pos, ZERO2, tm.GENERATOR_RES - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SafeIdx(float2 pos){
            return SafeIdx(new int2(pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int2 pos){
            if(pos.x < 0 || pos.y < 0 || pos.x >= tm.GENERATOR_RES.x || pos.y >= tm.GENERATOR_RES.y) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(int x, int z){
            return x * tm.GENERATOR_RES.x + z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(int2 pos){
            return getIdx(pos.x, pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(float2 pos){
            return getIdx((int) round(pos.x), (int) round(pos.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 getPos(float2 pos){
            return new int2((int) round(pos.x), (int) round(pos.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 getPos(int idx){
            return new int2((idx / tm.GENERATOR_RES.x), (idx % tm.GENERATOR_RES.x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool UnsafeNeighborhood(int2 pos){
            return UnsafeNeighborhood(pos.x, pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool UnsafeNeighborhood(int x, int z){
            if(x < 1) return true;
            if(z < 1) return true;
            if(x >= tm.GENERATOR_RES.x - 1) return true;
            if(z >= tm.GENERATOR_RES.y - 1) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OutOfBounds(int2 pos){
            if(pos.x < 0 || pos.y < 0 || pos.x >= tm.GENERATOR_RES.x || pos.y >= tm.GENERATOR_RES.y) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OutOfBounds(float2 pos){
            return OutOfBounds(new int2(pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int4 CollectNeighbors(int x, int z){
            // up, right, down, left
            int4 nh;
            if(UnsafeNeighborhood(x, z)){
                nh = new int4(
                    SafeIdx(up.x + x, up.y + z),
                    SafeIdx(right.x + x, right.y + z),
                    SafeIdx(down.x + x, down.y + z),
                    SafeIdx(left.x + x, left.y + z)
                );
            }else{
                nh = new int4(
                    getIdx(up.x + x, up.y + z),
                    getIdx(right.x + x, right.y + z),
                    getIdx(down.x + x, down.y + z),
                    getIdx(left.x + x, left.y + z)
                );
            }
            return nh;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CollectNeighbors(int x, int z, ref NativeArray<int> n){
            // up, right, down, left, ne, se, sw, nw
            // we're converting to INT and keeping 2 sigdigs
            if(UnsafeNeighborhood(x, z)){
                n[0] = (int) (100f * WIH(SafeIdx(up.x + x, up.y + z)));
                n[1] = (int) (100f * WIH(SafeIdx(right.x + x, right.y + z)));
                n[2] = (int) (100f * WIH(SafeIdx(down.x + x, down.y + z)));
                n[3] = (int) (100f * WIH(SafeIdx(left.x + x, left.y + z)));
                n[4] = (int) (100f * WIH(SafeIdx(ne.x + x, ne.y + z)));
                n[5] = (int) (100f * WIH(SafeIdx(se.x + x, se.y + z)));
                n[6] = (int) (100f * WIH(SafeIdx(sw.x + x, sw.y + z)));
                n[7] = (int) (100f * WIH(SafeIdx(nw.x + x, nw.y + z)));
            }else{
                n[0] = (int) (100f * WIH(getIdx(up.x + x, up.y + z)));
                n[1] = (int) (100f * WIH(getIdx(right.x + x, right.y + z)));
                n[2] = (int) (100f * WIH(getIdx(down.x + x, down.y + z)));
                n[3] = (int) (100f * WIH(getIdx(left.x + x, left.y + z)));
                n[4] = (int) (100f * WIH(getIdx(ne.x + x, ne.y + z)));
                n[5] = (int) (100f * WIH(getIdx(se.x + x, se.y + z)));
                n[6] = (int) (100f * WIH(getIdx(sw.x + x, sw.y + z)));
                n[7] = (int) (100f * WIH(getIdx(nw.x + x, nw.y + z)));
            }
        }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CollectNeighborsAllHeights(int x, int z, ref NativeArray<int> n, float maxFlowHeight = 25f){
            // up, right, down, left, ne, se, sw, nw
            // we're converting to INT and keeping 2 sigdigs
            if(UnsafeNeighborhood(x, z)){
                n[0] = (int) (100f * AllHeights(SafeIdx(up.x + x, up.y + z), maxFlowHeight));
                n[1] = (int) (100f * AllHeights(SafeIdx(right.x + x, right.y + z), maxFlowHeight));
                n[2] = (int) (100f * AllHeights(SafeIdx(down.x + x, down.y + z), maxFlowHeight));
                n[3] = (int) (100f * AllHeights(SafeIdx(left.x + x, left.y + z), maxFlowHeight));
                n[4] = (int) (100f * AllHeights(SafeIdx(ne.x + x, ne.y + z), maxFlowHeight));
                n[5] = (int) (100f * AllHeights(SafeIdx(se.x + x, se.y + z), maxFlowHeight));
                n[6] = (int) (100f * AllHeights(SafeIdx(sw.x + x, sw.y + z), maxFlowHeight));
                n[7] = (int) (100f * AllHeights(SafeIdx(nw.x + x, nw.y + z), maxFlowHeight));
            }else{
                n[0] = (int) (100f * AllHeights(getIdx(up.x + x, up.y + z), maxFlowHeight));
                n[1] = (int) (100f * AllHeights(getIdx(right.x + x, right.y + z), maxFlowHeight));
                n[2] = (int) (100f * AllHeights(getIdx(down.x + x, down.y + z), maxFlowHeight));
                n[3] = (int) (100f * AllHeights(getIdx(left.x + x, left.y + z), maxFlowHeight));
                n[4] = (int) (100f * AllHeights(getIdx(ne.x + x, ne.y + z), maxFlowHeight));
                n[5] = (int) (100f * AllHeights(getIdx(se.x + x, se.y + z), maxFlowHeight));
                n[6] = (int) (100f * AllHeights(getIdx(sw.x + x, sw.y + z), maxFlowHeight));
                n[7] = (int) (100f * AllHeights(getIdx(nw.x + x, nw.y + z), maxFlowHeight));
            }
        }

        // Curviture Methods adapted from
        // https://github.com/Scrawk/Terrain-Topology-Algorithms/blob/afe65384254462073f41984c4c8e7e029275d830/Assets/TerrainTopology/Scripts/CreateTopolgy.cs
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CalculateDerivatives(int x, int z, float w, out float2 d1, out float3 d2){
            float w2 = w * w;
            float4 z1;
            float z5 = height[getIdx(x,z)];
            float4 z6;

            if(UnsafeNeighborhood(x, z)){
                z1 = new float4(
                    height[SafeIdx(nw.x + x, nw.y + z)],
                    height[SafeIdx(up.x + x, up.y + z)],
                    height[SafeIdx(ne.x + x, ne.y + z)],
                    height[SafeIdx(left.x + x, left.y + z)]
                );
                z6 = new float4(
                    height[SafeIdx(right.x + x, right.y + z)],
                    height[SafeIdx(sw.x + x, sw.y + z)],
                    height[SafeIdx(down.x + x, down.y + z)],
                    height[SafeIdx(se.x + x, se.y + z)]
                );
            }else{
                z1 = new float4(
                    height[getIdx(nw.x + x, nw.y + z)],
                    height[getIdx(up.x + x, up.y + z)],
                    height[getIdx(ne.x + x, ne.y + z)],
                    height[getIdx(left.x + x, left.y + z)]
                );
                z6 = new float4(
                    height[getIdx(right.x + x, right.y + z)],
                    height[getIdx(sw.x + x, sw.y + z)],
                    height[getIdx(down.x + x, down.y + z)],
                    height[getIdx(se.x + x, se.y + z)]
                );
            }
            z1 *= tm.HEIGHT;
            z5 *= tm.HEIGHT;
            z6 *= tm.HEIGHT;

            float zx = (z1.z + z6.x + z6.w - z1.x - z1.w - z6.y) / (6.0f * w);
            float zy = (z1.x + z1.y + z1.z - z6.y - z6.z - z6.w) / (6.0f * w);

            float zxx = (z1.x + z1.z + z1.w + z6.x + z6.y + z6.w - 2.0f * (z1.y + z5 + z6.z)) / (3.0f * w2);
            float zyy = (z1.x + z1.y + z1.z + z6.y + z6.z + z6.w - 2.0f + (z1.w + z5 + z6.x)) / (3.0f * w2);
            float zxy = (z1.z + z6.y - z1.x - z6.w) / (4.0f * w2);

            d1 = new float2(-zx, -zy);
            d2 = new float3(-zxx, -zyy, -zxy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GaussianCurvature(float zx, float zy, float zxx, float zyy, float zxy){
            float zx2 = zx * zx;
            float zy2 = zy * zy;
            float p = zx2 + zy2;

            float n = zxx * zyy - zxy * zxy;
            float d = pow(p + 1, 2f);
            if (abs(d) < 1e-18f) return 0.0f;

            return n / d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float MeanCurvature(float zx, float zy, float zxx, float zyy, float zxy)
        {
            float zx2 = zx * zx;
            float zy2 = zy * zy;
            float p = zx2 + zy2;

            float n = (1 + zy2) * zxx - 2.0f * zxy * zx * zy + (1 + zx2) * zyy;
            float d = 2 * pow(p + 1, 1.5f);
            if (abs(d) < 1e-18f) return 0.0f;
            return n / d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float VerticalCurvature(float zx, float zy, float zxx, float zyy, float zxy)
        {
            float zx2 = zx * zx;
            float zy2 = zy * zy;
            float p = zx2 + zy2;

            float n = zx2 * zxx + 2.0f * zxy * zx * zy + zy2 * zyy;
            float d = p * pow(p + 1, 1.5f);
            if (abs(d) < 1e-18f) return 0.0f;
            return n / d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float HorizontalCurvature(float zx, float zy, float zxx, float zyy, float zxy){
            float zx2 = zx * zx;
            float zy2 = zy * zy;
            float p = zx2 + zy2;

            float n = zy2 * zxx - 2.0f * zxy * zx * zy + zx2 * zyy;
            float d = p * pow(p + 1f, 0.5f);
            if (abs(d) < 1e-18f) return 0.0f;
            return n / d;
        }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public float Curviture(int2 pos, float l){
        //     float2 d1;
        //     float3 d2;
        //     CalculateDerivatives(pos.x, pos.y, l, out d1, out d2);
        //     // float v = HorizontalCurvature(d1.x, d1.y, d2.x, d2.y, d2.z);
        //     float H = MeanCurvature(d1.x, d1.y, d2.x, d2.y, d2.z);
        //     float K = GaussianCurvature(d1.x, d1.y, d2.x, d2.y, d2.z);
        //     float v = H * H - K;
        //     if(v <= 0f) return 0f;
        //     v = H + sqrt(v);
        //     // return v;
        //     return RectifyRange(v, 2f);// * 10f;

        // }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Curviture(int2 pos, float l){
            float2 d1;
            float3 d2;
            CalculateDerivatives(pos.x, pos.y, l, out d1, out d2);
            float v = HorizontalCurvature(d1.x, d1.y, d2.x, d2.y, d2.z);
            // float v = VerticalCurvature(d1.x, d1.y, d2.x, d2.y, d2.z);
            v = abs(v);
            // return v;
            // if(v >= 0f) return 0f;
            return abs(RectifyRange(v, .05f)) / 2f;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float RectifyRange(float v, float exp_){
            float sign_ = sign(v);
            float pow_ = pow(10f, exp_);
            float log_ = log(1.0f + pow_ * abs(v));
            return sign_ * log_ ;
        }

        public void UpdateFlowMapFromTrack(int x, int z, float FLOW_LOSS_RATE, float SURFACE_EVAPORATION_RATE){
            int i = getIdx(x, z);
            float pv = flow[i];
            float tv = track[i];
            float poolV = pool[i];
            if(poolV > MINFLOWPOOL){
                flow[i] = ((1f - 0.1f * FLOW_LOSS_RATE) * pv);
            }
            // track does not accumulate to flow where there's a pool. Only decays
            else if(tv > 0f){
                flow[i] = ((1f - FLOW_LOSS_RATE) * pv) + (FLOW_LOSS_RATE * 50.0f * tv) / (1f + 50.0f* tv);
            }else{
                flow[i] = (1f - FLOW_LOSS_RATE) * pv;
            }
            track[i] = 0f;
            // Evaporation rate of pools
            pool[i] = max(poolV - (SURFACE_EVAPORATION_RATE / tm.HEIGHT), 0f);
        }

        public void ChangeVegetationDensity(int x, int z, float mag){
            float4 v;
            // on axes
            int4 nh = CollectNeighbors(x, z);
            v = new float4(
                plants[nh.x],
                plants[nh.y],
                plants[nh.z],
                plants[nh.w]
            );
            v +=  mag * 0.6f;
            plants[nh.x] = v.x;
            plants[nh.y] = v.y;
            plants[nh.z] = v.z;
            plants[nh.w] = v.w;
            // on diagonals
            if(UnsafeNeighborhood(x, z)){
                nh = new int4(
                    SafeIdx(ne.x + x, ne.y + z),
                    SafeIdx(nw.x + x, nw.y + z),
                    SafeIdx(se.x + x, se.y + z),
                    SafeIdx(sw.x + x, sw.y + z)
                );
            }else{
                nh = new int4(
                    getIdx(ne.x + x, ne.y + z),
                    getIdx(nw.x + x, nw.y + z),
                    getIdx(se.x + x, se.y + z),
                    getIdx(sw.x + x, sw.y + z)
                );
            }
            v = new float4(
                plants[nh.x],
                plants[nh.y],
                plants[nh.z],
                plants[nh.w]
            );
            v +=  mag * 0.4f;
            plants[nh.x] = v.x;
            plants[nh.y] = v.y;
            plants[nh.z] = v.z;
            plants[nh.w] = v.w;

            // here
            nh.x = getIdx(x,z);
            v.x = plants[nh.x];
            v += mag * 1f;
            plants[nh.x] = v.x;
        }

        public void SpreadPool(
            int x,
            int z,
            ref NativeArray<FloodedNeighbor> buff,
            ref NativeQueue<BeyerParticle>.ParallelWriter particleQueue,
            ref ErosionParameters ep,
            bool drainParticles
        ){
            int idx = getIdx(x, z);
            float hLand = height[idx];
            float hWater = pool[idx];
            if(hWater <= 0f){
                return;
            }
            // Debug.Log($"dist >> {hWater}");
            float tHeight = hLand + hWater;
            if(x == 0 || z == 0 || x == tm.GENERATOR_RES.x - 1 || z == tm.GENERATOR_RES.y - 1){
                buff[0] = new FloodedNeighbor(SafeIdx(up.x + x, up.y + z), ref this);
                buff[1] = new FloodedNeighbor(SafeIdx(right.x + x, right.y + z), ref this);
                buff[2] = new FloodedNeighbor(SafeIdx(down.x + x, down.y + z), ref this);
                buff[3] = new FloodedNeighbor(SafeIdx(left.x + x, left.y + z), ref this);
                
            }else{
                buff[0] = new FloodedNeighbor(getIdx(up.x + x, up.y + z), ref this);
                buff[1] = new FloodedNeighbor(getIdx(right.x + x, right.y + z), ref this);
                buff[2] = new FloodedNeighbor(getIdx(down.x + x, down.y + z), ref this);
                buff[3] = new FloodedNeighbor(getIdx(left.x + x, left.y + z), ref this);
            }
            buff.Sort<FloodedNeighbor>();
            float fill = 0f;
            float diffV = 0f;
            for(int e = 0; e < 4; e++){
                fill = 0f;
                diffV = tHeight - buff[e].current; // if we moved the whole difference, they would just swap
                if(hWater < 1E-3f) continue;
                if( buff[e].water <= 0f && hLand >= buff[e].height){
                    // Found Drain!
                    if(drainParticles){
                        BeyerParticle p = new BeyerParticle(
                                (ushort) 64000,
                                getPos(buff[e].idx),
                                ep,
                                tm,
                                hWater
                            );
                        particleQueue.Enqueue(
                            p
                        );
                    }else{
                        buff[e].Commit(hWater, ref this);
                    }
                    hWater = 0f;
                    tHeight = hLand;
                }else if(diffV > 0f){
                    if(hWater <= 0f){
                        continue;
                    }
                    fill = min(0.25f * hWater, 0.25f * diffV);
                    hWater -= fill;
                    tHeight = hLand + hWater;
                    buff[e].Commit(fill, ref this);
                }else if (diffV < 0f){
                    if(buff[e].water <= 0f){
                        continue;
                    }
                    fill = min(0.25f * buff[e].water, -0.25f * diffV);
                    hWater += fill;
                    tHeight = hLand + hWater;
                    buff[e].Commit(-1f * fill, ref this);
                }
            }
            pool[idx] = hWater;
        }
    }

    // Flooding Helper
    public struct FloodedNeighbor: IComparable<FloodedNeighbor>, IEquatable<FloodedNeighbor> {
        
        public int idx;
        public float height;
        public float water;
        public float current {
            get { return height + water;}
            private set {}
        }

        public FloodedNeighbor(int idx, ref WorldTile tile){
            this.idx = idx;
            this.height = tile.height[this.idx];
            this.water = tile.pool[this.idx];
        }

        public void Commit(float val, ref WorldTile tile){
            tile.pool[idx] = water + val;

        }

        public int CompareTo(FloodedNeighbor obj){
            if (obj.Equals(this)){ return 0;}
            return GetHashCode() > obj.GetHashCode() ? 1 : -1;
        }

        public override int GetHashCode(){
            return current.GetHashCode();
        }

        public bool Equals(FloodedNeighbor other){
            if (idx != other.idx){
                return false;
            }
            return true;
        }
    }

    public struct PileSolver{
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        NativeArray<ManhattanVertex> verts;
        public WorldTile tile;
        int2 center;
        int maxDistance;

        public void Init(int radius){
            maxDistance = radius;
            int triCount = (((maxDistance + 1) * (maxDistance + 2)) * 2) - 3;
            verts = new NativeArray<ManhattanVertex>(triCount, Allocator.Temp);
            int c = 0;
            int2 dirA;
            int2 dirB;
            for (int dist = 0; dist < maxDistance; dist++){
                for(int dir = 0; dir < 4; dir++){
                    switch(dir){
                        case 0:
                            // NE
                            dirA = WorldTile.up;
                            dirB = WorldTile.right;
                            break;
                        case 1:
                            // SE
                            dirA = WorldTile.right;
                            dirB = WorldTile.down;
                            break;
                        case 2:
                            // SW
                            dirA = WorldTile.down;
                            dirB = WorldTile.left;
                            break;
                        case 3:
                            // NW
                            dirA = WorldTile.left;
                            dirB = WorldTile.up;
                            break;
                        default:
                            throw new ArgumentException();
                    }
                    for(int i = 0; i <= dist + 1; i++){
                        verts[c] = new ManhattanVertex(dist, dir, i, dirA, dirB);
                        c++;
                    }
                }
            }
        }

        public void SetPile(int2 pos){
            ManhattanVertex tri;
            for(int i = 0; i < verts.Length; i++){
                tri = verts[i];
                tri.SetPos(pos, ref tile);
                verts[i] = tri;
            }
        }

        public float DepositSediment(float amount, float increment){
            int c = 0;
            float deposited = 0f;
            float remaining = amount;
            float level = 0f;
            ManhattanVertex tri;
            for (int round = 1; round <= maxDistance; round++){
                level = verts[0].val + (increment * (float)round);
                c = -1;
                for(int dist = 0; dist < round; dist++){
                    for(int dir = 0; dir < 4; dir++){
                        for(int i = 0; i <= dist + 1; i++){
                            c++;
                            tri = verts[c];
                            if(!tri.isValid){
                                continue;
                            }
                            if(!tri.CanRaiseTo(level)){
                                continue;
                            }
                            float diff = tri.RaiseBy(min(increment, remaining));
                            deposited += diff;
                            remaining = amount - deposited;
                            verts[c] = tri;
                            if(remaining <= 0f) return 0f;
                        }
                    }
                }
            }
            return remaining;
        }

        public void CommitChanges(ref WorldTile tile){
            int c = -1;
            ManhattanVertex tri;
            for(int dist = 0; dist < maxDistance; dist++){
                for(int dir = 0; dir < 4; dir++){
                    for(int i = 0; i <= dist + 1; i++){
                        c++;
                        tri = verts[c];
                        if(!tri.isValid) continue;
                        if(!tri.modified) continue;
                        tri.CommitHeight(ref tile);
                    }
                }
            }
        }

        public void HandlePile(int2 pos, float amount, float increment){
            SetPile(pos);
            float remaining = amount;
            while (remaining > 0f){
                remaining = DepositSediment(remaining, increment);
            }
            CommitChanges(ref tile);
            // Debug.Log($"committed {amount} at {pos.x}, {pos.y}");
        }
    }

    public struct ManhattanVertex {
        int2 posRef;
        int2 vertOffset;
        public int dir;
        public int dist;
        public int instanceId;

        public bool modified;
        public bool isValid;
        
        int idx;
        public float val;

        public ManhattanVertex(int dist, int dir, int instanceId, int2 dirA, int2 dirB){
            this.posRef = new int2(0);
            this.vertOffset = new int2(0);
            this.dist = dist;
            this.dir = dir;
            this.instanceId = instanceId;
            
            this.idx = 0;
            this.val = 0f;

            this.modified = false;
            this.isValid = true;
            vertOffset = GetOffset(dist, instanceId, dirA, dirB);
        }

        public static int2 GetOffset(int dist, int i, int2 dirA, int2 dirB){
            int2 startA = dist * dirA;
            return startA + (i * (dirB - dirA));
        }

        public void SetPos(int2 pos, ref WorldTile tile){
            posRef = pos + vertOffset;
            if (!tile.InBounds(posRef)){
                isValid = false;
                return;
            }
            isValid = true;
            modified = false;
            idx = tile.getIdx(posRef);
            val = tile.height[idx];
        }

        public void CommitHeight(ref WorldTile tile){
            tile.height[idx] = val;
        }

        public bool CanRaiseTo(float level){
            if(val < level) return true;
            return false;
        }

        public float RaiseBy(float increment){
            modified = true;
            val += increment;
            return increment;
        }
    }

    public enum ColorChannelFloat
    {
        R = 0,
        G = 4,
        B = 8,
        A = 12
    }

    public enum ColorChannelByte
    {
        R = 0,
        G = 1,
        B = 2,
        A = 3
    }

    public struct RGBA32
    {

        public byte R, G, B, A;

        public static explicit operator int4 ( RGBA32 val ) => new int4{ x=val.R , y=val.G , z=val.B , w=val.A };
        public static explicit operator RGBA32 ( int4 val ) => new RGBA32{ R=(byte)val.x , G=(byte)val.y , B=(byte)val.z , A=(byte)val.w };
	
        public static RGBA32 operator + ( RGBA32 lhs , RGBA32 rhs ) => (RGBA32)( (int4)lhs + (int4)rhs );
        public static RGBA32 operator - ( RGBA32 lhs , RGBA32 rhs ) => (RGBA32)( (int4)lhs - (int4)rhs );

        public byte this[ColorChannelByte c]{
            get {
                switch(c){
                    case ColorChannelByte.R:
                        return this.R;
                    case ColorChannelByte.G:
                        return this.G;
                    case ColorChannelByte.B:
                        return this.B;
                    case ColorChannelByte.A:
                        return this.A;
                    default:
                        throw new ArgumentOutOfRangeException("unknown color");
                }
            }
            set {
                switch(c){
                    case ColorChannelByte.R:
                        this.R = value;
                        break;
                    case ColorChannelByte.G:
                        this.G = value;
                        break;
                    case ColorChannelByte.B:
                        this.B = value;
                        break;
                    case ColorChannelByte.A:
                        this.A = value;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException("unknown color");
                }
            }
        }

    }

    public enum Heading : byte {
        // Exclusive direction
        N    = 0b_00000001,
        E    = 0b_00000100,
        S    = 0b_00000010,
        W    = 0b_00001000,
        NE   = 0b_00000101,
        SE   = 0b_00000110,
        SW   = 0b_00001010,
        NW   = 0b_00001001,
        NONE = 0b_00000000
    }

    public static class HeadingExt {

        public static readonly Heading[] WTORDER = new Heading[8] {
            Heading.N,
            Heading.E,
            Heading.S,
            Heading.W,
            Heading.NE,
            Heading.SE,
            Heading.SW,
            Heading.NW
        };

        public static readonly Heading[] ADJACENT = new Heading[8] {
            Heading.N,
            Heading.NE,
            Heading.E,
            Heading.SE,
            Heading.S,
            Heading.SW,
            Heading.W,
            Heading.NW
        };

        public static int ToWorldTileIdx(this Heading heading){
            for(int i = 0; i < 8; i++){
                if(heading == WTORDER[i]) return i;
            }
            return -1;
        }

        public static void AdjacentHeadings(this Heading heading, out Heading left, out Heading right){
            int i;
            for(i = 0; i < 8; i++){
                if(heading == ADJACENT[i]) break;
            }
            if(i == 0){
                left = ADJACENT[7];
                right = ADJACENT[1];
            }else if(i == 7){
                left = ADJACENT[6];
                right = ADJACENT[0];
            }else{
                left = ADJACENT[i - 1];
                right = ADJACENT[i + 1];
            }
        }

        public static String Name(this Heading heading){
            return $"{(byte) heading}";
        }

        // public static void Orthogonal(this Heading h, out int2 a, out int2 b){
        //     if((byte) h < 3){
        //         a = new int2(1, 0);
        //         b = new int2(-1, 0);
        //     }else if(h == Heading.E || h == Heading.W){
        //         a = new int2(0, 1);
        //         b = new int2(0, -1);
        //     }else if(h == Heading.NW || h == Heading.SE){
        //         a = new int2(1, 1);
        //         b = new int2(-1, -1);
        //     }else{
        //         a = new int2(1, -1);
        //         b = new int2(-1, 1);
        //     }
        // }

        public static Heading FromInt2(int2 dir){
            Heading b = 0;
            if( dir.x > 0){
                b = b | Heading.E;
            }else if (dir.x < 0){
                b = b | Heading.W;
            }
            if( dir.y > 0){
                b = b | Heading.N;
            }else if (dir.y < 0){
                b = b | Heading.S;
            }
            return b;
        }

        public static Heading FromFloat2(float2 dir){
            Heading b = 0;
            if( dir.x > 0f){
                b = b | Heading.E;
            }else if (dir.x < 0f){
                b = b | Heading.W;
            }
            if( dir.y > 0f){
                b = b | Heading.N;
            }else if (dir.y < 0f){
                b = b | Heading.S;
            }
            return b;
        }

        public static int2 ToInt2(this Heading heading){
            byte b = (byte) heading;
            
            int x = (b >> 2 & 1) != 0 ? 1 : (((b >> 3 & 1) != 0) ? -1 : 0);
            int y = (b >> 0 & 1) != 0 ? 1 : (((b >> 1 & 1) != 0) ? -1 : 0);
            // int x = ((b & (byte) Heading.E) == b) ? 1 : ((b & (byte) Heading.W) == b ? -1 : 0 );
            // int y = ((b & (byte) Heading.N) == b) ? 1 : ((b & (byte) Heading.S) == b ? -1 : 0 );
            return new int2(
                (b >> 2 & 1) != 0 ? 1 : (((b >> 3 & 1) != 0) ? -1 : 0),
                (b >> 0 & 1) != 0 ? 1 : (((b >> 1 & 1) != 0) ? -1 : 0)
            );
        }

        // public static void TestToInt2(){
        //     PrintInt2(ToInt2(Heading.N));
        //     PrintInt2(ToInt2(Heading.E));
        //     PrintInt2(ToInt2(Heading.S));
        //     PrintInt2(ToInt2(Heading.W));
        //     PrintInt2(ToInt2(Heading.NE));
        //     PrintInt2(ToInt2(Heading.SE));
        //     PrintInt2(ToInt2(Heading.SW));
        //     PrintInt2(ToInt2(Heading.NW));
        //     throw new Exception();
        // }

        // public static void TestInt2Conversion(int2 d){
        //     Heading h = FromInt2(d);
        //     int2 d2 = ToInt2(h);
        //     bool2 ok = (d2 == d);
        //     if(!ok.x || !ok.y){
        //         byte b = (byte) h;
        //         Debug.LogError($"mismatch {b} !!! in: {d.x}, {d.y}");
        //     }
        // }

        // public static void PrintInt2(int2 r){
        //     Debug.Log($"{r.x}, {r.y}");
        // }
    }

}