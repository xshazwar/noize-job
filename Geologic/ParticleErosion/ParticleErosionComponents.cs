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

/*
// 
//   POOLS
// 
*/

    struct FlowSuperPosition {
        public static readonly int2[] neighbors = new int2[] {
            // matches Cardinal convention
            // opposite d === (d * -1)
            new int2(-1,  1), // NW 
            new int2( 1, -1), // SE
            new int2( 0,  1), // N
            new int2( 0, -1), // S
            new int2( 1,  1), // NE
            new int2(-1, -1), // SW
            new int2( 1,  0), // E
            new int2(-1,  0), // W   
        };

        public static readonly int POOLSEARCHDEPTHI = 32;
        public static readonly byte POOLSEARCHDEPTHB = 32;
        public int2 res;
        
        [NativeDisableContainerSafetyRestriction]
        NativeArray<Cardinal> flow;
        
        [NativeDisableContainerSafetyRestriction]
        NativeSlice<float> heightMap;
        
        [NativeDisableContainerSafetyRestriction]
        NativeSlice<float> outMap;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int getIdx(int x, int z){
            if(x >= res.x || z >= res.y || x < 0 || z < 0){
                return -1;
            }
            return x * res.x + z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int getIdx(int2 pos){
            return getIdx(pos.x, pos.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int2 getPos(int idx){
            // int x = idx / res.x;
            // int z = idx % res.x;
            return new int2((idx / res.x), (idx % res.x));
        }

/*
// 
//   Frontier Finding
// 
*/

        public void CreateSuperPositions(int z, NativeStream.Writer minimaStream){
            // NativeStream.Writer minimaStream = minimaStream.AsWriter();
            minimaStream.BeginForEachIndex(z);
            int idxN = 0;
            float height = 0f;
            for(int x = 0; x < res.x; x++){
                int idx = getIdx(x, z);
                height = heightMap[idx];
                // just for visual debug
                outMap[idx] = height;
                Cardinal v = Cardinal.X;
                for(int d = 0; d < 8; d++){
                    // TODO make off the edge always show as a valid down.
                    idxN = getIdx(neighbors[d].x + x, neighbors[d].y + z);
                    if(idxN < 0){
                        v = v | (Cardinal)( ( ((byte)1) << d ) );
                    }
                    else if (height > heightMap[idxN]){
                        v = v | (Cardinal)( ( ((byte)1) << d ) );
                    }
                }
                if (v == Cardinal.X){
                    minimaStream.Write<int>(idx);
                }
                flow[idx] = v;
            }
            minimaStream.EndForEachIndex();
        }

        bool UpdateFrontier(
            int2 pos,
            ref UnsafeParallelHashMap<int, Cardinal> frontier,
            ref UnsafeParallelHashSet<int> basin
        ){
            bool foundNeighbor = false;
            int2 nKey;
            int idxN;
            int reciprocal = 1;
            int inverseIdx = 0;
            for(int d = 0; d < 8; d++){
                nKey = neighbors[d] + pos;
                idxN = getIdx(nKey.x, nKey.y);
                // TODO validate if off edge?
                if (idxN < 0){
                    // off the map
                }
                else if (!frontier.ContainsKey(idxN) && !basin.Contains(idxN)){
                    inverseIdx = d + reciprocal;
                    Cardinal mask = flow[idxN];
                    mask = PruneMask(inverseIdx, mask, ref foundNeighbor);
                    frontier.Add(idxN, mask);
                    // We'll update frontierIdx outside of the iterator
                }
                reciprocal *= -1;
            }
            return foundNeighbor;
        }

        bool CollapseFrontierState(
            int2 pos,
            ref UnsafeParallelHashMap<int, Cardinal> frontier,
            ref UnsafeParallelHashSet<int> basin)
        {
            bool collapsed = false;
            int2 nKey;
            int idxN;
            int reciprocal = 1;
            int inverseIdx = 0;
            for(int d = 0; d < 8; d++){
                nKey = neighbors[d] + pos;
                idxN = getIdx(nKey.x, nKey.y);
                if (idxN < 0){
                    // off the map
                }
                else if (frontier.ContainsKey(idxN) && !basin.Contains(idxN)){
                    inverseIdx = d + reciprocal;
                    // h -> n = neighbors[n] || Cardinal[n]
                    // n -> h = neighbors[n + reciprocal] || Cardinal[n + reciprocal]
                    // remove self from neighbors mask, indicate collapsed

                    frontier[idxN] = PruneMask(inverseIdx, frontier[idxN], ref collapsed);  
                }
                // the opposite direction flops between ahead and behind the current neighbor direction
                // [d == 0] NW ( + 1) === SE
                // [d == 1] SE ( - 1) === NW
                reciprocal *= -1;
            }
            return collapsed;
        }

        bool MoveToBasin(
            ref UnsafeParallelHashMap<int, Cardinal> frontier,
            ref UnsafeParallelHashSet<int> basin,
            ref UnsafeParallelHashSet<int>.Enumerator frontierIndices
        ){
            bool foundNewFrontier = false;
            while(frontierIndices.MoveNext()){
                int idxCandidate = frontierIndices.Current;
                if(frontier[idxCandidate] == Cardinal.X ){
                    if(basin.Add(idxCandidate)){
                        foundNewFrontier = UpdateFrontier(
                            getPos(idxCandidate),
                            ref frontier,
                            ref basin
                            ) || foundNewFrontier;
                        }
                }
            }
            return foundNewFrontier;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        Cardinal PruneMask(int directionIdx, Cardinal mask, ref bool changed){
            if ((mask & (Cardinal) ( (byte) 1 << directionIdx)) != 0){
                mask &= ~ (Cardinal) ( (byte) 1 << directionIdx);
                changed = true;
            }
            return mask;
        }

        public void UpdateFrontierReferences(
            ref UnsafeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator,
            ref UnsafeParallelHashSet<int> frontierIdx,
            ref UnsafeParallelHashSet<int> basin
        ){
            int idx = 0;
            int2 pos = new int2();
            int2 nKey= new int2();
            int idxN = 0;
            bool all_in_basin = true;

            while(frontierEnumerator.MoveNext()){
                idx = frontierEnumerator.Current.Key;
                if (!basin.Contains(idx)){
                    frontierIdx.Add(idx);
                }else{
                    pos = getPos(idx);
                    all_in_basin = true;
                    for(int d = 0; d < 8; d++){
                        nKey = neighbors[d] + pos;
                        idxN = getIdx(nKey.x, nKey.y);
                        if (!basin.Contains(idxN)){
                            all_in_basin = false;
                        }
                    }
                    if(all_in_basin){
                        frontierIdx.Remove(idx);
                    }
                }
            }
        }

        public void PruneFrontierMap(
            ref UnsafeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator,
            ref UnsafeParallelHashMap<int, Cardinal> frontier,
            ref UnsafeParallelHashSet<int> frontierIdx,
            ref UnsafeParallelHashSet<int> trash
        ){
            int idx = 0;
            frontierEnumerator.Reset(); 
            trash.Clear();
            while(frontierEnumerator.MoveNext()){
                idx = frontierEnumerator.Current.Key;
                if(!frontierIdx.Contains(idx)){
                    trash.Add(idx);
                }
            }
            var trashIter = trash.GetEnumerator();
            while(trashIter.MoveNext()){
                idx = trashIter.Current;
                frontier.Remove(idx);
            }
        }

        public void CollapseMinima(
            int minimaIdx,
            // boundary coincidence is annoying to calculate so we'll double link it
            NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterBM,
            NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterMB,
            NativeParallelHashMap<int, int>.ParallelWriter catchmentWriter,
            ProfilerMarker? profiler = null
        ){
            int2 pos = getPos(minimaIdx);
            // TODO see if we can optimize the size of these collections so they don't need to resize
            // The Safe versions of these are suffering from the second usage atomic safety issue in collections 1.3 so we're using unsafe
            UnsafeParallelHashSet<int> frontierIdx = new UnsafeParallelHashSet<int>((int)(res.x ), Allocator.Temp);
            UnsafeParallelHashSet<int> basin = new UnsafeParallelHashSet<int>((int)(res.x ), Allocator.Temp);
            UnsafeParallelHashSet<int> trash = new UnsafeParallelHashSet<int>((int)(res.x ), Allocator.Temp);
            UnsafeParallelHashMap<int, Cardinal> frontier = new UnsafeParallelHashMap<int, Cardinal>((int)(res.x ), Allocator.Temp);
        
            int iterCount = 0;
            int frontierIterCount = 0;
            bool basinComplete = false;
            // by definition the minima is in the basin.
            basin.Add(minimaIdx);
            // Our frontier starts with the adjacent values
            UpdateFrontier(pos, ref frontier, ref basin);
            UnsafeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator = frontier.GetEnumerator();
            UpdateFrontierReferences(ref frontierEnumerator, ref frontierIdx, ref basin);
            UnsafeParallelHashSet<int>.Enumerator frontierIdxIter = frontierIdx.GetEnumerator();
            while(!basinComplete){
                frontierIterCount = 0;
                profiler?.Begin();
                frontierIdxIter.Reset();
                bool collapsed = false;
                while(frontierIdxIter.MoveNext()){
                    frontierIterCount ++;
                    int idxCandidate = frontierIdxIter.Current;
                    collapsed = CollapseFrontierState(getPos(idxCandidate), ref frontier, ref basin) || collapsed;               
                }
                // Debug.Log($"{collapsed}");
                // Debug.Log($"{frontierIterCount}");
                frontierIdxIter.Reset();
                profiler?.End();
                profiler?.Begin();
                basinComplete = !MoveToBasin(ref frontier, ref basin, ref frontierIdxIter);
                profiler?.End();
                profiler?.Begin();
                frontierEnumerator.Reset();
                // DebugCollapseState(ref basin, ref frontier, ref frontierIdx, ref trash, (iterCount * 10) + 1);
                UpdateFrontierReferences(ref frontierEnumerator, ref frontierIdx, ref basin);
                // DebugCollapseState(ref basin, ref frontier, ref frontierIdx, ref trash, (iterCount * 10) + 2);
                PruneFrontierMap(ref frontierEnumerator, ref frontier, ref frontierIdx, ref trash);
                // DebugCollapseState(ref basin, ref frontier, ref frontierIdx, ref trash, (iterCount * 10) + 3);
                profiler?.End();
                iterCount ++;
            }

            frontierEnumerator.Reset();
            int probeIdx = 0;

            while(frontierEnumerator.MoveNext()){
                probeIdx = frontierEnumerator.Current.Key;
                if (basin.Contains(probeIdx)) continue;
                boundaryWriterBM.Add(probeIdx, minimaIdx);
                boundaryWriterMB.Add(minimaIdx, probeIdx);
            }
            UnsafeParallelHashSet<int>.Enumerator basinMembers = basin.GetEnumerator();
            while(basinMembers.MoveNext()){
                probeIdx = basinMembers.Current;
                catchmentWriter.TryAdd(probeIdx, minimaIdx);
            }
            frontier.Dispose();
            basin.Dispose();
            trash.Dispose();
            frontierIdx.Dispose();
        }
    
    public void DebugCollapseState(
        ref UnsafeParallelHashSet<int> basin,
        ref UnsafeParallelHashMap<int, Cardinal> frontier,
        ref UnsafeParallelHashSet<int> frontierIdx,
        ref UnsafeParallelHashSet<int> trash,
        int tag
    ){
        Debug.Log($"------- {tag}");
        Debug.Log($"basin {basin.Count()}");
        Debug.Log($"frontier {frontier.Count()}");
        Debug.Log($"frontierIdx {frontierIdx.Count()}");
        Debug.Log($"trash {trash.Count()}");
        

    }

/*
// 
//   Drain Finding
// 
*/

        public void AddUniquePoolValues(
            PoolKey key,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeList<int> minimas
        ){
            var minutiaIter = drainToMinima.GetValuesForKey(key);
            while(minutiaIter.MoveNext()){
                if (!NativeArrayExtensions.Contains<int, int>(minimas, minutiaIter.Current)){
                    minimas.Add(minutiaIter.Current);
                }
            }
        }

        public bool ReduceDrains(
            int minOrder,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeParallelHashMap<PoolKey, int> minimaToDrain,
            ref NativeParallelMultiHashMap<int, int> boundary_MB,
            ref NativeParallelMultiHashMap<int, int> boundary_BM,
            ref NativeList<int> sharedBoundary
        ){
            bool success = false;
            // tried to find higher order drains from existing shared drain
            NativeList<int> minimas = new NativeList<int>(32, Allocator.Temp);
            var (pks, pksSize) = NativeParallelHashMapExtensions.GetUniqueKeyArray(drainToMinima, Allocator.Temp);
            int i = 0;
            int p = 0;
            int next = 1;
            PoolKey refer = new PoolKey();
            PoolKey probe = new PoolKey();
            while(i < pksSize ){
                next = 1;
                p = i + 1;
                refer = pks[i];
                // Debug.Log($"Refer {refer.idx} -> order {refer.order} -> n{refer.n}");
                AddUniquePoolValues(refer, ref drainToMinima, ref minimas);
                while(p < pksSize){
                    probe = pks[p];
                    if(probe.idx != refer.idx){
                        break;
                    }
                    
                    // Debug.Log($"Refer drain [{refer.idx}] -> Probe drain {probe.idx} -> order {probe.order} -> n{probe.n}");
                    AddUniquePoolValues(probe, ref drainToMinima, ref minimas);
                    p += 1;
                    next += 1;
                }
                if (next > 1){
                    refer.order = (byte) minimas.Length;
                    refer.n = 0;
                    if(minimas.Length >= minOrder){
                        // Debug.LogWarning($"Rolled up drain: {refer.idx} -> order {refer.order} -> {refer.n} : new pool @ {minimas.Length}");
                        FindHigherOrderDrain(ref minimas, ref drainToMinima, ref minimaToDrain, ref boundary_MB, ref boundary_BM, ref sharedBoundary);
                        success = true;
                    }
                }
                i += next;
                minimas.Clear();
            }
            return success;
        }

        public void FindFirstOrderDrain(
            int minimaIdx,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeParallelHashMap<PoolKey, int> minimaToDrain,
            ref NativeParallelMultiHashMap<int, int> boundary_MB
        ){
            // Finds the drain in a single minima catchment
            // By definition this is a first order Pool
            float drainHeight = float.MaxValue;
            int drainIdx = 0;
            NativeParallelMultiHashMap<int, int>.Enumerator bIdx = boundary_MB.GetValuesForKey(minimaIdx);
            int probeIdx = 0;
            while(bIdx.MoveNext()){
                probeIdx = bIdx.Current;
                if (heightMap[probeIdx] < drainHeight){
                    drainHeight = heightMap[probeIdx];
                    drainIdx = probeIdx;
                }
            }
            PoolKey key = new PoolKey {idx = drainIdx, order = 1, n = 0};
            while(drainToMinima.ContainsKey(key)){
                key.n += 1;
            }
            drainToMinima.Add(key, minimaIdx);
            key.idx = minimaIdx;
            // drain -> minima is occupied, but the reverse is not
            key.n = 0;
            minimaToDrain.TryAdd(key, drainIdx);
        }

        public void FindHigherOrderDrain(
            ref NativeList<int> minimas,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeParallelHashMap<PoolKey, int> minimaToDrain,
            ref NativeParallelMultiHashMap<int, int> boundary_MB,
            ref NativeParallelMultiHashMap<int, int> boundary_BM,
            ref NativeList<int> sharedBoundary
        ){
            // Ignores the shared border between multiple catchments, and finds the lowest point on the remaining boundary
            int minimaIdx = 0;
            int probeIdx = 0;
            float drainHeight = float.MaxValue;
            int drainIdx = 0;
            
            PoolKey key = new PoolKey {
                idx = 0,
                order = (byte) minimas.Length,
                n = 0
            };  
            
            
            CollectSharedBoundary(minimas, ref boundary_MB, ref sharedBoundary);
            sharedBoundary.Sort();

            for(int i = 0; i < minimas.Length; i++){
                minimaIdx = minimas[i];
                NativeParallelMultiHashMap<int, int>.Enumerator bIdx = boundary_MB.GetValuesForKey(minimaIdx);
                while(bIdx.MoveNext()){
                    probeIdx = bIdx.Current;
                    if (heightMap[probeIdx] > drainHeight) continue;
                    if (NativeArrayExtensions.Contains<int, int>(sharedBoundary, probeIdx)) continue;
                    // TODO perform thinning on the boundary_BM keys so that we don't have
                    // thick borders. This should solve the issue of Invalid Drain Neighborhoods
                    // an remove the need for this error prone method
                    if(!DrainNeighborhoodIsValid(probeIdx, ref boundary_BM, ref sharedBoundary)) continue;
                    drainHeight = heightMap[probeIdx];
                    drainIdx = probeIdx;
                }
            }
            
            sharedBoundary.Clear();
            SaveManyDrains(drainIdx, ref minimas, ref drainToMinima, ref minimaToDrain, ref key);
        }

        public bool DrainNeighborhoodIsValid(
            int probeIdx,
            ref NativeParallelMultiHashMap<int, int> boundary_BM,
            ref NativeList<int> sharedBoundary
        ){
        // this cuts off little spurs of false indepedant border
        // also tried to cut off when it falls into a self contained border with no exit
            int boundaryLocked = 0;
            int nborProbe = 0;
            for(int xm = -1; xm <= 1; xm += 1){
                for(int ym = -res.x; ym <= res.x; ym += res.x){
                    nborProbe = xm + ym + probeIdx;
                    if( nborProbe < 0) continue;
                    if( nborProbe >= (res.x * res.y)) continue;
                    if( NativeArrayExtensions.Contains<int, int>(sharedBoundary, probeIdx + xm + ym)){
                        return false;
                    }
                    if (boundary_BM.ContainsKey(nborProbe)){
                        boundaryLocked += 1;
                    }
                }
            }
            return boundaryLocked <= 5;
        }

        public void SaveManyDrains(
            int drainIdx,
            ref NativeList<int> minimas,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeParallelHashMap<PoolKey, int> minimaToDrain,
            ref PoolKey key
        ){
            key.idx = drainIdx;
            PoolKey probe = key.Clone();
            int present = 0;
            while(drainToMinima.ContainsKey(key)){  
                for(int i = 0; i < minimas.Length; i++){
                    probe.idx = minimas[i];
                    if (minimaToDrain.ContainsKey(probe)){
                        present += 1;
                    }
                }
                if (present == minimas.Length){
                    break;
                }
                key.n += 1;
            }
            for(int i = 0; i < minimas.Length; i++){
                drainToMinima.Add(key, minimas[i]);
            }
            for(int i = 0; i < minimas.Length; i++){
                key.idx = minimas[i];
                minimaToDrain.TryAdd(key, drainIdx);
            }
        }

        public void SolveDrainHeirarchy(
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchment,
            ref NativeParallelHashMap<PoolKey, Pool> pools,
            NativeList<PoolKey> drainKeyOut,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ProfilerMarker? profiler = null
        ){
            var (mnIdxs, mnSize) = NativeParallelHashMapExtensions.GetUniqueKeyArray(boundary_MB, Allocator.Temp);
            // drainToMinima [idx] -> minima
            NativeParallelHashMap<PoolKey, int> minimaToDrain = new NativeParallelHashMap<PoolKey, int>(mnSize, Allocator.Temp);
            
            for(int i = 0; i < mnSize; i++){
                FindFirstOrderDrain(mnIdxs[i], ref drainToMinima, ref minimaToDrain, ref boundary_MB);      
            }
            NativeList<int> minimas = new NativeList<int>(32, Allocator.Temp);
            NativeList<int> sharedBoundary = new NativeList<int>(res.x, Allocator.Temp);

            int searchDepth = 2;
            bool reduced = false;
            while(searchDepth < POOLSEARCHDEPTHI){
                reduced = ReduceDrains(searchDepth, ref drainToMinima, ref minimaToDrain, ref boundary_MB, ref boundary_BM, ref sharedBoundary);
                if (!reduced) break;
                searchDepth += 1;
            }
            var catchIter = catchment.GetEnumerator();
            NativeList<float> members = new NativeList<float>(res.x, Allocator.Temp);
            var (drainKeys, drainKeySize) = NativeParallelHashMapExtensions.GetUniqueKeyArray(drainToMinima, Allocator.Temp);
            for(int i = 0; i < drainKeySize; i++){
                drainKeyOut.Add(drainKeys[i]);
            }
        }

        public void CollectSharedBoundary(
            NativeList<int> minimaSet,
            ref NativeParallelMultiHashMap<int, int> boundary_MB,
            ref NativeList<int> sharedBoundary
        ){
            // boundary -> minima
            NativeParallelMultiHashMap<int, int> allBoundaries = 
                new NativeParallelMultiHashMap<int, int>(res.x * minimaSet.Length, Allocator.Temp);
            
            // put all boundaries into single set
            for(int i = 0; i < minimaSet.Length; i ++){
                var boundarIter = boundary_MB.GetValuesForKey(minimaSet[i]);
                while(boundarIter.MoveNext()){
                    allBoundaries.Add(boundarIter.Current, minimaSet[i]);
                }
            }
            var (minimaBoundaries, mbSize) = NativeParallelHashMapExtensions.GetUniqueKeyArray(allBoundaries, Allocator.Temp);
            for(int i = 0; i < mbSize; i++){
                if(allBoundaries.CountValuesForKey(minimaBoundaries[i]) > 1){
                    sharedBoundary.Add(minimaBoundaries[i]);
                }
            }

        }

        public void CollectPoolMembers(
            NativeList<int> minimaSet,
            int poolOrder,
            float drainHeight,
            NativeParallelHashMap<int, int>.Enumerator catchmentMap,
            ref NativeList<float> members
        ){
            while(catchmentMap.MoveNext()){
                if (NativeArrayExtensions.Contains<int, int>(minimaSet, catchmentMap.Current.Value)){
                    float height = heightMap[catchmentMap.Current.Key];
                    if (height < drainHeight){
                        members.Add(height);
                    }
                } 
            }
        }

        public void CollectMembersSharedInternalBorder(
            NativeList<int> minimaSet,
            float drainHeight,
            ref NativeParallelMultiHashMap<int, int> boundary_MB,
            ref NativeList<float> members
        ){
            NativeList<int> sharedBoundary = new NativeList<int>(res.x, Allocator.Temp);
            CollectSharedBoundary(minimaSet, ref boundary_MB, ref sharedBoundary);
            float probe = 0f;
            for(int i = 0; i < sharedBoundary.Length; i++){
                probe = heightMap[sharedBoundary[i]];
                if (probe < drainHeight){
                    members.Add(probe);
                }
            }
        }

        public void CreatePool(
            ref NativeList<int> minimaSet,
            int poolOrder,
            int drainIdx,
            NativeParallelHashMap<int, int>.Enumerator catchmentMap,
            ref NativeParallelMultiHashMap<int, int> boundary_MB,
            ref NativeList<float> members,
            ref NativeParallelHashMap<PoolKey, Pool>.ParallelWriter pools,
            ProfilerMarker? profiler = null
        ){
            float drainHeight = heightMap[drainIdx];
            
            profiler?.Begin();
            CollectPoolMembers(minimaSet, poolOrder, drainHeight, catchmentMap, ref members);
            profiler?.End();
            profiler?.Begin();
            CollectMembersSharedInternalBorder(minimaSet, drainHeight, ref boundary_MB, ref members);
            profiler?.End();
            
            profiler?.Begin();
            int referenceMinima = 0;
            float refMinHeight = float.MaxValue;

            for (int i = 0; i < minimaSet.Length; i++){
                if (heightMap[minimaSet[i]] < refMinHeight){
                    refMinHeight = heightMap[minimaSet[i]];
                    referenceMinima = minimaSet[i];
                }
            }
            Pool pool = new Pool();
            // Debug.Log($"{referenceMinima} -> {refMinHeight} >> {drainHeight} @{members.Length}");
            pool.Init(referenceMinima, refMinHeight, drainIdx, drainHeight, (byte) poolOrder);
            pool.SolvePool(members.AsArray());
            PoolKey key = new PoolKey {
                idx = referenceMinima,
                order = (byte) poolOrder,
                n = 0
            };
            // This SolveUpstreamPoolPrecidence is quite fast and not parallel safe
            // so it's going to get done after in a different job even though we could do it here
            pools.TryAdd(key, pool);
            profiler?.End();
        }

        public void SolvePoolPeering(
            ref NativeArray<PoolKey> sortedKeys,
            ref NativeParallelHashMap<PoolKey, Pool> pools){
            // we just brute force this
            int found = 0;
            Pool current;

            int x = 0;
            for (int i = 0; i < sortedKeys.Length; i++){
                current = pools[sortedKeys[i]];
                x = 0;
                found = 0;
                while( x < sortedKeys.Length && found < 3){
                    if (i == x){
                        // don't peer with yourself
                        x++;
                        continue;
                    }
                    if (current.indexDrain == pools[sortedKeys[x]].indexDrain){
                        current.AddPeer(found, sortedKeys[x]);
                        found++;
                    }
                    x++;
                }
                pools[sortedKeys[i]] = current;
            }

        }

        // solve for supercededBy / minimumVolume
        public void SolveUpstreamPoolPrecidence(
            PoolKey key,
            int poolOrder,
            ref NativeList<int> minimaSet,
            ref NativeParallelHashMap<PoolKey, Pool> pools
        ){
            // reaches down from parents into their children and set their lineage
            PoolKey probe = new PoolKey();
            Pool poolProbe = new Pool();
            bool match = false;
            for(int i = 0; i < minimaSet.Length; i++){
                probe.idx = minimaSet[i];
                match = false;
                for (byte ord = (byte) (poolOrder - 1); ord > 0 ; ord --){
                    probe.order = ord;
                    probe.n = 0;
                    while(pools.ContainsKey(probe)){
                        pools.TryGetValue(probe, out poolProbe);
                        if(probe.Equals(key)) Debug.LogError($"{key.idx} This should not happen!");
                        if (!poolProbe.HasParent()){
                            poolProbe.supercededBy = key;
                            pools[probe] = poolProbe;
                            match = true;
                        }
                        probe.n += 1;
                    }
                    if (match){
                        if(!pools.ContainsKey(key)){
                            Debug.LogError($"could not set minimum volume on {key.idx}:{key.order}");
                        }else{
                            Pool parent = pools[key];
                            // all children should have the same drain height
                            // so we just take the last one
                            parent.SetMinimumVolume(poolProbe.drainHeight);
                            pools[key] = parent;
                        }
                        break;
                    }
                }
            }
        }

        // TODO remove setups, I think it's better to setup the struct explicitly for steps that require
        // it than to have a method for each case

        public void SetupCollapse(
            int resolution,
            NativeArray<Cardinal> flow_,
            NativeSlice<float> heightMap_,
            NativeSlice<float> outMap_
        ){
            res = new int2(resolution, resolution);
            flow = flow_;
            heightMap = heightMap_;
            outMap = outMap_;
        }

        public void SetupPoolGeneration(
            int resolution,
            NativeSlice<float> heightMap_,
            NativeSlice<float> outMap_
        ){
            res = new int2(resolution, resolution);
            heightMap = heightMap_;
            outMap = outMap_;
        }

/*
// 
//      Pool Creation Job
// 
*/

        // Parallel
        public void CreatePoolFromDrain(
            PoolKey drainKey,
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeParallelHashMap<int, int> catchment,
            ref NativeParallelMultiHashMap<int, int> boundary_MB,
            ref NativeParallelHashMap<PoolKey, Pool>.ParallelWriter pools,
            ProfilerMarker? profiler = null
        ){
            profiler?.Begin();
            var catchIter = catchment.GetEnumerator();
            NativeList<int> minimas = new NativeList<int>(32, Allocator.Temp);
            NativeList<float> members = new NativeList<float>(res.x, Allocator.Temp);
            profiler?.End();
            profiler?.Begin();
            AddUniquePoolValues(drainKey, ref drainToMinima, ref minimas);
            profiler?.End();
            CreatePool(ref minimas, (int) drainKey.order, drainKey.idx, catchIter, ref boundary_MB, ref members, ref pools, profiler);

            members.Dispose();
            minimas.Dispose();
        }

        // TODO
        // public void CreatePoolEvent(int x, int z, float volume, ref NativeList<PoolUpdate>.Writer eventStream){}

        // Single
        public void LinkPoolHeirarchy(
            ref NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            ref NativeParallelHashMap<PoolKey, Pool> pools
        ){
            Pool referencePool;
            PoolKey referenceKey;
            NativeArray<PoolKey> poolKeys = pools.GetKeyArray(Allocator.Temp);
            NativeList<int> minimas = new NativeList<int>(256, Allocator.Temp);
            poolKeys.Sort();
            SolvePoolPeering(ref poolKeys, ref pools);
            for(int i = 0; i < poolKeys.Length; i ++){
                referenceKey = poolKeys[i];
                pools.TryGetValue(referenceKey, out referencePool);
                referenceKey.idx = referencePool.indexDrain;
                AddUniquePoolValues(referenceKey, ref drainToMinima, ref minimas);
                // We need this second pick up as we swap to the drain idx to search for unique values
                referenceKey = poolKeys[i];
                SolveUpstreamPoolPrecidence(referenceKey, referenceKey.order, ref minimas, ref pools);
                minimas.Clear();
            }
        }

        // Single
        public void ReducePoolUpdatesAndApply(NativeQueue<PoolUpdate> updateQueue, ref NativeParallelHashMap<PoolKey, Pool> pools){
            // Updating the pools may require traversing the linked list in the pool values
            // so it's economical to combine updates to the same pool

            NativeArray<PoolKey> poolKeys = pools.GetKeyArray(Allocator.Temp);
            poolKeys.Sort();
            NativeList<PoolUpdate> reduced = new NativeList<PoolUpdate>(pools.Count(), Allocator.Temp);
            NativeArray<PoolUpdate> updates = updateQueue.ToArray(Allocator.Temp);
            updates.Sort();
            int skip = 1;
            int idx = 0;
            float volume = 0f;
            for(int i = 0; i < updates.Length; i+= skip){
                skip = 1;
                idx = updates[i].minimaIdx;
                volume = updates[i].volume;
                while(skip + i < updates.Length && updates[i + skip].minimaIdx == idx){
                    volume += updates[i + skip].volume;
                    skip++;
                }
                reduced.Add(new PoolUpdate {minimaIdx = idx, volume = volume});
            }
            for( int i = 0; i < reduced.Length; i ++){
                UpdatePoolValue(reduced[i], ref pools, ref poolKeys);
            }

        }

        public void UpdatePoolValue(PoolUpdate update, ref NativeParallelHashMap<PoolKey, Pool> pools, ref NativeArray<PoolKey> poolKeys){
            PoolKey key = new PoolKey {
                idx = update.minimaIdx,
                order = 1,
                n = 0
            };
            Pool pool = new Pool(){
                indexMinima = -1
            };
            if(update.volume > 0f){
                while(poolKeys.Contains(key)){
                    pools.TryGetValue(key, out pool);
                    Debug.Log($"working on {pool.indexMinima}:{key.order} // starting @ {pool.volume} [{pool.minVolume}]");
                    pool.volume = max(pool.volume, pool.minVolume);
                    if((pool.volume + update.volume) < pool.capacity){
                        pool.volume += update.volume;
                        pools[key] = pool;
                        // pool's not full and we have no more water
                        Debug.Log($"{pool.indexMinima}:{key.order} is not yet full {pool.volume} / {pool.capacity}");
                        return;
                    }
                    pools[key] = pool;
                    // Instead of trying to fill peers, if they're not already full, we'll flow.

                    // BalancePeerPools(ref key, ref pool, ref update.volume, ref pools, ref key);
                    if(UpdatedPoolHasPeerCapacity(ref key, ref pool, ref update.volume, ref pools)){
                        Debug.Log($"pool {key.idx}:{key.order}n{key.n} flowing {update.volume} to peer");
                        EmitFlowFromPool(pool, update.volume);
                        return;
                    }
                    if(!pool.HasParent()){
                        Debug.Log($"pool {key.idx}:{key.order}n{key.n} overfilled by {update.volume}, no successor -> flow");
                        EmitFlowFromPool(pool, update.volume);
                        return;
                    }
                    key = pool.supercededBy;
                }
                Debug.LogWarning($"No pool for {key.idx}");
                return;
            }else{
                // TODO
                Debug.LogWarning("not handling negation yet");
            }
            Debug.LogWarning($"We should not really ever get here? {key.idx} -> {update.volume}");
        }


        public bool UpdatedPoolHasPeerCapacity(
            ref PoolKey pk,
            ref Pool primaryPool,
            ref float incomingVolume,
            ref NativeParallelHashMap<PoolKey, Pool> pools
        ){
            // try to allocate all water to the primary pool
            UpdatePoolPeerVolume(ref pk, ref primaryPool, ref incomingVolume, incomingVolume, ref pools);
            int peers = primaryPool.PeerCount();
            if(peers == 0) return false;
            Pool peerPool = new Pool();
            PoolKey peerKey = new PoolKey();
            for(int i = 0; i < peers; i++){
                peerKey = primaryPool.GetPeer(i);
                pools.TryGetValue(peerKey, out peerPool);
                if (peerPool.volume < peerPool.capacity) return true;
            }
            return false;
        }

        public void BalancePeerPools( 
            ref PoolKey pk,
            ref Pool primaryPool,
            ref float incomingVolume,
            ref NativeParallelHashMap<PoolKey, Pool> pools,
            ref PoolKey originator
        ){
            // try to allocate all water to the primary pool
            UpdatePoolPeerVolume(ref pk, ref primaryPool, ref incomingVolume, incomingVolume, ref pools);
            int peers = primaryPool.PeerCount();
            if(peers == 0) return;
            if(primaryPool.HasPeer(originator) && peers == 1) return;
            Pool peerPool = new Pool();
            PoolKey peerKey = new PoolKey();
            float allocation = !primaryPool.HasPeer(originator) ? incomingVolume / (float) peers : incomingVolume / (float) (peers - 1);
            Debug.Log($"{pk.idx}:{pk.order} exc {incomingVolume} allocating {allocation} -> {peers} peers");
            for(int i = 0; i < peers; i++){
                float allocationLocal = allocation;
                peerKey = primaryPool.GetPeer(i);
                if(peerKey.Equals(originator)){
                    continue;
                }
                pools.TryGetValue(peerKey, out peerPool);
                BalancePeerPools(ref peerKey, ref peerPool, ref allocationLocal, ref pools, ref originator);
                // if(UpdatePoolPeerVolume(ref peerKey, ref peerPool, ref incomingVolume, allocation, ref pools));
                incomingVolume -= (allocation - allocationLocal);
                Debug.Log($"{pk.idx}:{pk.order}-> {peerKey.idx}:{peerKey.order} new excess {incomingVolume}");
            }
        }

        public void EmitFlowFromPool(Pool originator, float volume){
            // Special cast of flow
            // We don't want to flow back into the pool from which we just were emitted
        }

        public void UpdatePoolPeerVolume(
            ref PoolKey key,
            ref Pool pool,
            ref float availableWater,
            float allocatedVolume,
            ref NativeParallelHashMap<PoolKey, Pool> pools
        ){
            float newWater = min(allocatedVolume, pool.capacity - pool.volume);
            if (newWater < 1E-10) return;
            pool.volume = pool.volume + newWater;
            // we check for equality so round off any edges
            if (abs(pool.volume - pool.capacity) < 1E-10) pool.volume = pool.capacity;
            Debug.Log($"{pool.indexMinima}:{key.order} >> {pool.volume} / {pool.capacity}");
            availableWater -= newWater;
            pools[key] = pool;
            return;
        }

        // Parallel

        public void DrawPoolLocation(
            int x,
            int z, 
            ref NativeParallelHashMap<int, int> catchment,          // (member -> minima)
            ref NativeParallelMultiHashMap<int, int> boundary_BM,   // ""
            ref NativeParallelHashMap<PoolKey, Pool> pools,
            ref NativeSlice<float> heightMap,
            ref NativeSlice<float> poolMap
        ){
            int idx = getIdx(x, z);
            float terrainHeight = heightMap[idx];
            PoolKey key = new PoolKey {
                idx = -1,
                order = 1,
                n = 0
            };
            catchment.TryGetValue(idx, out key.idx);
            // catchment members should be mutually exclusive with boundaries...
            float catchValue = 0f;
            if(key.Exists()){
                // is catchment
                catchValue =  LocalHeightInPool(terrainHeight, ref key, ref pools);
            }
            // is boundary
            float boundaryValue = 0f;
            if(boundary_BM.ContainsKey(idx)){
                NativeParallelMultiHashMap<int, int>.Enumerator minimaIter = boundary_BM.GetValuesForKey(idx);
                while (minimaIter.MoveNext()){
                    key = new PoolKey {
                        idx = minimaIter.Current,
                        order = 1,
                        n = 0
                    };
                    boundaryValue = max(
                        LocalHeightInPool(terrainHeight, ref key, ref pools)
                        , boundaryValue);
                }
            }
            poolMap[idx] = max(catchValue, boundaryValue);
            
        }

        public float LocalHeightInPool(
            float terrainHeight,
            ref PoolKey key,
            ref NativeParallelHashMap<PoolKey, Pool> pools
        ){
            int depth = 0;
            Pool pool = new Pool(){
                indexMinima = -1
            };
            while(pools.ContainsKey(key)){
                depth++;
                pools.TryGetValue(key, out pool);
                if(pool.volume < pool.capacity){
                    // Debug.LogWarning($"has capacity {key.idx}:{key.order}n{key.n} -> {pool.volume} / {pool.capacity}, break @ {depth}");
                    break;
                }
                if(pool.HasParent() && pools[pool.supercededBy].volume > 0f){
                    // Debug.LogWarning($"is full and has parent {key.idx}:{key.order}n{key.n} >> {pool.supercededBy.idx}:{pool.supercededBy.order}n{pool.supercededBy.n} @ {depth}");
                    key = pool.supercededBy;
                }else{
                    break;
                }
                if(depth > 16){
                    Debug.LogError("maximum pool draw depth exceeded, bugged?");
                    break;
                }
            }
            if (!pool.Exists()){
                return 0f;
            }else{
                float value = 1f;
                pool.EstimateHeight(terrainHeight, out value);
                return value;
            }
        }

        public void PoolDrawDebugAndCleanUp(
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchment,
            NativeParallelHashMap<PoolKey, Pool> pools,
            bool paintFor3D = false
        ){
            // outmap / heightmap / res from setup required

            var catchIter = catchment.GetEnumerator();
            ParticleDebugHelper draw = new ParticleDebugHelper();
            
            if (!paintFor3D){
                var (mnIdxs, mnSize) = NativeParallelHashMapExtensions.GetUniqueKeyArray(boundary_MB, Allocator.Temp);
                draw.Debug__PaintPoolsStatic(ref outMap, ref heightMap, ref pools, catchIter);
                draw.Debug__PaintMinimas(ref outMap, ref mnIdxs, 0f);
                draw.Debug__PaintStaticBoundaries(ref outMap, ref boundary_MB, 0f);
                draw.Debug__PaintDrains(ref outMap, ref pools, 1f);
                draw.Debug__InfoPools(ref pools);
            }else{
                draw.Debug__PaintPools(ref outMap, ref heightMap, ref pools, catchIter);
                draw.Debug__PaintBoundaries(ref outMap, ref heightMap, ref pools, ref boundary_BM);
            }         
        }

    }

    public struct FlowMaster {
        public WorldTile tile;
        
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        public NativeQueue<ErosiveEvent> events;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeQueue<ErosiveEvent>.ParallelWriter eventWriter;
        private Unity.Mathematics.Random random;
        static readonly int2 ZERO = new int2(0,0);

        public int2 MaxPos {
            get { return tile.res;}
            private set {}
        }

        public int2 RandomPos(){
            return random.NextInt2(ZERO, MaxPos);
        }

        // MultiThread
        public void ServiceParticle(ref Particle p, int seed, int maxSteps){
            random = new Unity.Mathematics.Random((uint) seed);
            if(p.isDead){
                p.Reset(RandomPos());
            }
            int step = 0;
            while(step < maxSteps){
                step += Descend(ref p, maxSteps - step);
            }
        }

        public int Descend(ref Particle p, int maxSteps){
            int step = 0;
            bool done = false;
            ErosiveEvent evt;
            while(step < maxSteps && !done){
                done = p.DescentComplete(ref tile, out evt);
                eventWriter.Enqueue(evt);
                if(done){
                    p.Reset(RandomPos());
                }
                step++;
            }
            return step;
        }

        // Single Thread
        public void ServiceParticleSingle(ref Particle p, int seed, int maxParticles){
            random = new Unity.Mathematics.Random((uint) seed);
            if(p.isDead){
                p.Reset(RandomPos());
            }
            int partCount = 0;
            while(partCount < maxParticles){
                DescendSingle(ref p);
                partCount++;
            }
        }

        // Single Beyer

        public void ServiceBeyerParticle(ref BeyerParticle p, int seed, int maxParticles){
            random = new Unity.Mathematics.Random((uint) seed);
            if(p.isDead){
                p.Reset(RandomPos());
            }
            int partCount = 0;
            while(partCount < maxParticles){
                p.DoDescent(ref tile);
                p.Reset(RandomPos());
                partCount++;
            }
        }
        



        public int DescendSingle(ref Particle p){
            int step = 0;
            bool done = false;
            ErosiveEvent evt;
            int next = 0;
            while(true){
                done = p.DescentCompleteSingle(ref tile, out evt);
                // next = tile.getIdx(p.pos);
                // if (next == -1){
                //     p.Reset(RandomPos());
                //     return step;
                // }
                // tile.CascadeHeightMapChange(next);
                if(done){
                    // Debug.Log($"stuck @ {p.pos.x}, {p.pos.y} v:{p.volume}");
                    if(!tile.Flood(ref p)){
                        // Debug.Log($"No flood {p.volume} remains");
                        p.Reset(RandomPos());
                        return step;
                    }
                    // Debug.Log($"drain jump >> {p.pos.x}, {p.pos.y}");
                    done = false;
                }
                step++;
            }
            return step;
        }

        // Single Thread
        public void CommitUpdateToMaps(
            ErosiveEvent evt,
            ref NativeQueue<PoolUpdate> poolUpdates,
            ref NativeParallelHashMap<int, int> catchment
        ){
            if(abs(evt.deltaPoolMap) > 0f){
                PoolUpdate pu = new PoolUpdate {
                    volume = 0.25f * evt.deltaPoolMap
                };
                catchment.TryGetValue(evt.idx, out pu.minimaIdx);
                if(pu.minimaIdx == 0){
                    Debug.LogError($"No valid minima on pool update from evt idx {evt.idx} v: {evt.deltaPoolMap}");
                }else{
                    poolUpdates.Enqueue(pu);
                }

            }
            if(abs(evt.deltaWaterTrack) > 0f){
                float v = tile.track[evt.idx];
                v += evt.deltaWaterTrack;
                tile.track[evt.idx] = v;
            }
            if(abs(evt.deltaSediment) > 0f){
                float v = tile.height[evt.idx];
                v += evt.deltaSediment;
                tile.height[evt.idx] = v;
                tile.CascadeHeightMapChange(evt.idx);
            }
        }
    }

    // public struct ParticleMergeStep : IParticleManager {
    //     // Merges superimposed particles from a sorted list
    //     public void Execute<P>(NativeSlice<P> particles) where P : struct, IParticle{
    //         int current = -1;
    //         int2 dead = new int2(-1, -1);
    //         bool2 same;
    //         for (int i = 0; i < particles.Length; i++){
    //             same = (particles[i].GetPosition() == dead);
    //             if (same.x && same.y){
    //                 continue;
    //             }
    //             if (current < 0){
    //                 current = i;
    //                 continue;
    //             }
    //             same = (particles[current].GetPosition() == particles[i].GetPosition());
    //             if (same.x && same.y){
    //                 particles[current].Consume<P>(particles[i]);
    //                 particles[i].SetPosition(dead.x, dead.y);
    //             }else{
    //                 current = i;
    //             }
    //         }
    //     }
    // }

    // public struct ParticleSortStep : IParticleManager {
    //     // Merges superimposed particles from a sorted list
    //     public void Execute<P>(NativeSlice<P> particles) where P : struct, IParticle{
    //         // use the sort extension
    //     }
    // }

    // public struct ParticleErosionStep: IParticleErode {
    //     public int Resolution {get; set;}
    //     public int JobLength {get; set;}
    //     private const float TIMESTEP = 0.2f;

    //     void Effect<P, RW>(P particle, RW tile) where RW: struct, IRWTile {

    //     }

    //     public void Execute<P, RW>(int i, NativeSlice<P> particles, RW tile)
    //         where P : struct, IParticle
    //         where RW: struct, IRWTile {

    //     }

    // }

}