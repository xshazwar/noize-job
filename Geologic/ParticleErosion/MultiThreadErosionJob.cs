using System;

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

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct BeyerCycleMultiThreadJob: IJobParallelFor {
        FlowMaster fm;
        NativeArray<BeyerParticle> particles;
        int RND_SEED;
        int MAX_STEPS;

        public void Execute(int i){
            BeyerParticle p = particles[i];
            fm.BeyerSimultaneousDescent(ref p, RND_SEED + i, MAX_STEPS);
            particles[i] = p;
        }

        public static JobHandle ScheduleParallel(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeArray<BeyerParticle> particles,
            NativeQueue<ErosiveEvent> events,
            int eventLimit,
            int res,
            JobHandle deps
        ){
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            var job = new BeyerCycleMultiThreadJob {
                fm = new FlowMaster {
                    tile = new WorldTile {
                        res = new int2(res, res),
                        height = height,
                        pool = pool,
                        flow = flow,
                        track = track
                    },
                    events = events,
                    eventWriter = events.AsParallelWriter()
                },
                particles = particles,
                RND_SEED = seed,
                MAX_STEPS = eventLimit
            };
            return job.Schedule<BeyerCycleMultiThreadJob>(particles.Length, 1, deps);
        }
    }

    // [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    // public struct ErosionCycleBeyerParticleJob: IJob {
    //     FlowMaster fm;
    //     NativeArray<BeyerParticle> particles;
    //     int RND_SEED;
    //     int MAX_STEPS;

    //     public void Execute(){
    //         BeyerParticle p = particles[0];
    //         fm.ServiceBeyerParticle(ref p, RND_SEED, MAX_STEPS);
    //         particles[0] = p;
    //     }

    //     public static JobHandle ScheduleRun(
    //         NativeArray<float> height,
    //         NativeArray<float> pool,
    //         NativeArray<float> flow,
    //         NativeArray<float> track,
    //         NativeArray<BeyerParticle> particles,
    //         NativeQueue<ErosiveEvent> events,
    //         int eventLimit,
    //         int res,
    //         JobHandle deps
    //     ){
    //         int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
    //         var job = new ErosionCycleBeyerParticleJob {
    //             fm = new FlowMaster {
    //                 tile = new WorldTile {
    //                     res = new int2(res, res),
    //                     height = height,
    //                     pool = pool,
    //                     flow = flow,
    //                     track = track
    //                 },
    //                 events = events,
    //                 eventWriter = events.AsParallelWriter()
    //             },
    //             particles = particles,
    //             RND_SEED = seed,
    //             MAX_STEPS = eventLimit
    //         };
    //         return job.Schedule(deps);
    //     }
    // }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct UpdateFlowFromTrackJob: IJobFor {
        WorldTile tile;
        int flip;
        int res;

        public void Execute(int z){
            for (int x = 0; x < res; x++){
                tile.UpdateFlowMapFromTrack(x, z);
            }
        }

        public static JobHandle Schedule(
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            int res,
            JobHandle deps
        ){
            var job = new UpdateFlowFromTrackJob(){
                res = res,
                tile = new WorldTile {
                    pool = pool,
                    flow = flow,
                    track = track,
                    res = new int2(res, res)
                }
            };
            return job.ScheduleParallel(res, res, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct PoolAutomataJob: IJobFor {
        WorldTile tile;
        int res;
        int flip;

        public void Execute (int z) {
            NativeArray<FloodedNeighbor> buff = new NativeArray<FloodedNeighbor>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int offset = 0;
            z *= 2;
            if(flip != 0){
                z += 1;
            }else{
                offset = 1;
            }
            // Debug.Log($"z{z} flip: {flip}, od {offset}");
            for (int x = offset; x < res; x += 2){
                tile.SpreadPool(x, z, ref buff);
                // if(tile.pool[tile.getIdx(x,z)] > 0f) {
                    
                // }
            }
        }

        public static JobHandle Schedule(
            NativeArray<float> pool,
            NativeArray<float> height,
            int iterations,
            int res,
            JobHandle deps
        ){
            JobHandle handle = deps;
            var job = new PoolAutomataJob(){
                res = res,
                tile = new WorldTile {
                    pool = pool,
                    height = height,
                    res = new int2(res, res)
                }
            };
            for (int i = 0; i < iterations; i++){
                for(int flipflop = 0; flipflop <= 1; flipflop++){
                    job.flip = flipflop;
                    handle = job.ScheduleParallel(
                        (int) (res / 2) , 10, handle
                    );
                }
            }
            return handle;
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ProcessBeyerErosiveEventsJob: IJob {

        FlowMaster fm;
 
        public void Execute(){
            ErosiveEvent evt = new ErosiveEvent();
            NativeArray<float> kernel3 = new NativeArray<float>(3, Allocator.Temp){
                [0] = 0.30780132912347f,
                [1] = 0.38439734175306006f,
                [2] = 0.30780132912347f
            };
            NativeArray<float> kernel5 = new NativeArray<float>(5, Allocator.Temp){
                [0] = 0.12007838424321349f,
                [1] = 0.23388075658535032f,
                [2] = 0.29208171834287244f,
                [3] = 0.23388075658535032f,
                [4] = 0.12007838424321349f
            };
            while(fm.events.TryDequeue(out evt)){
                fm.CommitBeyerUpdateToMaps(evt, ref kernel3, ref kernel5);
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeQueue<ErosiveEvent> events,
            int res,
            JobHandle deps
        ){
            var job = new ProcessBeyerErosiveEventsJob {
                fm = new FlowMaster {
                    tile = new WorldTile {
                        res = new int2(res, res),
                        height = height,
                        pool = pool,
                        flow = flow,
                        track = track
                    },
                    events = events
                }
            };
            return job.Schedule(deps);
        }
    }
}