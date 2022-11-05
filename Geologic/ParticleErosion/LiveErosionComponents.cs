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

    public struct FlowMaster {
        public WorldTile tile;
        
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        // public NativeQueue<ErosiveEvent> events;
        public NativeParallelMultiHashMap<int, ErosiveEvent> events;

        
        // [NativeDisableContainerSafetyRestriction]
        // public NativeQueue<ErosiveEvent>.ParallelWriter eventWriter;
        
        [NativeDisableContainerSafetyRestriction]
        public NativeParallelMultiHashMap<int, ErosiveEvent>.ParallelWriter eventWriter;

        private Unity.Mathematics.Random random;
        static readonly int2 ZERO = new int2(0,0);

        // public static readonly float POOL_PLACEMENT_MULTIPLIER = 0.5f;
        // public static readonly float TRACK_PLACEMENT_MULTIPLIER = 80f;

        public static readonly float SEDIMENT_PLACEMENT_DIVISOR = 1f;
        

        public static readonly float[] KERNEL3 = new float[] { 0.30780132912347f, 0.38439734175306006f, 0.30780132912347f };
        public static readonly float[] KERNEL5 = new float[] { 0.12007838424321349f, 0.23388075658535032f, 0.29208171834287244f, 0.23388075658535032f, 0.12007838424321349f };

        public int2 MaxPos {
            get { return tile.ep.TILE_RES;}
            private set {}
        }

        public int2 RandomPos(){
            return random.NextInt2(ZERO, MaxPos);
        }

        public void CreateRandomParticles(
            int count,
            int seed,
            ErosionParameters ep,
            ref NativeQueue<BeyerParticle>.ParallelWriter particleWriter
        ){
            random = new Unity.Mathematics.Random((uint) seed);
            for (int i = 0; i < count; i++){
                particleWriter.Enqueue(
                    new BeyerParticle(RandomPos(), ep, false)
                );
            }
        }

        public void BeyerSimultaneousDescentSingle(
            ref BeyerParticle p
        ){
            ErosiveEvent evt;
            while(!p.isDead){
                p.DescendSimultaneous(ref tile, out evt);
                // eventWriter.Enqueue(evt);
                eventWriter.Add(evt.idx, evt);
            }
        }

        public void CombineBeyerEvents(ErosiveEvent evt, ref float poolV, ref float trackV, ref float sedimentV){
            poolV += evt.deltaPoolMap;
            trackV += evt.deltaWaterTrack;
            sedimentV += evt.deltaSediment;
        }

        public void HandleBeyerEvent(int idx, float poolV, float trackV, float sedimentV, ref NativeQueue<ErosiveEvent>.ParallelWriter erosionWriter){
            if(abs(poolV) > 0f){
                Place(idx, poolV, (tile.ep.POOL_PLACEMENT_MULTIPLIER), ref tile.pool);
            }
            if(abs(trackV) > 0f){
                Place(idx, trackV, tile.ep.TRACK_PLACEMENT_MULTIPLIER, ref tile.track);
            }
            erosionWriter.Enqueue(new ErosiveEvent {
                idx = idx,
                deltaSediment = sedimentV
            });
        }

        public void WriteSedimentMap(ref NativeQueue<ErosiveEvent> sedimentEvents, int kernelSize, ref NativeArray<float> kernel){
            PileSolver solver = new PileSolver {tile = tile};
            float PILE_THRESHOLD = tile.ep.PILE_THRESHOLD / tile.ep.HEIGHT;
            float MIN_PILE_INCREMENT = tile.ep.MIN_PILE_INCREMENT / tile.ep.HEIGHT;
            solver.Init(tile.ep.PILING_RADIUS);
            ErosiveEvent evt;
            while(sedimentEvents.TryDequeue(out evt)){
                if(evt.deltaSediment < 0f){
                    KernelDisperse(evt.idx, evt.deltaSediment, SEDIMENT_PLACEMENT_DIVISOR, ref tile.height, kernelSize, ref kernel);
                }else{
                    if(evt.deltaSediment <= PILE_THRESHOLD){
                        KernelDisperse(evt.idx, evt.deltaSediment, SEDIMENT_PLACEMENT_DIVISOR, ref tile.height, kernelSize, ref kernel);
                    }else{
                        solver.HandlePile(tile.getPos(evt.idx), evt.deltaSediment, MIN_PILE_INCREMENT);
                    }
                }
            } 
        }

        public void KernelDisperse(int idx, float val, float scalingFactor, ref NativeArray<float> buffer, int kernelSize, ref NativeArray<float> kernel){
            float offset = floor((float) kernelSize / 2f);
            float2 posD = new float2(tile.getPos(idx));
            float kernelFactor = 1f;
            float2 probe = new float2(0);
            for (int x = 0; x < kernelSize; x++){
                for( int z = 0; z < kernelSize; z++){
                    // TODO generalize for other kernels?
                    kernelFactor = kernel[x] * kernel[z];
                    probe.x = posD.x - offset + (float) x;
                    probe.y = posD.y - offset + (float) z;
                    idx = tile.SafeIdx(probe);
                    float last = buffer[idx];
                    float newDiff =  ((val * kernelFactor) / scalingFactor);
                    float nextV = last + newDiff;
                    if(nextV > 1f){continue;} // bad build breaker
                    if(nextV < 0f){continue;} // bad build breaker
                    buffer[idx] = last + newDiff;
                }
            }
        }

        // public void KernelDisperse(int idx, float val, Heading heading, float scalingFactor, ref NativeArray<float> buffer, int kernelSize, ref NativeArray<float> kernel){
        //     int offset = (int)floor(kernelSize / 2);
        //     int2 posD = tile.getPos(idx);
        //     int2 oa;
        //     int2 ob;
        //     int2 probe;
        //     heading.Orthogonal(out oa, out ob);
        //     float kernelFactor = kernel[offset];
        //     float last = buffer[idx];
        //     float diff = (val * kernelFactor) / scalingFactor;
        //     // float nextV = last + diff
        //     buffer[idx] = last + diff;
        //     for(int i = 1; i <= offset; i++){
        //         kernelFactor = kernel[offset + i];
        //         for (int flip = 0; flip < 2; flip++){
        //             if(flip == 0){
        //                 probe = posD + (oa * i);
        //             }else{
        //                 probe = posD + (ob * i);
        //             }
        //             idx = tile.SafeIdx(probe);
        //             last = buffer[idx];
        //             diff = (val * kernelFactor) / scalingFactor;
        //             buffer[idx] = last + diff;
        //         }
        //     }
        // }

        public void Place(int idx, float val, float scalingFactor, ref NativeArray<float> buffer){
            float last = buffer[idx];
            last += val * scalingFactor;
            buffer[idx] = last;
        }
    }
}