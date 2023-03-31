// using Unity.Collections.LowLevel.Unsafe;
// using Unity.Collections;

// using UnityEngine;

// namespace xshazwar.noize.geologic {

//     public struct ParticleDebugHelper{

//         public static readonly int POOLSEARCHDEPTHI = 32;
//         public static readonly byte POOLSEARCHDEPTHB = 32;

//         public void Debug__PaintStaticBoundaries(
//             ref NativeSlice<float> outMap,
//             ref NativeParallelMultiHashMap<int, int> boundary_MB,
//             float value
//         ){
//             int v = 0;
//             var vals = boundary_MB.GetEnumerator();
//             while(vals.MoveNext()){
//                 v = vals.Current.Value;
//                 outMap[v] = value;
//             } 
//         }

//         public void Debug__PaintMinimas(
//             ref NativeSlice<float> outMap,
//             ref NativeArray<int> minimas,
//             float value
//         ){
//             for(int i = 0; i < minimas.Length; i++){
//                 outMap[minimas[i]] = value;
//             }
//         }

//         public void Debug__PaintPools(
//             ref NativeSlice<float> outMap,
//             ref NativeSlice<float> heightMap,
//             ref NativeParallelHashMap<PoolKey, Pool> pools,
//             NativeParallelHashMap<int, int>.Enumerator catchmentMap
//         ){
//             PoolKey key = new PoolKey();
//             PoolKey debugKey = new PoolKey();
//             Pool pool;
//             float height = 0f;
//             int tryCount = 0;
//             while(catchmentMap.MoveNext()){
//                 key.idx = catchmentMap.Current.Value;
//                 key.order = 1;
//                 key.n = 0;
//                 if (!pools.ContainsKey(key)){
//                     continue;
//                 }
//                 tryCount = 0;
//                 do {
//                     if (!pools.TryGetValue(key, out pool)) break;
//                     debugKey = key;
//                     key = pool.supercededBy;
//                     tryCount++;
//                 }while(pool.supercededBy.idx != -1 && tryCount < POOLSEARCHDEPTHI);
//                 // if(tryCount == POOLSEARCHDEPTHI) Debug.Log($"{pool.indexMinima} {tryCount}");

//                 height = heightMap[catchmentMap.Current.Key];
//                 if (height < pool.drainHeight){
//                     // Debug.Log($"{pool.drainHeight} {tryCount}");
//                     // if(outMap[catchmentMap.Current.Key] > pool.drainHeight){
//                     //     Debug.LogWarning($"logical error! {pool.drainHeight} !< {outMap[catchmentMap.Current.Key]}");
//                     //     Debug.LogWarning($"{pool.indexMinima} -> {pool.indexDrain} debugKey: {debugKey.idx}:{debugKey.order}n{debugKey.n}, key: {key.idx}:{key.order}n{key.n}");
//                     //     Debug.LogWarning($"supercededBy {pool.supercededBy.idx}:{pool.supercededBy.order}n{pool.supercededBy.n}");
//                     //     continue;
//                     // }
//                     outMap[catchmentMap.Current.Key]  = pool.drainHeight;
//                 }
//             }
//             catchmentMap.Reset();
//         }

//         public void Debug__PaintPoolsStatic(
//             ref NativeSlice<float> outMap,
//             ref NativeSlice<float> heightMap,
//             ref NativeParallelHashMap<PoolKey, Pool> pools,
//             NativeParallelHashMap<int, int>.Enumerator catchmentMap
//         ){
//             PoolKey key = new PoolKey();
//             PoolKey debugKey = new PoolKey();
//             Pool pool;
//             float height = 0f;
//             int tryCount = 0;
//             while(catchmentMap.MoveNext()){
//                 key.idx = catchmentMap.Current.Value;
//                 key.order = 1;
//                 key.n = 0;
//                 if (!pools.ContainsKey(key)){
//                     continue;
//                 }
//                 tryCount = 0;
//                 do {
//                     if (!pools.TryGetValue(key, out pool)) break;
//                     debugKey = key;
//                     key = pool.supercededBy;
//                     tryCount++;
//                 }while(pool.supercededBy.idx != -1 && tryCount < POOLSEARCHDEPTHI);
//                 // if(tryCount == POOLSEARCHDEPTHI) Debug.Log($"{pool.indexMinima} {tryCount}");

//                 height = heightMap[catchmentMap.Current.Key];
//                 if (height < pool.drainHeight){
//                     // Debug.Log($"{pool.drainHeight} {tryCount}");
//                     if(outMap[catchmentMap.Current.Key] > pool.drainHeight){
//                         Debug.LogWarning($"logical error! {pool.drainHeight} !< {outMap[catchmentMap.Current.Key]}");
//                         Debug.LogWarning($"{pool.indexMinima} -> {pool.indexDrain} debugKey: {debugKey.idx}:{debugKey.order}n{debugKey.n}, key: {key.idx}:{key.order}n{key.n}");
//                         Debug.LogWarning($"supercededBy {pool.supercededBy.idx}:{pool.supercededBy.order}n{pool.supercededBy.n}");
//                         continue;
//                     }
//                     outMap[catchmentMap.Current.Key] =  .5f - (0.15f * ((float)key.order / 2));
//                 }
//             }
//             catchmentMap.Reset();
//         }

//         public void Debug__PaintBoundaries(
//             ref NativeSlice<float> outMap,
//             ref NativeSlice<float> heightMap,
//             ref NativeParallelHashMap<PoolKey, Pool> pools,
//             ref NativeParallelMultiHashMap<int, int> boundary_BM
//         ){
//             int boundaryIdx = 0;
//             float maxHeight = 0f;
//             int tryCount = 0;
//             PoolKey key = new PoolKey();
//             Pool pool;
//             var (bnd, bndSize) = NativeParallelHashMapExtensions.GetUniqueKeyArray(boundary_BM, Allocator.Temp);
//             NativeParallelMultiHashMap<int, int>.Enumerator minimaIter;
//             for( int i = 0; i < bndSize; i++){
//                 boundaryIdx = bnd[i];
//                 maxHeight = heightMap[boundaryIdx];
//                 minimaIter = boundary_BM.GetValuesForKey(boundaryIdx);
//                 while(minimaIter.MoveNext()){
//                     key.idx = minimaIter.Current;
//                     key.order = 1;
//                     key.n = 0;
//                     tryCount = 0;
//                     do{
//                         if (!pools.TryGetValue(key, out pool)) break;
//                         key = pool.supercededBy;
//                         tryCount++;
//                     }while(pool.supercededBy.idx != -1 && tryCount < POOLSEARCHDEPTHI);
//                     if(pool.drainHeight > maxHeight){
//                         // Debug.LogWarning($"{boundaryIdx} -> min {pool.indexMinima} | {pool.drainHeight} > {maxHeight}");
//                         maxHeight = pool.drainHeight;
//                         // Debug.LogWarning($">> {maxHeight}");
//                     }
//                 }
//                 outMap[boundaryIdx] = maxHeight;
//             }
//         }

//         public void Debug__PaintDrains(ref NativeSlice<float> outMap, ref NativeParallelHashMap<PoolKey, Pool> pools, float value){
//             var poolKeys = pools.GetKeyArray(Allocator.Temp);
//             poolKeys.Sort();
//             PoolKey k;
//             Pool pool;
//             for(int i = 0; i < poolKeys.Length; i++){
//                 k = poolKeys[i];
//                 pools.TryGetValue(k, out pool);
//                 outMap[pool.indexDrain] =  value;
//             }
//         }

        

//         public void Debug__InfoPools(ref NativeParallelHashMap<PoolKey, Pool> pools){
//             // var poolKeys = pools.GetKeyArray(Allocator.Temp);
//             // poolKeys.Sort();
//             // Pool pool;
//             // PoolKey key;
//             // for(int i = 0; i < poolKeys.Length; i++){
//             //     key = poolKeys[i];
//             //     pools.TryGetValue(key, out pool);
//             //     if(pool.supercededBy.idx != -1){
//             //         Debug.Log($"mn {pool.indexMinima} -> {pool.indexDrain}: @{key.order} >> {pool.supercededBy.idx}:{pool.supercededBy.order}:{pool.supercededBy.n}");
//             //     }else{
//             //         Debug.Log($"mn {pool.indexMinima} -> {pool.indexDrain}: @{key.order}. Top of food chain");
//             //     }
                
//             // }
//             Debug.LogWarning($"pools: {pools.Count()}");
//         }
//     }
// }