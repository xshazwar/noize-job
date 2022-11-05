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
        ErosionParameters ep;

        int RND_SEED;
        int COUNT;

        public void Execute(int i){
            fm.CreateRandomParticles(COUNT, RND_SEED + i, ep, ref particleWriter);
        }

        public static JobHandle ScheduleParallel(
            NativeQueue<BeyerParticle> particles,
            ErosionParameters ep,
            int res,
            int maxParticles,
            JobHandle deps,
            int concurrency = 10
        ){
            int threads = concurrency;
            int currentParticles = particles.Count;
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            int required = max(1000, maxParticles - currentParticles);
            var job = new FillBeyerQueueJob {
                fm = new FlowMaster {
                    tile = new WorldTile {
                        ep = ep
                    }
                },
                ep = ep,
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
    public struct TestPileSolverJob: IJob {
        
        // [NativeDisableContainerSafetyRestriction]
        PileSolver solver;

        public void Execute(){
            solver.Init(50);
            for(int x = 50; x < 250; x+= 25){
                for(int z = 50; z < 250; z +=25){
                    solver.HandlePile(new int2(x, z), (((float)x) / (float)(5))  , 0.0005f);
                }
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            ErosionParameters ep,
            JobHandle deps
        ){
            
            var job = new TestPileSolverJob {
                solver = new PileSolver{
                tile = new WorldTile {
                        ep = ep,
                        height = height
                    }
                }
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ClearQueueJob<T>: IJob where T: struct{
        [NativeDisableContainerSafetyRestriction]
        NativeQueue<T> queue;

        public void Execute(){
            queue.Clear();
        }

        public static JobHandle ScheduleRun(
            NativeQueue<T> queue,
            JobHandle deps
        ){
            
            var job = new ClearQueueJob<T> {
                queue = queue
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ClearMultiDict<K, V>: IJob where K: struct, IEquatable<K> where V: struct{
        [NativeDisableContainerSafetyRestriction]
        NativeParallelMultiHashMap<K, V> dict;

        public void Execute(){
            dict.Clear();
        }

        public static JobHandle ScheduleRun(
            NativeParallelMultiHashMap<K, V> dict,
            JobHandle deps
        ){
            
            var job = new ClearMultiDict<K, V> {
                dict = dict
            };
            return job.Schedule(deps);
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
        }

        public static JobHandle ScheduleParallel(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeList<BeyerParticle> particles,
            // NativeQueue<ErosiveEvent> events,
            NativeParallelMultiHashMap<int, ErosiveEvent> events,
            ErosionParameters ep,
            int eventLimit,
            int res,
            JobHandle deps
        ){
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            var job = new QueuedBeyerCycleMultiThreadJob {
                fm = new FlowMaster {
                    tile = new WorldTile {
                        ep = ep,
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
            ErosionParameters ep,
            int res,
            JobHandle deps
        ){
            var job = new UpdateFlowFromTrackJob(){
                res = res,
                tile = new WorldTile {
                    ep = ep,
                    pool = pool,
                    flow = flow,
                    track = track
                    // res = new int2(res, res)
                }
            };
            return job.ScheduleParallel(res, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct PoolAutomataJob: IJobFor {
        WorldTile tile;
        [NativeDisableContainerSafetyRestriction]
        NativeQueue<BeyerParticle>.ParallelWriter particleWriter;
        int res;
        int xoff;
        int zoff;
        bool drainParticles;

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
                    tile.SpreadPool(x, z, ref buff, ref particleWriter, drainParticles);
                }
            }
        }

        public static JobHandle Schedule(
            NativeArray<float> pool,
            NativeArray<float> height,
            NativeQueue<BeyerParticle> particleQueue,
            ErosionParameters ep,
            int iterations,
            int res,
            bool drainParticles,
            JobHandle deps
        ){
            JobHandle handle = deps;
            var job = new PoolAutomataJob(){
                res = res,
                drainParticles = drainParticles,
                particleWriter = particleQueue.AsParallelWriter(),
                tile = new WorldTile {
                    ep = ep,
                    pool = pool,
                    height = height
                    // res = new int2(res, res)
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
    public struct ProcessBeyerErosiveEventsJob: IJobFor {

        NativeQueue<ErosiveEvent>.ParallelWriter erosionWriter;
        FlowMaster fm;
        int res;
 
        public void Execute(int z){
            int idx = 0;
            float poolV = 0f;
            float trackV = 0f;
            float sedimentV = 0f;
            NativeParallelMultiHashMap<int, ErosiveEvent>.Enumerator eventIter;
            for (int x = 0; x < res; x++){
                poolV = 0f;
                trackV = 0f;
                sedimentV = 0f;
                idx = x * res + z;
                eventIter = fm.events.GetValuesForKey(idx);
                while(eventIter.MoveNext()){
                    fm.CombineBeyerEvents(eventIter.Current, ref poolV, ref trackV, ref sedimentV);
                }
                fm.HandleBeyerEvent(idx, poolV, trackV, sedimentV, ref erosionWriter);
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeQueue<ErosiveEvent> erosions,
            NativeParallelMultiHashMap<int, ErosiveEvent> events,
            ErosionParameters ep,
            int res,
            JobHandle deps
        ){
            var job = new ProcessBeyerErosiveEventsJob {
                res = res,
                erosionWriter = erosions.AsParallelWriter(),
                fm = new FlowMaster {
                    tile = new WorldTile {
                        ep = ep,
                        height = height,
                        pool = pool,
                        flow = flow,
                        track = track
                    },
                    events = events
                }
            };
            return job.ScheduleParallel(res, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ErodeHeightMaps: IJob {

        FlowMaster fm;
        NativeQueue<ErosiveEvent> erosions;
 
        public void Execute(){
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
            fm.WriteSedimentMap(ref erosions, 5, ref kernel5);
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeQueue<ErosiveEvent> erosions,
            ErosionParameters ep,
            int res,
            JobHandle deps
        ){
            var job = new ErodeHeightMaps {
                erosions = erosions,
                fm = new FlowMaster {
                    tile = new WorldTile {
                        // res = new int2(res, res),
                        ep = ep,
                        height = height
                    }
                }
            };
            return job.Schedule(deps);
        }
    }

    // [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    // public struct ProcessBeyerErosiveEventsJob: IJob {

    //     ProfilerMarker profiler_;
    //     FlowMaster fm;
 
    //     public void Execute(){
    //         NativeParallelHashMap<int, ErosiveEvent> erosions = new NativeParallelHashMap<int, ErosiveEvent>(fm.tile.ep.TILE_RES.x * fm.tile.ep.TILE_RES.y, Allocator.Temp);
            
    //         ErosiveEvent evt = new ErosiveEvent();
    //         NativeArray<float> kernel3 = new NativeArray<float>(3, Allocator.Temp){
    //             [0] = 0.30780132912347f,
    //             [1] = 0.38439734175306006f,
    //             [2] = 0.30780132912347f
    //         };
    //         NativeArray<float> kernel5 = new NativeArray<float>(5, Allocator.Temp){
    //             [0] = 0.12007838424321349f,
    //             [1] = 0.23388075658535032f,
    //             [2] = 0.29208171834287244f,
    //             [3] = 0.23388075658535032f,
    //             [4] = 0.12007838424321349f
    //         };
    //         // while(fm.events.TryDequeue(out evt)){
    //         //     fm.CommitBeyerUpdateToMaps(evt, ref kernel3, ref kernel5);
    //         // }
    //         // float erode = 0f;
    //         float deposit = 0f;
    //         profiler_.Begin();
    //         while(fm.events.TryDequeue(out evt)){
    //             fm.HandleBeyerEvent(ref evt, ref erosions, ref kernel3, ref deposit);
    //         }
    //         profiler_.End();
    //         // Debug.Log($"total erosion {erode} || total deposition {deposit}");
    //         // Debug.Log($"total deposition {deposit}");
    //         fm.ProcessErosivePiles(ref erosions);
    //     }

    //     public static JobHandle ScheduleRun(
    //         NativeArray<float> height,
    //         NativeArray<float> pool,
    //         NativeArray<float> flow,
    //         NativeArray<float> track,
    //         NativeQueue<ErosiveEvent> events,
    //         ErosionParameters ep,
    //         int res,
    //         JobHandle deps
    //     ){
    //         ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
    //         var job = new ProcessBeyerErosiveEventsJob {
    //             profiler_ = marker_,
    //             fm = new FlowMaster {
    //                 tile = new WorldTile {
    //                     // res = new int2(res, res),
    //                     ep = ep,
    //                     height = height,
    //                     pool = pool,
    //                     flow = flow,
    //                     track = track
    //                 },
    //                 events = events
    //             }
    //         };
    //         return job.Schedule(deps);
    //     }
    // }
}