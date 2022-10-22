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

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct FillBeyerQueueJob: IJobParallelFor {
        FlowMaster fm;
        [NativeDisableContainerSafetyRestriction]
        NativeQueue<BeyerParticle>.ParallelWriter particleWriter;
        int RND_SEED;
        int COUNT;

        public void Execute(int i){
            // Debug.Log($"generating {COUNT} particles with random position");
            fm.CreateRandomParticles(COUNT, RND_SEED + i, ref particleWriter);
        }

        public static JobHandle ScheduleParallel(
            NativeQueue<BeyerParticle> particles,
            int res,
            int maxParticles,
            JobHandle deps
        ){
            int threads = 10;
            int currentParticles = particles.Count;
            // Debug.Log($"current Particles {currentParticles}");
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            int required = maxParticles - currentParticles;
            var job = new FillBeyerQueueJob {
                fm = new FlowMaster {
                    tile = new WorldTile {
                        res = new int2(res, res)
                    }
                },
                particleWriter = particles.AsParallelWriter(),
                RND_SEED = seed,
                COUNT = (int) floor(required / 10)
            };
            return job.Schedule<FillBeyerQueueJob>(threads, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct CopyBeyerQueueJob: IJob {
        NativeList<BeyerParticle> particles;

        [NativeDisableContainerSafetyRestriction]
        NativeQueue<BeyerParticle> particleQueue;

        public void Execute(){
            NativeArray<BeyerParticle> temp = particleQueue.ToArray(Allocator.Temp);
            particles.CopyFrom(temp);
        }

        public static JobHandle ScheduleRun(
            NativeList<BeyerParticle> particles,
            NativeQueue<BeyerParticle> particleQueue,
            JobHandle deps
        ){
            
            var job = new CopyBeyerQueueJob {
                particles = particles,
                particleQueue = particleQueue
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ClearBeyerQueueJob: IJob {
        [NativeDisableContainerSafetyRestriction]
        NativeQueue<BeyerParticle> particleQueue;

        public void Execute(){
            particleQueue.Clear();
        }

        public static JobHandle ScheduleRun(
            NativeQueue<BeyerParticle> particleQueue,
            JobHandle deps
        ){
            
            var job = new ClearBeyerQueueJob {
                particleQueue = particleQueue
            };
            return job.Schedule(deps);
        }
    }

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

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct QueuedBeyerCycleMultiThreadJob: IJobParallelForDefer {
        FlowMaster fm;
        [ReadOnly]
        NativeArray<BeyerParticle> particles;

        public void Execute(int i){
            BeyerParticle p = particles[i];
            fm.BeyerSimultaneousDescentSingle(ref p);
            particles[i] = p;
        }

        public static JobHandle ScheduleParallel(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeList<BeyerParticle> particles,
            NativeQueue<ErosiveEvent> events,
            int eventLimit,
            int res,
            JobHandle deps
        ){
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            var job = new QueuedBeyerCycleMultiThreadJob {
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
                particles = particles.AsDeferredJobArray()
            };
            return job.Schedule<QueuedBeyerCycleMultiThreadJob, BeyerParticle>(particles, 1, deps);
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
            return job.ScheduleParallel(res, 1, deps);
        }
    }

    // [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    // public struct PoolAutomataJob: IJobFor {
    //     WorldTile tile;
    //     int res;
    //     int xoff;
    //     int zoff;

    //     public void Execute (int z) {
    //         NativeArray<FloodedNeighbor> buff = new NativeArray<FloodedNeighbor>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
    //         int offset = xoff;
    //         if(z % 2 != 0){
    //             offset += 1;
    //         }
    //         z *= 2;
    //         z += zoff;
    //         for (int x = offset; x < res; x += 2){
    //             if(tile.pool[tile.getIdx(x,z)] > 0f) {
    //                 tile.SpreadPool(x, z, ref buff);
    //             }
    //         }
    //     }

    //     public static JobHandle Schedule(
    //         NativeArray<float> pool,
    //         NativeArray<float> height,
    //         int iterations,
    //         int res,
    //         JobHandle deps
    //     ){
    //         JobHandle handle = deps;
    //         var job = new PoolAutomataJob(){
    //             res = res,
    //             tile = new WorldTile {
    //                 pool = pool,
    //                 height = height,
    //                 res = new int2(res, res)
    //             }
    //         };
    //         for (int i = 0; i < iterations; i++){
    //             for(int xoff = 0; xoff < 2; xoff++){
    //                 for(int zoff = 0; zoff < 2; zoff ++){
    //                     job.xoff = xoff;
    //                     job.zoff = zoff;
    //                     handle = job.ScheduleParallel(
    //                         (int) (res / 2) , 1, handle
    //                     );
    //                 }  
    //             }
    //         }
    //         return handle;
    //     }
    // }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct PoolAutomataJob: IJobFor {
        WorldTile tile;
        [NativeDisableContainerSafetyRestriction]
        NativeQueue<BeyerParticle>.ParallelWriter particleWriter;
        int res;
        int xoff;
        int zoff;

        public void Execute (int z) {
            NativeArray<FloodedNeighbor> buff = new NativeArray<FloodedNeighbor>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int offset = xoff;
            if(z % 2 != 0){
                offset += 1;
            }
            z *= 2;
            z += zoff;
            for (int x = offset; x < res; x += 2){
                if(tile.pool[tile.getIdx(x,z)] > 0f) {
                    tile.SpreadPool(x, z, ref buff, ref particleWriter);
                }
            }
        }

        public static JobHandle Schedule(
            NativeArray<float> pool,
            NativeArray<float> height,
            NativeQueue<BeyerParticle> particleQueue,
            int iterations,
            int res,
            JobHandle deps
        ){
            JobHandle handle = deps;
            var job = new PoolAutomataJob(){
                res = res,
                particleWriter = particleQueue.AsParallelWriter(),
                tile = new WorldTile {
                    pool = pool,
                    height = height,
                    res = new int2(res, res)
                }
            };
            for (int i = 0; i < iterations; i++){
                for(int xoff = 0; xoff < 2; xoff++){
                    for(int zoff = 0; zoff < 2; zoff ++){
                        job.xoff = xoff;
                        job.zoff = zoff;
                        handle = job.ScheduleParallel(
                            (int) (res / 2) , 1, handle
                        );
                    }  
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