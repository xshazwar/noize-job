// using System;
// using System.Collections.Generic;

// using System.Runtime.CompilerServices;
// using System.Runtime.InteropServices;

// using Unity.Collections.LowLevel.Unsafe;
// using Unity.Profiling;

// using Unity.Burst;
// using Unity.Collections;
// using Unity.Jobs;
// using UnityEngine;

// #if UNITY_EDITOR
// using UnityEngine.Assertions;
// #endif

// using static Unity.Mathematics.math;

// using xshazwar.noize.pipeline;
// using xshazwar.noize.filter;

// namespace xshazwar.noize.geologic {
//     using Unity.Mathematics;

//     struct LazyFloatComparer: IComparer<float> {
//         public int Compare(float a, float b){
//             float diff = b - a;
//             if (abs(diff) < .00000000001){
//                 return 0;
//             }
//             if (diff > 0){
//                 return -1;
//             }
//             return 1;
//         }
//     }

// /*
// // 
// //   POOLS
// // 
// */
//     public struct PoolUpdate:  IComparable<PoolUpdate>, IEquatable<PoolUpdate>{
//         public int minimaIdx;
//         public float volume;

//         public int CompareTo(PoolUpdate obj){
//             if (obj.Equals(this)){ return 0;}
//             return GetHashCode() > obj.GetHashCode() ? 1 : -1;
//         }

//         public override int GetHashCode(){
//             return minimaIdx.GetHashCode() ^ volume.GetHashCode();
//         }

//         public bool Equals(PoolUpdate other){
//             if (minimaIdx != other.minimaIdx){
//                 return false;
//             }
//             return true;
//         }
//     }

//     public struct PoolKey : IEquatable<PoolKey>, IComparable<PoolKey> {
//         // This can be used to reference a minima or a drain. Anything that is ambigous between pool orders
//         public int idx;
//         // A minima can host multiple successive pools of different characteristics, so we allow an order here. Zero is the smallest pool
//         public byte order;
//         public byte n;
    
//         public bool Equals(PoolKey other){
//             if (idx != other.idx){
//                 return false;
//             }
//             return (order == other.order && n == other.n);
//         }

//         public int CompareTo(PoolKey obj){
//             if (obj.Equals(this)){ return 0;}
//             return GetHashCode() < obj.GetHashCode() ? 1 : -1;
//         }
     
//         public override int GetHashCode(){
//             return idx.GetHashCode() ^ (order.GetHashCode() + n.GetHashCode());
//             // return idx.GetHashCode() + (order.GetHashCode() * n.GetHashCode());
//         }

//         public PoolKey Clone(){
//             return new PoolKey {
//                 idx = idx,
//                 order = order,
//                 n = n
//             };
//         }

//         public bool Exists(){
//             return this.idx > -1;
//         }
    
//     }

//     public struct Pool : IEquatable<Pool>, IComparable<Pool> {

//         public int indexMinima;
//         public float minimaHeight;
//         public int indexDrain;
//         public float drainHeight;
//         public byte order;  // pool order as defined by it's drain

//     // Properties for Filling

//         // a pool can only have 3 peers in our grid layout 
//         // in the worst case of a confluence at a drain and cardinal neighbors
//         // distinct from one another by diagonals
 
//         public PoolKey peer0;
//         public PoolKey peer1;
//         public PoolKey peer2;
        
//         // In cases of a pool with peers. When all are full a new pool is created. This is the reference
//         public PoolKey supercededBy;

//         public int memberCount; // total verts
//         public float capacity;  // in normalized height units (a single vert can occupy a volume of 0 -> 1)
//         public float volume;    // in normalized height units
//         public float minVolume; // minimum volume, under this threshold constituent pools are not yet filled

//         // Beta1 for regression
//         public float b1;
//         // Beta2 for regression
//         public float b2;

//         public void Init(int indexMinima_, float minimaHeight_, int indexDrain_, float drainHeight_, byte order_){
//             indexMinima = indexMinima_;
//             minimaHeight = minimaHeight_;
//             indexDrain = indexDrain_;
//             drainHeight = drainHeight_;
//             order = order_;
//             volume = 0f;
//             minVolume = 0f;
//             peer0 = new PoolKey {idx = -1}; // idx : -1 === DNE
//             peer1 = new PoolKey {idx = -1};
//             peer2 = new PoolKey {idx = -1};
//             supercededBy = new PoolKey {idx = -1};
//         }

//         public bool Exists(){
//             return indexMinima > -1;
//         }
        
//         public void EstimateHeight(float cellHeight, out float waterHeight){
//             // wh == surfaceHeightAtMinima(volume) - cellHeight 
//             waterHeight = (b1 + (b2 * log(math.max(volume + 1f, 1f)))) - cellHeight;
//         }

//         public void SetMinimumVolume(float confluenceHeight){
//             // for higher order pools, a confluence must be met
//             // before the pool will fill
//             // confluenceHeight = (b1 + (b2 * log(math.max(volume + 1f, 1f)))) - minimaHeight;
//             minVolume = exp((confluenceHeight - minimaHeight) / b2) - 1f;
//             // float checksum = (b1 + (b2 * log(math.max(minVolume + 1f, 1f)))) - confluenceHeight;
//             // Debug.LogWarning($"min volume {minVolume} / {capacity} ->> {checksum}");

//         }
        
//         public void SolvePool(NativeArray<float> heights){
//             memberCount = heights.Length;
//             capacity = drainHeight - minimaHeight; // just to start;
//             heights.Sort<float, LazyFloatComparer>(new LazyFloatComparer());
//             for (int i = 1 ; i < heights.Length; i++){
//                 capacity += (drainHeight - heights[i]);
//             }
//             b1 = minimaHeight;
//             b2 = (drainHeight - minimaHeight) / log(capacity + 1f);
//         }

//         public bool HasParent(){
//             return this.supercededBy.Exists();
//         }

//         public bool HasPeer(PoolKey peer){
//             return peer.Equals(peer0) || peer.Equals(peer1) || peer.Equals(peer2);
//         }

//         public void AddPeer(int peerIdx, PoolKey key){
//             if(peerIdx == 0) peer0 = key;
//             else if(peerIdx == 1) peer1 = key;
//             else if(peerIdx == 2) peer2 = key;
//         }

//         public PoolKey GetPeer(int peerIdx){
//             if(peerIdx == 0) return peer0;
//             else if(peerIdx == 1) return peer1;
//             else if(peerIdx == 2) return peer2;
//             throw new ArgumentException("only three valid peers");
//         }

//         public int PeerCount(){
//             if(!peer0.Exists()) return 0;
//             if(!peer1.Exists()) return 1;
//             if(!peer2.Exists()) return 2;
//             return 3;
//         }

//         // equals / compares

//         public bool Equals(Pool other){
//             if (indexMinima != other.indexMinima){
//                 return false;
//             }
//             return (order == other.order && indexDrain == other.indexDrain);
//         }

//         public int CompareTo(Pool obj){
//             if (obj.Equals(this)){ return 0;}
//             return GetHashCode() > obj.GetHashCode() ? 1 : -1;
//         }
     
//         public override int GetHashCode(){
//             return indexMinima.GetHashCode() + (order.GetHashCode());
//             // return idx.GetHashCode() + (order.GetHashCode() * n.GetHashCode());
//         }
//     }

//     // // Erosive Events

//     // public struct ErosiveEvent : IEquatable<ErosiveEvent> {
//     //     public int idx; // the tile space idx affected
//     //     public float deltaWaterTrack;
//     //     public float deltaPoolMap;
//     //     public float deltaSediment;

//     //     public static implicit operator ErosiveEvent(int idx){
//     //         return new ErosiveEvent() {
//     //             idx = idx,
//     //             deltaWaterTrack = 0,
//     //             deltaPoolMap = 0,
//     //             deltaSediment = 0
//     //         };
//     //     }

//     //     public bool Equals(ErosiveEvent other){
//     //         // TODO use lazy comparer for the floats
//     //         if (idx != other.idx){
//     //             return false;
//     //         }
//     //         return (deltaWaterTrack == other.deltaWaterTrack && deltaPoolMap == other.deltaPoolMap && deltaSediment == other.deltaSediment);
//     //     }
//     // }

//     // public struct BeyerParticle : IEquatable<BeyerParticle> {
//     //     public float2 pos;
//     //     public float2 dir; // norm
//     //     public float vel;
//     //     public float water;
//     //     public float sediment;
//     //     public bool isDead;
//     //     public int age;
        
//     //     // tuning constants
//     //     static readonly float INERTIA = 0.6f;
//     //     static readonly float GRAVITY = 1f;
//     //     static readonly float FRICTION = .9f;
//     //     // static readonly float EVAP = .001f;
//     //     static readonly float EVAP = .01f;
//     //     static readonly float EROSION = 0.2f;
//     //     static readonly float DEPOSITION = 0.1f;
//     //     static readonly float MINSLOPE = 0.00001f;
//     //     static readonly float CAPACITY = .2f;
//     //     static readonly int MAXAGE = 64;
//     //     static readonly float TERMINAL_VELOCITY = 2f;
//     //     // end tuning
//     //     static readonly float2 res = new float2(3000f / 256f, 3000f/ 256f);

//     //     static readonly float2 left = new float2(-1, 0);
//     //     static readonly float2 right = new float2(1, 0);
//     //     static readonly float2 up = new float2(0, 1);
//     //     static readonly float2 down = new float2(0, -1);
//     //     private static readonly float2 ZERO2 = new float2(0);
//     //     private static readonly bool2 TRUE2 = new bool2(true);

//     //     // gauss sigma 1
//     //     static readonly int KERNELSIZE = 3;
//     //     static readonly float[] KERNEL = new float[] {0.10376260474744907f, 0.17107561355036938f, 0.10376260474744907f};
//     //     // static readonly float[] KERNEL = new float[] { 0.274068619061197f, 0.45186276187760605f, 0.274068619061197f };
//     //     // static readonly float[] KERNEL = new float[] { 0.054488684549642945f, 0.24420134200323337f, 0.4026199468942475f, 0.24420134200323337f, 0.054488684549642945f };
//     //     // gauss sigma 1.5
//     //     // static readonly float[] KERNEL = new float[] { 0.30780132912347f, 0.38439734175306006f, 0.30780132912347f };
//     //     // static readonly float[] KERNEL = new float[] { 0.12007838424321349f, 0.23388075658535032f, 0.29208171834287244f, 0.23388075658535032f, 0.12007838424321349f };

//     //     public BeyerParticle(int2 pos, bool dead){
//     //         this.pos = new float2(pos);
//     //         this.dir = new float2(0f, 0f);
//     //         this.vel = 0f;
//     //         this.water = 1f;
//     //         this.sediment = 0f;
//     //         this.isDead = dead;
//     //         this.age = 0;
//     //     }

//     //     public BeyerParticle(int2 pos, float water): this(pos, false){
//     //         this.water = water;
//     //     }

//     //     public bool Equals(BeyerParticle other){
//     //         // TODO use lazy comparer for the floats
//     //         if (!(pos == other.pos).Equals(TRUE2)){
//     //             return false;
//     //         }
//     //         if (!(vel == other.vel)){
//     //             return false;
//     //         }
//     //         return (water == other.water && sediment == other.sediment);
//     //     }

//     //     public void Reset(int2 pos){
//     //         this.pos = new float2(pos);
//     //         // Debug.Log($"reset -> {this.pos.x}, {this.pos.y}");
//     //         this.dir = new float2(0f, 0f);
//     //         this.vel = 0f;
//     //         this.water = 1f;
//     //         this.sediment = 0f;
//     //         this.isDead = false;
//     //         this.age = 0;
//     //     }

//     //     public float2 nextPos(ref WorldTile tile) {
//     //         float h = tile.WIH(pos);
            
//     //         float3 a = cross(new float3(0, h - tile.WIH(pos + up), res.y), new float3(res.x, h - tile.WIH(pos + right), 0));
//     //         float3 b = cross(new float3(0, h - tile.WIH(pos + down), -res.y), new float3(-res.x, h - tile.WIH(pos + left), 0));
//     //         float3 norm = new float3(a.x + b.x, a.y + b.y, a.z + b.z);
//     //         float2 g = new float2(-norm.x, -norm.z);
//     //         float3 _norm = normalize(norm);
//     //         float2 gnorm = normalize(g);
//     //         if(vel > 0.001f){
//     //             dir = normalize((dir * (INERTIA * _norm.y / 1)) + (gnorm * ( 1f - (INERTIA * _norm.y / 1))));
//     //         }else{
//     //             dir = gnorm;
//     //         }
//     //         return pos + dir;
//     //     }

//     //     public void DoDescent(ref WorldTile tile){
//     //         int steps = 0;
//     //         while(steps < MAXAGE && Descend(ref tile)){
//     //             steps += 1;
//     //         }
//     //     }

//     //     public void UpdateTrack(ref WorldTile tile){
//     //         int idx = tile.getIdx(pos);
//     //         float trackVolume = tile.track[idx];
//     //         trackVolume += water;
//     //         tile.track[idx] = trackVolume;
//     //     }

//     //     public bool DescendSimultaneous(ref WorldTile tile, out ErosiveEvent evt){
//     //         int idx = tile.getIdx(pos);
//     //         evt = idx;
//     //         if(age >= MAXAGE){
//     //             isDead = true;
//     //             evt.deltaPoolMap = water / WorldTile.HEIGHT;
//     //             evt.deltaSediment = sediment;
//     //             return false;
//     //         }
//     //         if(tile.StandingWater(pos)){
//     //             vel *= 0.5f;
//     //             // evt.deltaPoolMap = water / WorldTile.HEIGHT;
//     //             // evt.deltaSediment = sediment;
//     //             // isDead = true;
//     //             // return false;
//     //         }
//     //         if(water < .001f){
//     //             isDead = true;
//     //             // evt.deltaPoolMap = water / WorldTile.HEIGHT;
//     //             evt.deltaSediment = sediment;
//     //             return false;
//     //         }
//     //         age++;
//     //         float2 posN = nextPos(ref tile);
//     //         if (posN.Equals(pos)) {
//     //             isDead = true;
//     //             return false;
//     //         } // did not descend
//     //         if (tile.OutOfBounds(posN)) {
//     //             isDead = true;
//     //             return false;
//     //         } // went OOB
//     //         // UpdateTrack(ref tile);
//     //         float currentHeight = tile.WIH(pos);
//     //         float hDiff = tile.WIH(posN) - tile.WIH(pos); // in world Meters
//     //         float vDiff = abs(hDiff);
//     //         if(vel < .001f && vDiff <= .1f){
//     //             // Debug.Log("Too slow");
//     //             evt.deltaPoolMap = water / WorldTile.HEIGHT;
//     //             evt.deltaSediment = sediment;
//     //             isDead = true;
//     //             return false;
//     //         }else{
//     //             evt.deltaWaterTrack = water;
//     //         }
//     //         float depositionAmount = 0f;
//     //         float currentCapacity = 0f;
//     //         // going uphill
//     //         if(hDiff > 0 ) {
//     //             // deposit sediment
//     //             depositionAmount = min((DEPOSITION * sediment), 0.1f * vDiff);
//     //             if(depositionAmount < 0f) Debug.LogError("eroding on deposit?");
//     //         }else{ // downhill
//     //             currentCapacity = max(vDiff, MINSLOPE) * vel * water * CAPACITY;
//     //             if (sediment < currentCapacity){
//     //                 // erode
//     //                 depositionAmount = -1f * max(min(EROSION * (currentCapacity - sediment), vDiff), 0f);
//     //                 if(depositionAmount > 0f) Debug.LogError($"depositing on erosion? v:{vel}, {sediment}/{currentCapacity}");
//     //             }
//     //             else{
//     //                 // deposit
//     //                 depositionAmount = (DEPOSITION * (sediment - currentCapacity));
//     //                 // depositionAmount = min((DEPOSITION * (sediment - currentCapacity)), 0.1f * vDiff);
//     //                 if(depositionAmount < 0f) Debug.LogError("eroding on deposit?");
//     //             }
//     //         }
//     //         if(abs(depositionAmount) > 0.001f){
//     //             // DepositSediment(ref tile, pos, depositionAmount);
//     //             evt.deltaSediment = depositionAmount;
//     //             sediment -= depositionAmount;
//     //         }

//     //         float effectiveFriction = FRICTION * (1 - .2f * tile.flow[idx]);
//     //         vel = min(sqrt(max(0f, vel * vel + (-hDiff) * (GRAVITY - effectiveFriction))), TERMINAL_VELOCITY);

//     //         // vel = min(sqrt(max(0f, vel * vel + (-hDiff) * (GRAVITY - effectiveFriction))), 120f);
//     //         // TODO PUT BACK IN
//     //         water = water * (1 - EVAP);
//     //         pos = posN;
//     //         return true;
//     //     }

//     //     public bool Descend(ref WorldTile tile){
//     //         float2 posN = nextPos(ref tile);
//     //         int idx = tile.getIdx(pos);
//     //         if (posN.Equals(pos)) { 
//     //             return false;
//     //         } // did not descend
//     //         if (tile.OutOfBounds(posN)) {
//     //             return false;
//     //         } // went OOB
//     //         UpdateTrack(ref tile);
//     //         float currentHeight = tile.WIH(pos);
//     //         float hDiff = tile.WIH(posN) - tile.WIH(pos); // in world Meters
//     //         float vDiff = abs(hDiff);
//     //         float depositionAmount = 0f;
//     //         float currentCapacity = 0f;
//     //         // going uphill
//     //         if(hDiff > 0 ) {
//     //             // deposit sediment
//     //             depositionAmount = min((DEPOSITION * sediment), 0.1f * vDiff);
//     //             if(depositionAmount < 0f) Debug.LogError("eroding on deposit?");
//     //         }else{ // downhill
//     //             currentCapacity = max(vDiff, MINSLOPE) * vel * water * CAPACITY;
//     //             if (sediment < currentCapacity){
//     //                 // erode
//     //                 depositionAmount = -1f * max(EROSION * (currentCapacity - sediment), vDiff);
//     //                 if(depositionAmount >= 0f) Debug.LogError("depositing on erosion?");
//     //             }
//     //             else{
//     //                 // deposit
//     //                 depositionAmount = (DEPOSITION * (sediment - currentCapacity));
//     //                 if(depositionAmount < 0f) Debug.LogError("eroding on deposit?");
//     //             }
//     //         }
//     //         if(abs(depositionAmount) > 0.05f){
//     //             float real = DepositSediment(ref tile, pos, depositionAmount);
//     //             sediment -= depositionAmount;
//     //         }

//     //         float effectiveFriction = FRICTION * (1 - tile.flow[idx]);
//     //         vel = min(sqrt(max(0f, vel * vel + (-hDiff) * (GRAVITY - effectiveFriction))), 120f);
//     //         // if(vel < .001f){
//     //         //     // Debug.Log("Too slow");
//     //         //     return false;
//     //         // }
//     //         water = water * (1 - EVAP);
//     //         pos = posN;
//     //         return true;
//     //     }

//     //     public float DepositSediment(ref WorldTile tile, float2 posD, float val){
//     //         float amount = 0f;
//     //         // float factor = val > 0f ? DEPOSITION : EROSION;
//     //         float offset = floor((float) KERNELSIZE / 2f);
//     //         int idx = 0;
//     //         float kernelFactor = 1f;
//     //         float2 probe = new float2(0);
//     //         for (int x = 0; x < KERNELSIZE; x++){
//     //             for( int z = 0; z < KERNELSIZE; z++){
//     //                 // TODO generalize for other kernels?
//     //                 kernelFactor = x == 1 && z == 1 ? 0.25f : 0.75f / 8f;
//     //                 probe.x = posD.x - offset + (float) x;
//     //                 probe.y = posD.y - offset + (float) z;
//     //                 idx = tile.SafeIdx(probe);
//     //                 float last = tile.height[idx];
//     //                 // float newDiff =  ((factor * val * kernelFactor) / WorldTile.HEIGHT);
//     //                 float newDiff =  ((val * kernelFactor) / WorldTile.HEIGHT);
//     //                 // Debug.Log($"old: {last} new: {last + newDiff}: {factor}, {val}, {kernelFactor}");
//     //                 float nextV = last + newDiff;
//     //                 if(nextV > 1f){continue;} // bad build breaker
//     //                 if(nextV < 0f){continue;} // bad build breaker
//     //                 tile.height[idx] = last + newDiff;
//     //                 amount += newDiff;
//     //             }
//     //         }
//     //         return amount;
//     //     }

//     // }

//     public struct Particle : IEquatable<Particle> {
        
//         // Parameters
//         public static readonly float density = 1.0f;  //This gives varying amounts of inertia and stuff...
//         // original
//         // public static readonly float evapRate = 0.001f;
//         public static readonly float evapRate = 0.0001f;
        
//         public static readonly float depositionRate = 1.2f*0.08f; // original
//         // public static readonly float depositionRate = 0.5f*0.08f;
//         public static readonly float minVol = 0.01f;
        
//         // public static readonly float friction = 0.25f; // original
//         // public static readonly float friction = 0.025f;
//         public static readonly float friction = 0.25f;
//         // public static readonly float volumeFactor = 0.5f; // original
//         public static readonly float volumeFactor = 2f;
//         private static readonly bool2 TRUE2 = new bool2(true, true);

//         // public static readonly float MINIMUM_MOMENTUM = 1E-5f;
//         public static readonly float MINIMUM_MOMENTUM = 5E-4f;
        
//         // Fields
//         public int2 pos;
//         public float2 speed;
//         public float volume; // = 1f;
//         public float sediment; // = 0f;
//         public int spill;
//         public bool isDead;
//         public int age;

//         public bool Equals(Particle other){
//             // TODO use lazy comparer for the floats
//             if (!(pos == other.pos).Equals(TRUE2)){
//                 return false;
//             }
//             if (!(speed == other.speed).Equals(TRUE2)){
//                 return false;
//             }
//             return (volume == other.volume && sediment == other.sediment);
//         }
        
//         public void Reset(int2 pos){
//             this.pos = pos;
//             this.speed = new float2(0f, 0f);
//             this.volume = 1f;
//             this.sediment = 0;
//             this.spill = 10;
//             isDead = false;
//             age = 0;
//             // Debug.Log($"particle reset to {pos.x}, {pos.y}");
//         }

//         public void SetPosition(int x, int y){
//             pos.x = x;
//             pos.y = y;
//         }

//         public int2 GetPosition(){
//             return pos;
//         }

//         public bool DescentComplete(ref WorldTile tile, out ErosiveEvent evt){
//             int idx = tile.getIdx(pos);
//             evt = idx;
//             if (volume < minVol) return true;
//             evt.deltaWaterTrack = volume;
//             float3 norm = tile.Normal(pos);
//             float2 horiz = new float2(norm.x, norm.z);
//             // float effF = friction * (1f - tile.flow[idx]);
//             float effF = friction * (1f - .05f * tile.flow[idx]);
//             if(length(horiz *effF) < MINIMUM_MOMENTUM){
//                 // Should this write to the pool map? Not sure how it wouldn't
//                 Debug.Log($"new pool at {idx} ?");
//                 evt.deltaPoolMap = volume;
//                 return true;
//             }
//             speed = lerp(horiz, speed, effF);
//             speed = sqrt(2.0f) * normalize(speed);
//             pos.x += (int) speed.x;
//             pos.y += (int) speed.y;
//             if(tile.OutOfBounds(pos)){
//                 // TODO transfer to other tile queue
//                 Debug.Log("particle out of bounds");
//                 volume = 0f;
//                 return true;
//             }
//             int nextIdx = tile.getIdx(pos);
//             if(tile.StandingWater(pos)){
//                 evt.idx = nextIdx;
//                 evt.deltaPoolMap = volume;
//                 return true;
//             };
//             //Mass-Transfer (in MASS)
//             // effD(plantDensity[pos]) -> local erosion strength (based on plant density) ***Can ignore? / Punt?***
//             float effD = depositionRate * 1f;
//             // float effD = depositionRate * 0.5f;
//             float c_eq = max(0f, tile.height[idx] - tile.height[nextIdx]);
//             float cdiff = c_eq - sediment;
//             sediment += effD * cdiff;
//             evt.deltaSediment = -effD * cdiff;

//             //Evaporate (Mass Conservative)
//             float effR = evapRate * (1f - 0.2f * tile.flow[idx]);
//             sediment /= (1.0f - effR);
//             volume *= (1.0f - effR);
//             // Debug.Log($"pstate {pos.x}, {pos.y}, S:{sediment}, V:{volume} @{effR}");
//             return false;
//         }
//         public bool DescentCompleteSingle(ref WorldTile tile, out ErosiveEvent evt){
//             int idx = tile.getIdx(pos);
//             evt = idx;
//             if (volume < minVol) return true;
            
//             float trackVolume = tile.track[idx];
//             trackVolume += volume;
//             tile.track[idx] = trackVolume;

//             float3 norm = tile.Normal(pos);
//             float2 horiz = new float2(norm.x, norm.z);
//             // float effF = friction * (1f - tile.flow[idx]);
//             float effF = friction * (1f - tile.flow[idx]);
//             if(length(horiz *effF) < MINIMUM_MOMENTUM){
//                 return true;
//             }
//             speed = lerp(horiz, speed, effF);
//             speed = sqrt(2.0f) * normalize(speed);
//             pos.x += (int) round(speed.x);
//             pos.y += (int) round(speed.y);

//             // Out-Of-Bounds
//             if(tile.OutOfBounds(pos)){
//                 // TODO transfer to other tile queue
//                 // Debug.Log("particle out of bounds");
//                 volume = 0f;
//                 return true;
//             }
//             int nextIdx = tile.getIdx(pos);
            
//             // Particle is in Pool
//             if(tile.StandingWater(pos)){
//                 return true;
//             };

//             //Mass-Transfer (in MASS)
//             // effD(plantDensity[pos]) -> local erosion strength (based on plant density) ***Can ignore? / Punt?***
//             float effD = depositionRate * 1f;
//             // float effD = depositionRate * 0.01f;
//             float c_eq = max(0f, tile.height[idx] - tile.height[nextIdx]);
//             float cdiff = c_eq - sediment;
//             float heightChange = effD * cdiff;
//             sediment += heightChange;
//             float currentHeight = tile.height[idx];
//             currentHeight -= effD * cdiff;
//             tile.height[idx] = currentHeight;

//             //Evaporate (Mass Conservative)
//             float effR = evapRate * (1f - 0.2f * tile.flow[idx]);
//             sediment /= (1.0f - effR);
//             volume *= (1.0f - effR);
//             Cascade(pos, ref tile);
//             age++;
//             // Debug.Log($"pstate {pos.x}, {pos.y}, S:{sediment}, V:{volume} @{age}");
//             return false;
//         }
//         void Cascade(int2 pos, ref WorldTile tile){
//             int idx = tile.getIdx(pos);
//             if(tile.pool[idx] > 0f) return;
//             int idxn = 0;
//             int2 n = new int2();
//             float diff = 0f;
//             float transfer = 0f;
//             float value = 0f;
//             float excess = 0f;
//             for(int i = 0; i < 8; i ++){
//                 n.x = pos.x + WorldTile.normX[i];
//                 n.y = pos.y + WorldTile.normY[i];
//                 if (tile.OutOfBounds(n)) continue;
//                 idxn = tile.getIdx(n);
//                 if(tile.pool[idxn] > 0f) continue;
//                 diff = tile.height[idx] - tile.height[idxn];
//                 if (diff == 0f) continue;
//                 excess = abs(diff) - WorldTile.maxdiff;
//                 if (excess <= 0f) continue;
//                 transfer = WorldTile.settling * excess / 2.0f;
//                 if (diff > 0f){
//                     value = tile.height[idx];
//                     value -= transfer;
//                     tile.height[idx] = value;
//                     value = tile.height[idxn];
//                     value += transfer;
//                     tile.height[idxn] = value;
//                 }
//                 else {
//                     value = tile.height[idxn];
//                     value -= transfer;
//                     tile.height[idxn] = value;
//                     value = tile.height[idx];
//                     value += transfer;
//                     tile.height[idx] = value;
//                 }
//             }
//         }
//     }

//     // public struct WorldTile {

        
//     //     public static readonly float HEIGHT = 3000f;
//     //     static readonly float SCALE = 256f * (3000f / 4000f);
//     //     // static readonly float SCALE = 80f;
//     //     // static readonly float SCALE = 0.1f;
//     //     public static readonly int2 ZERO2 = new int2(0);
//     //     public int2 res;
        
//     //     [NativeDisableContainerSafetyRestriction]
//     //     public NativeArray<float> height;
        
//     //     [NativeDisableContainerSafetyRestriction]
//     //     public NativeArray<float> pool;
        
//     //     [NativeDisableContainerSafetyRestriction]
//     //     public NativeArray<float> flow;

//     //     [NativeDisableContainerSafetyRestriction]
//     //     public NativeArray<float> track;

//     //     public static readonly int2 left = new int2(-1, 0);
//     //     public static readonly int2 right = new int2(1, 0);
//     //     public static readonly int2 up = new int2(0, 1);
//     //     public static readonly int2 down = new int2(0, -1);
//     //     static readonly int2 ne = new int2(1, 1);
//     //     static readonly int2 nw = new int2(-1, 1);
//     //     static readonly int2 sw = new int2(-1, -1);
//     //     static readonly int2 se = new int2(1, -1);

//     //     static public readonly int[] nx =  new int[] {-1,-1,-1, 0, 0, 1, 1, 1};
//     //     static public readonly int[] ny =  new int[] {-1, 0, 1,-1, 1,-1, 0, 1};

//     //     static public readonly int[] normX =  new int[] {-1,-1,-1, 0, 0, 1, 1, 1};
//     //     static public readonly int[] normY =  new int[] {-1, 0, 1,-1, 1,-1, 0, 1};

//     //     // static public readonly float maxdiff = 0.01f;  // maximum diff under which no modification will be made in either direction
//     //     // static public readonly float maxdiff = 0.00005f; 
//     //     static public readonly float MINFLOWPOOL = .00005f;
//     //     static public readonly float maxdiff = 0.0005f; 
//     //     // static public readonly float settling = 0.1f;
//     //     static public readonly float settling = 0.1f;
//     //     // static readonly float lRate = 0.01f;
//     //     static readonly float lRate = 0.05f;

//     //     public float3 Normal(int2 pos){
//     //         // returns normal of the (WIH)
            
//     //         float3 n = Normal(pos + nw, right, down);
//     //         n += Normal(pos + ne, down, left);
//     //         n += Normal(pos + se, left, up);
//     //         n += Normal(pos + sw, up, right);
//     //         n += Normal(pos, left, up);
//     //         n += Normal(pos, up, right);
//     //         n += Normal(pos, right, down);
//     //         n += Normal(pos, down, left);
//     //         // n += Normal(pos + up, se, sw);
//     //         // n += Normal(pos + down, nw, ne);

//     //         return normalize(n);
//     //     }

//     //     public float3 Normal(int2 pos, int2 a, int2 b){
//     //         return cross(
//     //             HiIVec(pos, 0, 0) - HiIVec(pos, a.x, a.y),
//     //             HiIVec(pos, 0, 0) - HiIVec(pos, b.x, b.y)
//     //         );
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public bool StandingWater(int x, int z){
//     //         return pool[SafeIdx(x, z)] > 0f;
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public bool StandingWater(int2 pos){
//     //         return pool[SafeIdx(pos)] > 0f;
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public bool StandingWater(float2 pos){
//     //         return StandingWater(new int2(pos));
//     //     }

//     //     public float3 diff(float x, float z, int2 pos, int2 dir){
//     //         return new float3(x, SCALE * (WIH(pos + dir) - WIH(pos)), z);
//     //     }

//     //     // public float WIH(int idx){
//     //     //     return height[idx] + pool[idx];
//     //     // }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float WIV(int idx){
//     //         // return SCALE * (height[idx] + pool[idx]);
//     //         return height[idx] + pool[idx];
//     //     }
        
//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float WIH(int idx){
//     //         // return SCALE * (height[idx] + pool[idx]);
//     //         return HEIGHT * (height[idx] + pool[idx]);
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float WIH(int2 pos){
//     //         return WIH(SafeIdx(pos));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float WIH(float2 pos){
//     //         return WIH(SafeIdx(pos));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float WIH(int x, int z){
//     //         return WIH(getIdx(x, z));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float WIH(int2 pos, int dx, int dz){
//     //         return WIH(SafeIdx(pos.x + dx, pos.y + dz));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public float3 HiIVec(int2 pos, int dx, int dz){
//     //         return new float3(dx, WIH(pos, dx, dz), dz);
//     //     }
        
//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int SafeIdx(int x, int z){
//     //         return getIdx(
//     //             clamp(x, 0, res.x - 1),
//     //             clamp(z, 0, res.y - 1));
//     //     }        
        
//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int SafeIdx(int2 pos){
//     //         return getIdx(clamp(pos, ZERO2, res - 1));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int SafeIdx(float2 pos){
//     //         return SafeIdx(new int2(pos));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int getIdx(int x, int z){
//     //         return x * res.x + z;
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int getIdx(int2 pos){
//     //         return getIdx(pos.x, pos.y);
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int getIdx(float2 pos){
//     //         return getIdx((int) pos.x, (int) pos.y);
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public int2 getPos(int idx){
//     //         return new int2((idx / res.x), (idx % res.x));
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public bool OutOfBounds(int2 pos){
//     //         if(pos.x < 0 || pos.y < 0 || pos.x >= res.x || pos.y >= res.y) return true;
//     //         return false;
//     //     }

//     //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     //     public bool OutOfBounds(float2 pos){
//     //         return OutOfBounds(new int2(pos));
//     //     }

//     //     public void UpdateFlowMapFromTrack(int x, int z){
//     //         int i = getIdx(x, z);
//     //         float pv = flow[i];
//     //         float tv = track[i];
//     //         float poolV = pool[i];
//     //         if(poolV > MINFLOWPOOL){
//     //             flow[i] = ((1f - 0.1f * lRate) * pv);
//     //         }
//     //         else if(tv > 0f){
//     //             flow[i] = ((1f - lRate) * pv) + (lRate * 50.0f * tv) / (1f + 50.0f* tv);
//     //         }else{
//     //             if(pv < .005f){
//     //                 flow[i] = .25f * pv;
//     //             }else{
//     //                 flow[i] = (1f - lRate) * pv;
//     //             }
//     //         }
//     //         track[i] = 0f;
//     //         // Evaporation rate of pools
//     //         // pool[i] = max(poolV - 0.00001f, 0f);
//     //     }

//     //     public void CascadeHeightMapChange(int idx){
//     //         if(pool[idx] > 0f) return;
//     //         int idxn = 0;
//     //         int2 pos = getPos(idx);
//     //         int2 n = new int2();
//     //         float diff = 0f;
//     //         float transfer = 0f;
//     //         float value = 0f;
//     //         float excess = 0f;
//     //         for(int i = 0; i < 8; i ++){
//     //             n.x = pos.x + nx[i];
//     //             n.y = pos.y + ny[i];
//     //             if (OutOfBounds(n)) continue;
//     //             idxn = getIdx(n);
//     //             if(pool[idxn] > 0f) continue;
//     //             diff = height[idx] - height[idxn];
//     //             if (diff == 0f) continue;
//     //             excess = abs(diff) - WorldTile.maxdiff;
//     //             if (excess <= 0f) continue;
//     //             transfer = WorldTile.settling * excess / 2.0f;
//     //             if (diff > 0f){
//     //                 value = height[idx];
//     //                 value -= transfer;
//     //                 height[idx] = value;
//     //                 value = height[idxn];
//     //                 value += transfer;
//     //                 height[idxn] = value;
//     //             }
//     //             else {
//     //                 value = height[idxn];
//     //                 value -= transfer;
//     //                 height[idxn] = value;
//     //                 value = height[idx];
//     //                 value += transfer;
//     //                 height[idx] = value;
//     //             }
//     //         }
//     //     }

//     //     private void LowBound(
//     //         int2 pos,
//     //         ref int2 drain,
//     //         ref float plane
//     //     ){
//     //         if(pos.x < 0 | pos.y < 0 | pos.x >= res.x | pos.y >= res.y) return;
            
//     //         int idx = getIdx(pos);

//     //         if(pool[idx] == 0f) return;

//     //         //Below Drain Height
//     //         if(height[pos.x * res.y + drain.y] + pool[pos.x * res.y + drain.y] < height[drain.x * res.y + drain.y] + pool[drain.x * res.y + drain.y]){
//     //             return;
//     //         }
//     //         //Higher than Plane (we want lower)
//     //         if(height[pos.x*res.y+pos.y] + pool[pos.x*res.y+pos.y] >= plane){
//     //             return;
//     //         }

//     //         plane = height[pos.x * res.y + pos.y] + pool[pos.x * res.y + pos.y];                
//     //     }

//     //     private bool FindSet(
//     //         int2 pos,
//     //         float plane,
//     //         ref NativeArray<bool> tried,
//     //         ref NativeParallelHashMap<int, float> boundary,
//     //         ref NativeList<int2> floodSet,
//     //         ref bool drainFound,
//     //         ref int2 drain
//     //     ){
//     //         if(pos.x < 0 | pos.y < 0 | pos.x >= res.x | pos.y >= res.y) return true;
            
//     //         int idx = getIdx(pos);
            
//     //         if(tried[idx]) return true;
//     //         tried[idx] = true;

//     //         //Wall / Boundary
//     //         if((height[idx] + pool[idx]) > plane){
//     //             boundary[idx] = height[idx] + pool[idx];
//     //             return true;
//     //         }

//     //         //Drainage Point
//     //         if((height[idx] + pool[idx]) < plane){
//     //             //No Drain yet
//     //             if(!drainFound){
//     //                 drain = pos;
//     //             }
//     //             //Lower Drain
//     //             else if(pool[idx] + height[idx] < pool[drain.x * res.y+drain.y] + height[drain.x * res.y * drain.y]){
//     //                 drain = pos;
//     //             }
                    
//     //             drainFound = true;
//     //             return false;
//     //         }

//     //         floodSet.Add(pos);

//     //         if(!FindSet(pos + right, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos - right, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos + up, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos - up, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos + ne, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos - ne, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos + nw, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;
//     //         if(!FindSet(pos - nw, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)) return false;

//     //         return true;
//     //     }

//     //     public bool Flood(ref Particle part){
//     //         if(part.volume < Particle.minVol || part.spill-- <= 0)
//     //         {
//     //             // Debug.Log("no more spills / out of volume");
//     //             return false;
//     //         }
//     //         int2 pos = part.pos;
//     //         int idx = getIdx(pos);
//     //         int arrSize = res.x * res.y;
//     //         NativeArray<bool> tried = new NativeArray<bool>(arrSize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
//     //         for (int i = 0; i < arrSize; i++){
//     //             tried[i] = false;
//     //         }
//     //         NativeParallelHashMap<int, float> boundary = new NativeParallelHashMap<int, float>(arrSize, Allocator.Temp);
//     //         NativeList<int2> floodSet = new NativeList<int2>(arrSize, Allocator.Temp);

//     //         bool drainFound = false;
//     //         int2 drain = new int2();
//     //         float plane = height[idx] + pool[idx];

//     //         int mbi = idx;
//     //         float mbv = plane;
//     //         int c = 0;
//     //         while(part.volume > Particle.minVol && FindSet(pos, plane, ref tried, ref boundary, ref floodSet, ref drainFound, ref drain)){

//     //             //Find the Lowest Element on the Boundary
//     //             var boundIter = boundary.GetEnumerator();
//     //             boundIter.MoveNext();
//     //             var minbound = boundIter.Current;
//     //             mbi = minbound.Key;
//     //             mbv = minbound.Value;
//     //             while(boundIter.MoveNext()){
//     //                 if(boundIter.Current.Value < mbv){
//     //                     mbv = boundIter.Current.Value;
//     //                     mbi = boundIter.Current.Key;
//     //                 }
//     //             }

//     //             //Compute the Height of our Volume over the Set
//     //             float vheight = part.volume*Particle.volumeFactor/(float)floodSet.Length;

//     //             //Not High Enough: Fill 'er up
//     //             if(plane + vheight < mbv){
//     //                 plane += vheight;
//     //             }
//     //             else{
//     //                 part.volume -= (mbv - plane)/Particle.volumeFactor*(float)floodSet.Length;
//     //                 plane = mbv;
//     //             }
//     //             for(int i = 0; i < floodSet.Length; i ++){
//     //                 int idxs = getIdx(floodSet[i]);
//     //                 pool[idxs] = plane - height[idxs];
//     //             }
                
//     //             boundary.Remove(mbi);
//     //             tried[mbi] = false;
//     //             pos.x = mbi / res.y;
//     //             pos.y = mbi % res.y;
//     //             c++;
//     //         }
//     //         if(drainFound){
//     //             LowBound(drain + right, ref drain, ref plane);
//     //             LowBound(drain - right, ref drain, ref plane);
//     //             LowBound(drain + up, ref drain, ref plane);
//     //             LowBound(drain - up, ref drain, ref plane);
                
//     //             LowBound(drain + ne, ref drain, ref plane);
//     //             LowBound(drain - ne, ref drain, ref plane);
//     //             LowBound(drain + nw, ref drain, ref plane);
//     //             LowBound(drain - nw, ref drain, ref plane);

//     //             for(int i = 0; i < floodSet.Length; i ++){
//     //                 int idxs = getIdx(floodSet[i]);
//     //                 pool[idxs] = (plane > height[idxs]) ? (plane - height[idxs]) : 0f;
//     //             }
//     //             part.sediment /= (float)floodSet.Length; //Distribute Sediment in Pool
//     //             part.pos = drain;
//     //             // Debug.Log("drain found");
//     //             return true;
//     //         }
//     //         // Debug.Log($"no drain found after {c} rounds");
//     //         return false;
//     //     }

//     //     public void SpreadPool(
//     //         int x,
//     //         int z,
//     //         ref NativeArray<FloodedNeighbor> buff,
//     //         ref NativeQueue<BeyerParticle>.ParallelWriter particleQueue
//     //     ){
//     //         int idx = getIdx(x, z);
//     //         float hLand = height[idx];
//     //         float hWater = pool[idx];
//     //         // Debug.Log($"dist >> {hWater}");
//     //         float tHeight = hLand + hWater;
//     //         if(x == 0 || z == 0 || x == res.x - 1 || z == res.y - 1){
//     //             buff[0] = new FloodedNeighbor(SafeIdx(up.x + x, up.y + z), ref this);
//     //             buff[1] = new FloodedNeighbor(SafeIdx(right.x + x, right.y + z), ref this);
//     //             buff[2] = new FloodedNeighbor(SafeIdx(down.x + x, down.y + z), ref this);
//     //             buff[3] = new FloodedNeighbor(SafeIdx(left.x + x, left.y + z), ref this);
                
//     //         }else{
//     //             buff[0] = new FloodedNeighbor(getIdx(up.x + x, up.y + z), ref this);
//     //             buff[1] = new FloodedNeighbor(getIdx(right.x + x, right.y + z), ref this);
//     //             buff[2] = new FloodedNeighbor(getIdx(down.x + x, down.y + z), ref this);
//     //             buff[3] = new FloodedNeighbor(getIdx(left.x + x, left.y + z), ref this);
//     //         }
//     //         buff.Sort<FloodedNeighbor>();
//     //         float fill = 0f;
//     //         float diffV = 0f;
//     //         for(int e = 0; e < 4; e++){
//     //             fill = 0f;
//     //             diffV = tHeight - buff[e].current; // if we moved the whole difference, they would just swap
//     //             if( buff[e].water <= 0f && hLand >= buff[e].height ){
//     //                 // Found Drain!
//     //                 // Debug.Log("found drain!");
//     //                 fill = 0.95f * hWater;
//     //                 particleQueue.Enqueue(
//     //                     new BeyerParticle(
//     //                         getPos(buff[e].idx),
//     //                         fill + 1f
//     //                     )
//     //                 );
//     //                 hWater -= fill;
//     //                 tHeight = hLand + hWater;
//     //             }else if(diffV > 0f){
//     //                 if(hWater <= 0f){
//     //                     continue;
//     //                 }
//     //                 fill = min(0.25f * hWater, 0.25f * diffV);
//     //                 hWater -= fill;
//     //                 tHeight = hLand + hWater;
//     //                 buff[e].Commit(fill, ref this);
//     //             }else if (diffV < 0f){
//     //                 if(buff[e].water <= 0f){
//     //                     continue;
//     //                 }
//     //                 fill = min(0.25f * buff[e].water, -0.25f * diffV);
//     //                 hWater += fill;
//     //                 tHeight = hLand + hWater;
//     //                 buff[e].Commit(-1f * fill, ref this);
//     //             }
//     //         }
//     //         pool[idx] = hWater;
//     //     }
//     // }

//     // // Flooding Helper
//     // public struct FloodedNeighbor: IComparable<FloodedNeighbor>, IEquatable<FloodedNeighbor> {
        
//     //     public int idx;
//     //     public float height;
//     //     public float water;
//     //     public float current {
//     //         get { return height + water;}
//     //         private set {}
//     //     }

//     //     public FloodedNeighbor(int idx, ref WorldTile tile){
//     //         this.idx = idx;
//     //         this.height = tile.height[this.idx];
//     //         this.water = tile.pool[this.idx];
//     //     }

//     //     public void Commit(float val, ref WorldTile tile){
//     //         tile.pool[idx] = water + val;

//     //     }

//     //     public int CompareTo(FloodedNeighbor obj){
//     //         if (obj.Equals(this)){ return 0;}
//     //         return GetHashCode() > obj.GetHashCode() ? 1 : -1;
//     //     }

//     //     public override int GetHashCode(){
//     //         return current.GetHashCode();
//     //     }

//     //     public bool Equals(FloodedNeighbor other){
//     //         if (idx != other.idx){
//     //             return false;
//     //         }
//     //         return true;
//     //     }
//     // }

//     public static class CardinalExtension {

//         // Stuck this in here to see if we could predict catchment borders using hamming weight
//         // or orientation of cardinals. 

//         public static readonly int[] NIBBLE_LU = new int[] {
//             0, 1, 1, 2, 1, 2, 2, 3, 
//             1, 2, 2, 3, 2, 3, 3, 4
//         };

//         public static readonly Cardinal WE = (Cardinal.E | Cardinal.W);
//         public static readonly Cardinal NS = Cardinal.N | Cardinal.S;
//         public static readonly Cardinal D1 = Cardinal.NE | Cardinal.SW;
//         public static readonly Cardinal D2 = Cardinal.NW | Cardinal.SE;
        
//         public static int HammingW(this Cardinal b){
//             return NIBBLE_LU[((int) b) & 0x0F] + NIBBLE_LU[((int) b) >> 4];
//         }

//         public static bool StraightLine(this Cardinal b){
//             return ((Cardinal)(b | WE)) == b || (((Cardinal) b | NS) == b);
//         }

//         public static bool Diagonal(this Cardinal b){
//             return ((Cardinal)(b | D1)) == b || (((Cardinal) b | D2) == b);
//         }

//     }
    

//     public enum Cardinal : byte {
//         // Cardinal[i] where i < 8 === (Cardinal)( ( ((byte) 1) << i ) )
        
//         // cardinals
//         NW = 0b_00000001,
//         SE = 0b_00000010,
//         N  = 0b_00000100,
//         S  = 0b_00001000,
//         NE = 0b_00010000,
//         SW = 0b_00100000,
//         E  = 0b_01000000,
//         W  = 0b_10000000,
//         // convenience
//         X  = 0b_00000000,
//         A  = 0b_11111111

//     }

// }