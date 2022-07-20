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
            Debug.Log($"{index}");
            poolJob.CollapseMinima(minimaIdx, boundaryWriterBM, boundaryWriterMB, catchmentWriter);
        }

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeList<int> minimas,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
		)
        {
            
            var job = new ParticlePoolCollapseJob {
                minimas = minimas,
                boundaryWriterBM = boundaryMapMemberToMinima.AsParallelWriter(),
                boundaryWriterMB = boundaryMapMinimaToMembers.AsParallelWriter(),
                catchmentWriter = catchmentMap.AsParallelWriter()
            };
            job.poolJob = new FlowSuperPosition();
            ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
            job.profiler_ = marker_;
			job.poolJob.SetupCollapse(resolution, flow, heightMap, outMap);
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
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
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
        UnsafeParallelHashMap<PoolKey, Pool> pools;
        FlowSuperPosition poolJob;

		public void Execute () {
            poolJob.SolveDrainHeirarchy(boundaryBM, boundaryMB, catchment, pools, profiler);
        }

		public static JobHandle Schedule (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            UnsafeParallelHashMap<PoolKey, Pool> pools,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new DrainSolvingJob {
                boundaryBM = boundaryMapMemberToMinima,
                boundaryMB = boundaryMapMinimaToMembers,
                catchment = catchmentMap,
                pools = pools
            };
            job.poolJob = new FlowSuperPosition();
            ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
            job.profiler = marker_;
			job.poolJob.SetupPoolGeneration(resolution, heightMap, outMap);

			return job.Schedule(dependency);
		}
	}

    public delegate JobHandle DrainSolvingJobDelegate(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            UnsafeParallelHashMap<PoolKey, Pool> pools,
            int resolution,
            JobHandle dependency
    );

}