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
			job.poolJob.Setup(flow, heightMap, outMap, resolution);

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
        
        ProfilerMarker profiler;
        
        [ReadOnly]
        NativeStream.Reader streamReader;
        J poolJob;

		public void Execute (int index) {
            int count = streamReader.BeginForEachIndex(index);
            for(int n = 0; n < count; n ++){
                int minimaIdx = streamReader.Read<int>();
                poolJob.CollapseMinima(minimaIdx, profiler = profiler);
            }
            streamReader.EndForEachIndex();
        }

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
            
            var job = new ParticlePoolCollapseJob<J> { streamReader = minimaStream.AsReader()};
            job.poolJob = new J();
            ProfilerMarker marker_ = new ProfilerMarker("PoolColapse");
            job.profiler = marker_;
			job.poolJob.Setup(flow, heightMap, outMap, resolution);

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
            int resolution,
            JobHandle dependency
    );

}