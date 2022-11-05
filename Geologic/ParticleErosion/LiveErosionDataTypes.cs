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

    // Erosive Events
    public struct ErosiveEvent : IEquatable<ErosiveEvent> {
        public int idx; // the tile space idx affected
        public Heading dir; // the current direction of movement
        public float deltaWaterTrack;
        public float deltaPoolMap;
        public float deltaSediment;

        public static implicit operator ErosiveEvent(int idx){
            return new ErosiveEvent() {
                idx = idx,
                dir = Heading.NONE,
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

    public struct ErosionParameters {
        public float INERTIA;
        public float GRAVITY;
        public float DRAG;
        public float FRICTION;
        public float EVAP;
        public float EROSION;
        public float DEPOSITION;

        public float SLOW_CULL_ANGLE;
        public float SLOW_CULL_SPEED;
        public float CAPACITY;
        public int MAXAGE;
        public float TERMINAL_VELOCITY;

        public float SURFACE_EVAPORATION_RATE;
        public float POOL_PLACEMENT_DIVISOR;
        public float TRACK_PLACEMENT_DIVISOR;

        public int PILING_RADIUS;
        public float MIN_PILE_INCREMENT;
        public float PILE_THRESHOLD;
        
        public float HEIGHT;
        public float2 PATCH_RES;
        public int2 TILE_RES;

        public ErosionParameters(int resX, int resZ, float height, float width){
            INERTIA = 0.7f;
            GRAVITY = 1f;
            DRAG = .001f;
            FRICTION = .001f;
            EVAP = .001f;
            EROSION = 0.2f;

            DEPOSITION = 0.05f;
            SLOW_CULL_ANGLE = 3f;
            SLOW_CULL_SPEED = .1f;
            CAPACITY = 3f;
            MAXAGE = 64;
            TERMINAL_VELOCITY = 1 / DRAG; // vel = DRAG * pow(vel, 2)

            SURFACE_EVAPORATION_RATE = 0.1f;
            POOL_PLACEMENT_DIVISOR = 0.5f;
            TRACK_PLACEMENT_DIVISOR = 80f;

            PILING_RADIUS = 15;
            MIN_PILE_INCREMENT = 1f;
            PILE_THRESHOLD = 2f;

            HEIGHT = height;                                     // Total Height in M
            PATCH_RES = new float2(width / resX, width/ resZ); // grid space in M
            TILE_RES = new int2(resX, resZ);                     // grid spaces

        }
    }

    public struct BeyerParticle : IEquatable<BeyerParticle> {
        
        public int pid;
        public float2 pos;
        public float2 dir; // norm
        public Heading heading;
        public float vel;
        public float water;
        public float sediment;
        public bool isDead;
        public int age;
        
        public ErosionParameters ep;

        static readonly float2 left = new float2(-1, 0);
        static readonly float2 right = new float2(1, 0);
        static readonly float2 up = new float2(0, 1);
        static readonly float2 down = new float2(0, -1);
        private static readonly float2 ZERO2 = new float2(0);
        private static readonly bool2 TRUE2 = new bool2(true);

        public BeyerParticle(int2 pos, ErosionParameters ep, bool dead){
            this.pid = (100_000 * (int)pos.x) + (int) pos.y;
            this.pos = new float2(pos);
            this.dir = new float2(0f, 0f);
            this.heading = Heading.NONE;
            this.vel = .01f;
            this.water = 1f;
            this.sediment = 0f;
            this.isDead = dead;
            this.age = 0;
            this.ep = ep;
        }

        public BeyerParticle(int2 pos, ErosionParameters ep, float water): this(pos, ep, false){
            this.water = water;
        }

        public bool Equals(BeyerParticle other){
            // TODO use lazy comparer for the floats
            if (!(pos == other.pos).Equals(TRUE2)){
                return false;
            }
            if (!(vel == other.vel)){
                return false;
            }
            return (water == other.water && sediment == other.sediment);
        }

        public void Reset(int2 pos){
            this.pos = new float2(pos);
            this.pid = (100_000 * (int)pos.x) + (int) pos.y;
            this.dir = new float2(0f, 0f);
            this.vel = .01f;
            this.water = 1f;
            this.sediment = 0f;
            this.isDead = false;
            this.age = 0;
            this.ep = ep;
        }

        public float2 nextPos(ref WorldTile tile) {
            float h = tile.WIH(pos);
            float4 neighbor = new float4(
                tile.WIH(pos + up),
                tile.WIH(pos + right),
                tile.WIH(pos + down),
                tile.WIH(pos + left)
            );

            float3 a = cross(new float3(0, h - neighbor.x, ep.PATCH_RES.y), new float3(ep.PATCH_RES.x, h - neighbor.y, 0));
            float3 b = cross(new float3(0, h - neighbor.z, -ep.PATCH_RES.y), new float3(-ep.PATCH_RES.x, h - neighbor.w, 0));
            float3 norm = new float3(a.x + b.x, a.y + b.y, a.z + b.z);
            float2 g = new float2(-norm.x, -norm.z);
            float3 _norm = normalize(norm);
            float2 gnorm = round(4f * normalize(g)) / 4f;
            // dir = normalize((dir * (ep.INERTIA * (1 - _norm.y))) + (gnorm * ( 1f - (ep.INERTIA * (1 - _norm.y)))));
            dir = normalize((dir * (ep.INERTIA * (vel / ep.TERMINAL_VELOCITY) * (1 - _norm.y))) + (gnorm * ( 1f - (ep.INERTIA * (vel / ep.TERMINAL_VELOCITY) * (1 - _norm.y)))));
            // make rounder
            dir = round(10f * dir) / 10f;
            return pos + dir;
        }

        public bool DescendSimultaneous(ref WorldTile tile, out ErosiveEvent evt){
            int idx = tile.getIdx(pos);
            evt = idx;
            evt.dir = HeadingExt.FromFloat2(dir);
            if(water < 1E-8f){
                // Debug.Log($"dead from dehydration {water}: {age}");
                isDead = true;
                evt.deltaSediment = sediment / ep.HEIGHT;
                return false;
            }
            if(age >= ep.MAXAGE){
                // Debug.Log($"{pid} dead from old age {age}");
                isDead = true;
                evt.deltaPoolMap = water / ep.HEIGHT;
                evt.deltaSediment = sediment / ep.HEIGHT;
                return false;
            }
            // if(tile.StandingWater(pos)){
            //     float drop = 0.25f * sediment;
            //     evt.deltaSediment += (drop / ep.HEIGHT);
            //     sediment -= drop;
            // }
            // if( age > 10 && cmin(vhist) < .1f){
            //     // Debug.Log($"{pid} dead from slow speed {age}");
            //     evt.deltaPoolMap += water / ep.HEIGHT;
            //     evt.deltaSediment += sediment / ep.HEIGHT;
            //     isDead = true;
            //     return false;
            // }
            // if(vel <= 0f){
            //     evt.deltaPoolMap = water / ep.HEIGHT;
            //     evt.deltaSediment = sediment / ep.HEIGHT;
            //     isDead = true;
            //     return false;
            // }
            float2 posN = nextPos(ref tile);    
            evt.dir = HeadingExt.FromFloat2(dir);

            if (tile.OutOfBounds(posN)) {
                // Debug.Log($"dead from oob {age}");
                isDead = true;
                return false;
            } // went OOB
            float currentHeight = tile.WIH(pos);
            float hDiff = tile.WIH(posN) - tile.WIH(pos); // in world Meters
            float vDiff = abs(hDiff);
            float acceleration = 0f;
            float depositionAmount = 0f;
            float currentCapacity = 0f;
            float thetaD = 0f;
            float deltaV = 0f;
            float effectiveDrag = ep.DRAG * (1f - min(tile.flow[idx], 1f));
            float effectiveFriction =  ep.FRICTION * (1f - min(tile.flow[idx], 1f));

            if(vDiff > 0f){
                float theta = atan(vDiff / ep.PATCH_RES.x);
                thetaD = theta * 180f / 3.14159f;
                float accelerationSign = 1f;
                if(hDiff > 0f){
                    // uphill
                    acceleration = (ep.GRAVITY * sin(theta)) + effectiveFriction;
                    accelerationSign = -1f;

                }else{
                    acceleration = ep.GRAVITY * sin(theta) - effectiveFriction;
                }
                deltaV = accelerationSign * sqrt(2 * abs(acceleration) * (vDiff / sin(theta)));
            }
            vel = max((vel + deltaV), 0f);
            vel = max(vel - (effectiveDrag * vel * vel), 0f);
            if( thetaD < 3f && vel < 1f){
                // Debug.Log($"{pid} dead from slow speed {age}");
                evt.deltaPoolMap += water / ep.HEIGHT;
                evt.deltaSediment += sediment / ep.HEIGHT;
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
            if(abs(depositionAmount) > 0f){
                evt.deltaSediment += depositionAmount / ep.HEIGHT;
                // if(depositionAmount > 0f){
                //     Debug.Log($"{pid} v:{vel}, w:{water}, sed:{sediment} // cap:{currentCapacity} >> D:{evt.deltaSediment}");
                // }else{
                //     Debug.Log($"{pid} v:{vel}, w:{water}, sed:{sediment} // cap:{currentCapacity} >> E:{evt.deltaSediment}");
                // }
                sediment -= depositionAmount;
            }
            evt.deltaWaterTrack = water;
            water = water * (1 - ep.EVAP);
            pos = posN;
            age++;
            return true;
        }

    }

    public struct WorldTile {

        public ErosionParameters ep;

        public static readonly int2 ZERO2 = new int2(0);
        
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> height;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> pool;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> flow;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<float> track;

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

        static public readonly float MINFLOWPOOL = .00005f;
        static readonly float lRate = 0.05f;

        public float3 Normal(int2 pos){
            // returns normal of the (WIH)
            
            float3 n = Normal(pos + nw, right, down);
            n += Normal(pos + ne, down, left);
            n += Normal(pos + se, left, up);
            n += Normal(pos + sw, up, right);
            n += Normal(pos, left, up);
            n += Normal(pos, up, right);
            n += Normal(pos, right, down);
            n += Normal(pos, down, left);

            return normalize(n);
        }

        public float3 Normal(int2 pos, int2 a, int2 b){
            return cross(
                HiIVec(pos, 0, 0) - HiIVec(pos, a.x, a.y),
                HiIVec(pos, 0, 0) - HiIVec(pos, b.x, b.y)
            );
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIV(int idx){
            return height[idx] + pool[idx];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(int idx){
            return ep.HEIGHT * (height[idx] + pool[idx]);
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
        public float WIH(int x, int z){
            return WIH(getIdx(x, z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float WIH(int2 pos, int dx, int dz){
            return WIH(SafeIdx(pos.x + dx, pos.y + dz));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 HiIVec(int2 pos, int dx, int dz){
            return new float3(dx, WIH(pos, dx, dz), dz);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SafeIdx(int x, int z){
            return getIdx(
                clamp(x, 0, ep.TILE_RES.x - 1),
                clamp(z, 0, ep.TILE_RES.y - 1));
        }        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SafeIdx(int2 pos){
            return getIdx(clamp(pos, ZERO2, ep.TILE_RES - 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SafeIdx(float2 pos){
            return SafeIdx(new int2(pos));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int2 pos){
            if(pos.x < 0 || pos.y < 0 || pos.x >= ep.TILE_RES.x || pos.y >= ep.TILE_RES.y) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(int x, int z){
            return x * ep.TILE_RES.x + z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(int2 pos){
            return getIdx(pos.x, pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int getIdx(float2 pos){
            return getIdx((int) pos.x, (int) pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 getPos(int idx){
            return new int2((idx / ep.TILE_RES.x), (idx % ep.TILE_RES.x));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OutOfBounds(int2 pos){
            if(pos.x < 0 || pos.y < 0 || pos.x >= ep.TILE_RES.x || pos.y >= ep.TILE_RES.y) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool OutOfBounds(float2 pos){
            return OutOfBounds(new int2(pos));
        }

        public void UpdateFlowMapFromTrack(int x, int z){
            int i = getIdx(x, z);
            float pv = flow[i];
            float tv = track[i];
            float poolV = pool[i];
            if(poolV > MINFLOWPOOL){
                flow[i] = ((1f - 0.1f * lRate) * pv);
            }
            else if(tv > 0f){
                flow[i] = ((1f - lRate) * pv) + (lRate * 50.0f * tv) / (1f + 50.0f* tv);
            }else{
                flow[i] = (1f - lRate) * pv;
            }
            track[i] = 0f;
            // Evaporation rate of pools
            pool[i] = max(poolV - (ep.SURFACE_EVAPORATION_RATE / ep.HEIGHT), 0f);
        }

        public void SpreadPool(
            int x,
            int z,
            ref NativeArray<FloodedNeighbor> buff,
            ref NativeQueue<BeyerParticle>.ParallelWriter particleQueue,
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
            if(x == 0 || z == 0 || x == ep.TILE_RES.x - 1 || z == ep.TILE_RES.y - 1){
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
                        particleQueue.Enqueue(
                            new BeyerParticle(
                                getPos(buff[e].idx),
                                ep,
                                hWater
                            )
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
            // Debug.Log($"used : {c} / {triCount}");
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
                // Debug.Log($"reference level for rnd{round} === {level}");
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
                            // float diff = tri.RaiseBy(max(min(0.5f * (level - increment) - tri.val, remaining), 0f));
                            // float change = max(((level - tri.val) - increment), increment);
                            // float diff = tri.RaiseBy(min(change, remaining));
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

    // public struct ManhattanTriangle {
    //     int2 posRef;
    //     int dist;
    //     int instanceId;
    //     bool pointsIn;
    //     bool sawTooth;
    //     public bool modified;
    //     public bool isValid;
    //     int2 dirA;
    //     int2 dirB;

    //     int2 oa;
    //     int2 ob;
    //     int2 oc;

    //     int3 idxP;
    //     float3 valP;

    //     public ManhattanTriangle(int dist, int i, bool pointsIn, bool sawTooth, int2 dirA, int2 dirB){
    //         this.posRef = new int2(0);
    //         this.dist = dist;
    //         this.instanceId = i;
    //         this.pointsIn = pointsIn;
    //         this.sawTooth = sawTooth;
    //         this.dirA = dirA;
    //         this.dirB = dirB;
    //         this.idxP = new int3(0);
    //         this.valP = new float3(0f);
    //         this.modified = false;
    //         this.isValid = true;
    //         oa = new int2(0);
    //         ob = new int2(0);
    //         oc = new int2(0);
    //         TriangleIndices(dist, i, sawTooth, dirA, dirB);
    //     }

    //     public void TriangleIndices(int dist, int i, bool sawTooth, int2 dirA, int2 dirB){
    //         if(sawTooth){
    //             if (i % 2 == 0){
    //                 i /= 2;
    //                 i = (int) i;
    //                 oa = ManhattanMember(dist, i, dirA, dirB);
    //                 ob = ManhattanMember(dist + 1, i, dirA, dirB);
    //                 oc = ManhattanMember(dist + 1, i + 1, dirA, dirB);
    //             }else{
    //                 i /= 2;
    //                 i = (int) i;
    //                 i += 1;
    //                 oa = ManhattanMember(dist, i, dirA, dirB);
    //                 ob = ManhattanMember(dist + 1, i, dirA, dirB);
    //                 oc = ManhattanMember(dist, i - 1, dirA, dirB);
    //             }
    //         }else{
    //             if (i % 2 == 0){
    //                 i /= 2;
    //                 i = (int) i;
    //                 oa = ManhattanMember(dist, i, dirA, dirB);
    //                 ob = ManhattanMember(dist + 2, i + 1, dirA, dirB);
    //                 oc = ManhattanMember(dist + 1, i, dirA, dirB);
    //             }else{
    //                 i -= 1;
    //                 i /= 2;
    //                 i = (int) i;
    //                 oa = ManhattanMember(dist, i, dirA, dirB);
    //                 ob = ManhattanMember(dist + 2, i + 1, dirA, dirB);
    //                 oc = ManhattanMember(dist + 1, i + 1, dirA, dirB);
    //             }
                
    //         }
    //     }

    //     public void SetPos(int2 pos, ref WorldTile tile){
    //         posRef = pos;
    //         if (!tile.InBounds(pos + oa) || !tile.InBounds(pos + ob) || !tile.InBounds(pos + oc)){
    //             isValid = false;
    //             return;
    //         }
    //         isValid = true;
    //         idxP.x = tile.getIdx(pos + oa);
    //         idxP.y = tile.getIdx(pos + ob);
    //         idxP.z = tile.getIdx(pos + oc);
    //         valP.x = tile.height[idxP.x];
    //         valP.y = tile.height[idxP.y];
    //         valP.z = tile.height[idxP.z];
    //         modified = false;
    //     }

    //     public float LowPoint(){
    //         return cmin(valP);
    //     }

    //     public float HightPoint(){
    //         return cmax(valP);
    //     }

    //     public bool InnerPointBelowAllOuter(){
    //         // a closer point is lower than a further point
    //         if(pointsIn){
    //             return valP.x <= cmin(valP.yz);
    //         }else{
    //             return cmin(valP.xz) <= valP.y;
    //         }            
    //     }

    //     public float RaiseInnerPoint(float aboveMinOuter){
    //         modified = true;
    //         if(pointsIn){
    //             float diff = (cmin(valP.xz) - valP.y) + aboveMinOuter;
    //             valP.y += diff;
    //             return diff;
    //         }else{
    //             // assumes both are below the outer point
    //             float2 diff = (valP.y - valP.xz) + aboveMinOuter;
    //             valP.xz += diff;
    //             return csum(diff);
    //         }
    //     }

    //     // public bool pointsOut(){
    //     //     // if true, A & C are closer to the reference than B
    //     //     // if false, the opposite is true
    //     //     // repeats as manhattan distance (0, 1, 0) >> (0, 1, 1) >> (0, 1, 0) >> (0, 1, 1) ...
    //     //     return instanceId % 2 != 0;
    //     // }

    //     public static int2 ManhattanMember(int dist, int i, int2 dirA, int2 dirB){
    //         int2 startA = dist * dirA;
    //         return startA + (i * (dirB - dirA));
    //     }

    //     public void CommitHeights(ref WorldTile tile){
    //         tile.height[idxP.x] = valP.x;
    //         tile.height[idxP.y] = valP.y;
    //         tile.height[idxP.z] = valP.z;
    //     }



    // }

    public enum Heading : byte {
        // Exclusing direction
        N    = 0b_00000001,
        S    = 0b_00000010,
        E    = 0b_00000100,
        W    = 0b_00001000,
        NW   = 0b_00001001,
        NE   = 0b_00000101,
        SW   = 0b_00001010,
        SE   = 0b_00000110,
        NONE = 0b_00000000
    }

    public static class HeadingExt {

        public static int2 ToInt2(this Heading heading){
            byte b = (byte) heading;
            return new int2(
                ((b & (byte) Heading.E) == b) ? 1 : ((b & (byte) Heading.W) == b ? -1 : 0 ),
                ((b & (byte) Heading.N) == b) ? 1 : ((b & (byte) Heading.S) == b ? -1 : 0 )
            );
        }

        public static String Name(this Heading heading){
            return Enum.GetName( typeof(Heading), heading);
        }

        public static void Orthogonal(this Heading h, out int2 a, out int2 b){
            if((byte) h < 3){
                a = new int2(1, 0);
                b = new int2(-1, 0);
            }else if(h == Heading.E || h == Heading.W){
                a = new int2(0, 1);
                b = new int2(0, -1);
            }else if(h == Heading.NW || h == Heading.SE){
                a = new int2(1, 1);
                b = new int2(-1, -1);
            }else{
                a = new int2(1, -1);
                b = new int2(-1, 1);
            }
        }

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
    }

}