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
	public struct ParticlePoolMinimaJob<J> : IJobFor
        where J : struct, IPoolSuperPosition
        {
        NativeStream.Writer minimaWriter;
        J poolJob;

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
            
            var job = new ParticlePoolMinimaJob<J> {
                minimaWriter = minimaStream.AsWriter()
            };
            job.poolJob = new J();
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

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
	public struct ParticlePoolCollapseJob<J> : IJobFor
        where J : struct, IPoolSuperPosition
        {
        
        ProfilerMarker profiler_;
        
        [ReadOnly]
        NativeStream.Reader streamReader;
        NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterBM;
        NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterMB;
        NativeParallelHashMap<int, int>.ParallelWriter catchmentWriter;
        J poolJob;

		public void Execute (int index) {
            int count = streamReader.BeginForEachIndex(index);
            for(int n = 0; n < count; n ++){
                int minimaIdx = streamReader.Read<int>();
                poolJob.CollapseMinima(minimaIdx, boundaryWriterBM, boundaryWriterMB, catchmentWriter);
            }
            streamReader.EndForEachIndex();
        }

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
		)
        {
            
            var job = new ParticlePoolCollapseJob<J> {
                streamReader = minimaStream.AsReader(),
                boundaryWriterBM = boundaryMapMemberToMinima.AsParallelWriter(),
                boundaryWriterMB = boundaryMapMinimaToMembers.AsParallelWriter(),
                catchmentWriter = catchmentMap.AsParallelWriter()
            };
            job.poolJob = new J();
            ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
            job.profiler_ = marker_;
			job.poolJob.SetupCollapse(resolution, flow, heightMap, outMap);

			return job.ScheduleParallel(
                resolution, 1, dependency
			);
		}
	}

    public delegate JobHandle ParticlePoolCollapseJobDelegate(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
    );

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
	public struct PoolCreationJob<J> : IJob
        where J : struct, IPoolSuperPosition
        {
        // ProfilerMarker profiler;
        NativeParallelMultiHashMap<int, int> boundaryBM;
        NativeParallelMultiHashMap<int, int> boundaryMB;
        NativeParallelHashMap<int, int> catchment;
        J poolJob;

		public void Execute () {
            poolJob.SolvePoolHeirarchy(boundaryBM, boundaryMB, catchment);
        }

		public static JobHandle Schedule (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new PoolCreationJob<J> {
                boundaryBM = boundaryMapMemberToMinima,
                boundaryMB = boundaryMapMinimaToMembers,
                catchment = catchmentMap
            };
            job.poolJob = new J();
            // ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
            // job.profiler = marker_;
			job.poolJob.SetupPoolGeneration(resolution, heightMap, outMap);

			return job.Schedule(dependency);
		}
	}

    public delegate JobHandle PoolCreationJobDelegate(
            NativeSlice<float> heightMap,
            NativeSlice<float> outMap,
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap,
            int resolution,
            JobHandle dependency
    );

}