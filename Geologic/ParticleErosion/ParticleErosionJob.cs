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
	public struct ParticlePoolMinimaJob : IJobFor {
        NativeStream.Writer minimaWriter;
        FlowSuperPosition poolJob;

		public void Execute (int z) => poolJob.CreateSuperPositions(z, minimaWriter);

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            int resolution,
            JobHandle dependency
		)
        {
            
            var job = new ParticlePoolMinimaJob {
                minimaWriter = minimaStream.AsWriter()
            };
            job.poolJob = new FlowSuperPosition();
			job.poolJob.SetupCollapse(resolution, flow, heightMap, outMap);

            // no temporary allocations, so no need to dispose
			return job.ScheduleParallel(
                resolution, 16, dependency
			);
		}
	}

    public delegate JobHandle ParticlePoolMinimaJobDelegate(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            int resolution,
            JobHandle dependency
    );

    public struct ParticleWriteMinimas : IJob {
        [ReadOnly]
        NativeStream.Reader streamReader;
        
        [WriteOnly]
        NativeList<int> minimas;

        int res;

        // No idea why this doesn't work...
        // public void Execute () {
        //     int count = streamReader.RemainingItemCount;
        //     for(int n = 0; n < count; n ++){
        //         minimas.Add(streamReader.Read<int>());
        //         Debug.Log($"added {minimas[n]}");
        //     }
        // }

        public void Execute () {
            for (int index = 0; index < res; index++){
                int count = streamReader.BeginForEachIndex(index);
                for(int n = 0; n < count; n ++){
                    minimas.Add(streamReader.Read<int>());
                    // Debug.Log($"added {minimas[n]}");
                }
            }
        }

        public static JobHandle ScheduleRun(
            int res,
            NativeList<int> minimas,
            NativeStream minimaStream,
            JobHandle dep
        ){
            var prereq = new ParticleWriteMinimas {
                streamReader = minimaStream.AsReader(),
                minimas = minimas,
                res = res
            };
            return prereq.Schedule(dep);
        }
    }

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
	public struct ParticlePoolCollapseJob : IJobParallelForDefer
        {
        
        ProfilerMarker profiler_;
        [ReadOnly]
        NativeList<int> minimas;
        NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterBM;
        NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterMB;
        NativeParallelHashMap<int, int>.ParallelWriter catchmentWriter;
        FlowSuperPosition poolJob;

        public void Execute (int index) {
            int minimaIdx = minimas[index];
            poolJob.CollapseMinima(minimaIdx, boundaryWriterBM, boundaryWriterMB, catchmentWriter);
        }

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeList<int> minimas,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
		)
        {
            
            var job = new ParticlePoolCollapseJob {
                minimas = minimas,
                boundaryWriterBM = boundary_BM.AsParallelWriter(),
                boundaryWriterMB = boundary_MB.AsParallelWriter(),
                catchmentWriter = catchmentMap.AsParallelWriter()
            };
            job.poolJob = new FlowSuperPosition();
            ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
            job.profiler_ = marker_;
			job.poolJob.SetupCollapse(resolution, flow, heightMap, outMap);
            // dynamically picks length based on minima count
            return job.Schedule<ParticlePoolCollapseJob, int>(
                    minimas, 1, dependency
			);
		}
	}

    public delegate JobHandle ParticlePoolCollapseJobDelegate(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeList<int> minimas,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
    );

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
	public struct DrainSolvingJob : IJob
        {
        ProfilerMarker profiler;
        NativeParallelMultiHashMap<int, int> boundaryBM;
        NativeParallelMultiHashMap<int, int> boundaryMB;
        NativeParallelHashMap<int, int> catchment;
        NativeParallelHashMap<PoolKey, Pool> pools;
        NativeList<PoolKey> drainKeys;
        NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        FlowSuperPosition poolJob;

		public void Execute () {
            poolJob.SolveDrainHeirarchy(boundaryBM, boundaryMB, catchment, ref pools, drainKeys, ref drainToMinima, profiler);
        }

		public static JobHandle Schedule (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchmentMap,
            NativeParallelHashMap<PoolKey, Pool> pools,
            NativeList<PoolKey> drainKeys,
            NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new DrainSolvingJob {
                boundaryBM = boundary_BM,
                boundaryMB = boundary_MB,
                catchment = catchmentMap,
                pools = pools,
                drainKeys = drainKeys,
                drainToMinima = drainToMinima,
                poolJob = new FlowSuperPosition(),
                profiler = new ProfilerMarker("PoolColapse")
            };
			job.poolJob.SetupPoolGeneration(resolution, heightMap, outMap);

			return job.Schedule(dependency);
		}
	}

    public delegate JobHandle DrainSolvingJobDelegate(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchmentMap,
            NativeParallelHashMap<PoolKey, Pool> pools,
            NativeList<PoolKey> drainKeys,
            NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            int resolution,
            JobHandle dependency
    );

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct PoolCreationJob : IJobParallelForDefer {

        ProfilerMarker profiler;
        
        [ReadOnly]
        NativeArray<PoolKey> drainKeys;
        
        [ReadOnly]
        NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        
        [ReadOnly]
        NativeParallelHashMap<int, int> catchment;
        
        [ReadOnly]
        NativeParallelMultiHashMap<int, int> boundary_MB;
        
        [WriteOnly]
        NativeParallelHashMap<PoolKey, Pool>.ParallelWriter pools;
        FlowSuperPosition poolJob;

        public void Execute(int i){
            poolJob.CreatePoolFromDrain(drainKeys[i], ref drainToMinima, ref catchment, ref boundary_MB, ref pools, profiler);
        }

        public static JobHandle ScheduleParallel(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeList<PoolKey> drainKeys,
            NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            NativeParallelHashMap<int, int> catchment,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<PoolKey, Pool> pools,
            int res,
            JobHandle deps
        ){
            var job = new PoolCreationJob {
                drainKeys = drainKeys.AsDeferredJobArray(),
                drainToMinima = drainToMinima,
                catchment = catchment,
                boundary_MB = boundary_MB,
                pools = pools.AsParallelWriter(),
                poolJob = new FlowSuperPosition(),
                profiler = new ProfilerMarker("probe")
            };
            job.poolJob.SetupPoolGeneration(res, heightMap, outMap);

            return IJobParallelForDeferExtensions.Schedule<PoolCreationJob, PoolKey>(job, drainKeys, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct SolvePoolHeirarchyJob: IJob {

        NativeParallelMultiHashMap<PoolKey, int> drainToMinima;
        NativeParallelHashMap<PoolKey, Pool> pools;
        FlowSuperPosition poolJob;

        public void Execute(){
            poolJob.LinkPoolHeirarchy(ref drainToMinima, ref pools);
        }

        public static JobHandle ScheduleRun(
            NativeParallelMultiHashMap<PoolKey, int> drainToMinima,
            NativeParallelHashMap<PoolKey, Pool> pools,
            JobHandle deps
        ){
            var job = new SolvePoolHeirarchyJob {
                drainToMinima = drainToMinima,
                pools = pools,
                poolJob = new FlowSuperPosition()
            };
            return job.Schedule(deps);
        }

    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct PoolDrawDebugAndCleanUpJob: IJob {

        NativeParallelMultiHashMap<int, int> boundary_BM;
        NativeParallelMultiHashMap<int, int> boundary_MB;
        NativeParallelHashMap<int, int> catchment;
        NativeParallelHashMap<PoolKey, Pool> pools;
        bool paintFor3D;
        FlowSuperPosition poolJob;

        public void Execute(){
            poolJob.PoolDrawDebugAndCleanUp(boundary_BM, boundary_MB, catchment, pools, paintFor3D);
        }

        public static JobHandle ScheduleRun(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelMultiHashMap<int, int> boundary_MB,
            NativeParallelHashMap<int, int> catchment,
            NativeParallelHashMap<PoolKey, Pool> pools,
            int res,
            JobHandle deps,
            bool paintFor3D = false
        ){
            var job = new PoolDrawDebugAndCleanUpJob {
                boundary_BM = boundary_BM,
                boundary_MB = boundary_MB,
                catchment = catchment,
                pools = pools,
                paintFor3D = paintFor3D,
                poolJob = new FlowSuperPosition()
            };
            job.poolJob.SetupPoolGeneration(res, heightMap, outMap);

            return job.Schedule(deps);

        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct UpdatePoolValues: IJob {
        NativeQueue<PoolUpdate> updates;
        
        [NativeDisableContainerSafetyRestriction]
        NativeParallelHashMap<PoolKey, Pool> pools;
        FlowSuperPosition poolJob;

        public void Execute(){
            poolJob.ReducePoolUpdatesAndApply(updates, ref pools);
        }

        public static JobHandle ScheduleRun(
            NativeQueue<PoolUpdate> updates,
            NativeParallelHashMap<PoolKey, Pool> pools,
            JobHandle deps
        ){
            var job = new UpdatePoolValues(){
                updates = updates,
                pools = pools,
                poolJob = new FlowSuperPosition()
            };
            return job.Schedule(deps);
        }

    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct DrawPoolsJob: IJobFor {
        int res;
        [ReadOnly]
        NativeParallelHashMap<int, int> catchment;
        [ReadOnly]
        NativeParallelMultiHashMap<int, int> boundary_BM;
        [ReadOnly]
        NativeParallelHashMap<PoolKey, Pool> pools;
        [ReadOnly]
        NativeSlice<float> heightMap;
        
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        // [WriteOnly]
        NativeSlice<float> poolMap;

        FlowSuperPosition poolJob;

        public void Execute(int z){
            for(int x = 0; x < res; x++){
                poolJob.DrawPoolLocation(x, z, ref catchment, ref boundary_BM, ref pools, ref heightMap, ref poolMap);
            }
        }

        public static JobHandle Schedule(
            NativeSlice<float> poolMap,
            NativeSlice<float> heightMap,
            NativeParallelHashMap<int, int> catchment,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelHashMap<PoolKey, Pool> pools,
            int res,
            JobHandle deps
        ){
            var job = new DrawPoolsJob {
                res = res,
                catchment = catchment,
                boundary_BM = boundary_BM,
                pools = pools,
                heightMap = heightMap,
                poolMap = poolMap,
                poolJob = new FlowSuperPosition {
                    res = new int2(res, res)
                }
            };
            return job.ScheduleParallel(res, 16, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct ErosionCycleMultiThreadJob: IJobFor {
        FlowMaster fm;
        NativeArray<Particle> particles;
        int RND_SEED;
        int MAX_STEPS;

        public void Execute(int i){
            Particle p = particles[i];
            fm.ServiceParticle(ref p, RND_SEED + i, MAX_STEPS);
            particles[i] = p;
        }

        public static JobHandle Schedule(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeArray<Particle> particles,
            NativeQueue<ErosiveEvent> events,
            int eventLimit,
            int res,
            JobHandle deps
        ){
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            var job = new ErosionCycleMultiThreadJob {
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
            return job.ScheduleParallel(particles.Length, particles.Length, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct ErosionCycleBeyerParticleJob: IJob {
        FlowMaster fm;
        NativeArray<BeyerParticle> particles;
        int RND_SEED;
        int MAX_STEPS;

        public void Execute(){
            BeyerParticle p = particles[0];
            fm.ServiceBeyerParticle(ref p, RND_SEED, MAX_STEPS);
            particles[0] = p;
        }

        public static JobHandle ScheduleRun(
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
            var job = new ErosionCycleBeyerParticleJob {
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
            return job.Schedule(deps);
        }
    }


    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct ErosionCycleSingleThreadJob: IJob {
        FlowMaster fm;
        NativeArray<Particle> particles;
        int RND_SEED;
        int MAX_STEPS;

        public void Execute(){
            Particle p = particles[0];
            fm.ServiceParticleSingle(ref p, RND_SEED, MAX_STEPS);
            particles[0] = p;
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeArray<Particle> particles,
            NativeQueue<ErosiveEvent> events,
            int eventLimit,
            int res,
            JobHandle deps
        ){
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            var job = new ErosionCycleSingleThreadJob {
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
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct UpdateFlowFromTrackJob: IJobFor {
        WorldTile tile;
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
    public struct ProcessErosiveEventsJob: IJob {

        NativeQueue<PoolUpdate> poolUpdates;
        NativeParallelHashMap<int, int> catchment;
        FlowMaster fm;

        public void Execute(){
            ErosiveEvent evt = new ErosiveEvent();
            while(fm.events.TryDequeue(out evt)){
                fm.CommitUpdateToMaps(evt, ref poolUpdates, ref catchment);
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeQueue<ErosiveEvent> events,
            NativeQueue<PoolUpdate> poolUpdates,
            NativeParallelHashMap<int, int> catchment,
            int res,
            JobHandle deps
        ){
            var job = new ProcessErosiveEventsJob {
                poolUpdates = poolUpdates,
                catchment = catchment,
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

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct WriteErosionMaps: IJob {
        // Convenience wrapper for these interlocked jobs
        public void Execute(){}

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeQueue<ErosiveEvent> events,
            NativeQueue<PoolUpdate> poolUpdates,
            NativeParallelHashMap<int, int> catchment,
            NativeParallelMultiHashMap<int, int> boundary_BM,
            NativeParallelHashMap<PoolKey, Pool> pools,
            int res,
            JobHandle deps
        ){
            JobHandle writeFlowMap = UpdateFlowFromTrackJob.Schedule(pool, flow, track, res, deps);
            JobHandle updatePoolsJob = UpdatePoolValues.ScheduleRun(poolUpdates, pools, deps);
            JobHandle writePoolMap = DrawPoolsJob.Schedule(
                new NativeSlice<float>(pool),
                new NativeSlice<float>(height),
                catchment,
                boundary_BM,
                pools,
                res,
                updatePoolsJob
            );
            return JobHandle.CombineDependencies(writePoolMap, writeFlowMap);
        }
    }

    // [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    // public struct PoolInterpolationDebugJob: IJob {
    //     [ReadOnly]
    //     NativeParallelHashMap<PoolKey, Pool> pools;

    //     public void Execute(){
    //         NativeArray<PoolKey> keys = pools.GetKeyArray(Allocator.Temp);
    //         Pool pool = new Pool();
    //         float val = 0f;
    //         for(int x = 0; x < keys.Length; x++){
    //             pools.TryGetValue(keys[x], out pool);
    //             Debug.Log($"{pool.minimaHeight} -> {pool.drainHeight} @{pool.memberCount} b1: {pool.b1}, b2:{pool.b2} full {pool.volume / pool.capacity}% of {pool.capacity}");
                
    //             // pool.EstimateHeight(pool.minimaHeight, out val);
    //             // Debug.Log($"{val} @ empty == {val - pool.minimaHeight}");
    //             // pool.volume = pool.capacity;
    //             // pool.EstimateHeight(pool.minimaHeight, out val);
    //             // Debug.Log($"{val} @ full == {val - pool.drainHeight}");
    //         }
    //     }

    //     public static JobHandle ScheduleJob(
    //         NativeParallelHashMap<PoolKey, Pool> pools,
    //         JobHandle deps
    //     ){
    //         var job = new PoolInterpolationDebugJob {
    //             pools = pools
    //         };
    //         return job.Schedule(deps);
    //     }
    // }

}