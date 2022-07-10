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
            float diff = abs(b - a);
            if (abs(diff) < .0000001){
                return 0;
            }
            if (diff > 0){
                return 1;
            }
            return -1;
        }
    }

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

    
    // Boundaries -> { posMinima : [min_0_idx, ..., min_n_idx]}
    // Pools -> { posMinima : Pool}
    // PoolPositions { idx : PoolPosition} // Sparse Map

    struct Pool {
        public int indexMinima;
        public float minimaHeight;
        public int indexDrain;
        public float drainHeight;
        public float capacity;
        public float currentVolume;
        public float b1;
        public float b2;

        public void Init(int indexMinima_, float minimaHeight_, int indexDrain_, float drainHeight_){
            indexMinima = indexMinima_;
            minimaHeight = minimaHeight_;
            indexDrain = indexDrain_;
            drainHeight = drainHeight_;
            currentVolume = 0f;
        }

        public void EstimateHeight(float cellHeight, out float waterHeight){
            waterHeight = (b1 + b2 * log(currentVolume)) - (cellHeight - minimaHeight);
        }
        
        public void SolvePool(NativeArray<float> heights){
            capacity = 0f;
            NativeArray<float> trainingVolumes = new NativeArray<float>(heights.Length, Allocator.Temp);

            heights.Sort<float, LazyFloatComparer>(new LazyFloatComparer());
            minimaHeight = heights[0];
            float volumeBehind = 0f;
            trainingVolumes[0] = .0000001f;
            for (int i = 0 ; i < heights.Length; i++){
                capacity += drainHeight - heights[i];
                if (i > 0){
                    volumeBehind += ((heights[i] - heights[i - 1]) * i);
                    trainingVolumes[i] = volumeBehind;
                }  
            }
            Regression regressor = new Regression();
            regressor.LogRegression(heights, trainingVolumes, out b1, out b2);
        }
    }

    struct PoolPosition {
        float height; // current height land
        int minimaIndex; // where to find the minima this is linked to -> It's Pool
    }

    struct Regression {
        float Mean(NativeArray<float> items){
            float sum = 0f;
            for( int i = 0; i < items.Length; i ++){
                sum += items[i];
            }
            return sum / items.Length;
        }

        float SumSquareDifference(NativeArray<float> items){
            float i_mean = Mean(items);
            float sum = 0f;
            for( int i = 0; i < items.Length; i ++){
                sum += pow(items[i] - i_mean, 2f);
            }
            return sum;
        }

        float ComputeSXY(NativeArray<float> xs, NativeArray<float> ys){
            float mean_x = Mean(xs);
            float mean_y = Mean(ys);
            float sum = 0f;
            for( int i = 0; i < xs.Length; i ++){
                sum += (xs[i] - mean_x) * (ys[i] - mean_y);
            }
            return sum;
        }

        float MeanSquareError(NativeArray<float> pred, NativeArray<float> real){
            float sum = 0f;
            for( int i = 0; i < pred.Length; i ++){
                sum += pow((pred[i] - real[i]), 2);
            }
            return sum / pred.Length;
        }

        float PredictLog(float x, float b1, float b2){
            return b1 + b2 * log(x);
        }

        public void LogRegression(NativeArray<float> xs, NativeArray<float> ys, out float b1, out float b2, bool RectifyToEndValue = true){
            // Convert x -> ln(x)
            int size = xs.Length;
            float xM = xs[size - 1];
            
            for( int i = 0; i < size; i ++){
                xs[i] = log(xs[i]);
            }
            float sxx = SumSquareDifference(xs);
            float sxy = ComputeSXY(xs, ys);
            float syy = SumSquareDifference(ys);

            b2 = sxy / sxx;
            b1 = Mean(ys) - b2 * Mean(xs);

            if (RectifyToEndValue){
                float corr = PredictLog(xM, b1, b2) - ys[size - 1];
                b1 += corr;
            }
        }

    }

    struct FlowSuperPosition : IPoolSuperPosition{
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
        int2 res;
        
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
            ref NativeParallelHashMap<int, Cardinal> frontier,
            ref NativeParallelHashSet<int> basin
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
            ref NativeParallelHashMap<int, Cardinal> frontier,
            ref NativeParallelHashSet<int> basin)
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
                else if (!basin.Contains(idxN) && frontier.ContainsKey(idxN)){
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
            ref NativeParallelHashMap<int, Cardinal> frontier,
            ref NativeParallelHashSet<int> basin,
            ref NativeParallelHashSet<int>.Enumerator frontierIndices
        ){
            bool foundNewFrontier = false;
            while(frontierIndices.MoveNext()){
                int idxCandidate = frontierIndices.Current;
                if(!basin.Contains(idxCandidate) && frontier[idxCandidate] == Cardinal.X){
                    basin.Add(idxCandidate);
                    foundNewFrontier = UpdateFrontier(
                        getPos(idxCandidate),
                        ref frontier,
                        ref basin
                        ) || foundNewFrontier;
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
            ref NativeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator,
            ref NativeParallelHashSet<int> frontierIdx,
            ref NativeParallelHashSet<int> basin
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
            ref NativeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator,
            ref NativeParallelHashMap<int, Cardinal> frontier,
            ref NativeParallelHashSet<int> frontierIdx,
            ref NativeParallelHashSet<int> trash
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

        public void CollapseMinima(int idx, ProfilerMarker? profiler = null){
            int2 pos = getPos(idx);
            // TODO see if we can optimize the size of these collections so they don't need to resize
            NativeParallelHashMap<int, Cardinal> frontier = new NativeParallelHashMap<int, Cardinal>(
                (int)(res.x ), Allocator.Temp);
            NativeParallelHashSet<int> frontierIdx = new NativeParallelHashSet<int>(
                (int)(res.x ), Allocator.Temp);
            NativeParallelHashSet<int> basin = new NativeParallelHashSet<int>(
                (int)(res.x ), Allocator.Temp);
            NativeParallelHashSet<int> trash = new NativeParallelHashSet<int>(
                (int)(res.x ), Allocator.Temp);
        
            bool basinComplete = false;
            // by definition the minima is in the basin.
            basin.Add(idx);
            // Our frontier starts with the adjacent values
            UpdateFrontier(pos, ref frontier, ref basin);
            NativeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator = frontier.GetEnumerator();
            UpdateFrontierReferences(ref frontierEnumerator, ref frontierIdx, ref basin);
            NativeParallelHashSet<int>.Enumerator frontierIdxIter = frontierIdx.GetEnumerator();
            while(!basinComplete){
                frontierIdxIter.Reset();
                bool collapsed = false;
                while(frontierIdxIter.MoveNext()){
                    int idxCandidate = frontierIdxIter.Current;
                    collapsed = CollapseFrontierState(getPos(idxCandidate), ref frontier, ref basin) || collapsed;               
                }
                
                frontierIdxIter.Reset();
                basinComplete = !MoveToBasin(ref frontier, ref basin, ref frontierIdxIter);
                
                profiler?.Begin();
                frontierEnumerator.Reset();
                UpdateFrontierReferences(ref frontierEnumerator, ref frontierIdx, ref basin);
                PruneFrontierMap(ref frontierEnumerator, ref frontier, ref frontierIdx, ref trash);
                profiler?.End();
            }

            // NativeParallelHashMap<int, Cardinal>.Enumerator frontierEnumerator = frontier.GetEnumerator();
            frontierEnumerator.Reset();
            int drainIdx = 0;
            int probeIdx = 0;
            float drainHeight = float.MaxValue;

            while(frontierEnumerator.MoveNext()){
                probeIdx = frontierEnumerator.Current.Key;
                if (basin.Contains(probeIdx)) continue;
                outMap[probeIdx] = 0f;
                if (heightMap[probeIdx] < drainHeight){
                    drainHeight = heightMap[probeIdx];
                    drainIdx = probeIdx;
                }
            }
            // Debug.Log(drainIdx);

            // HeightValueSort
            outMap[idx] = 1f;
            NativeParallelHashSet<int>.Enumerator basinMembers = basin.GetEnumerator();
            while(basinMembers.MoveNext()){
                probeIdx = basinMembers.Current;
                if(probeIdx == idx){
                    continue;
                }
                if (heightMap[probeIdx] < drainHeight){
                    outMap[probeIdx] = .2f;
                    // Debug.Log(probeIdx);
                }
                // else{
                //     outMap[probeIdx] = .4f;
                // }
            }
            outMap[drainIdx] = 1f;

        }

        public void Setup(NativeArray<Cardinal> flow_, NativeSlice<float> heightMap_, NativeSlice<float> outMap_, int resolution){
            res = new int2(resolution, resolution);
            flow = flow_;
            heightMap = heightMap_;
            outMap = outMap_;
        }


    }


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