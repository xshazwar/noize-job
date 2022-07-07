using Unity.Collections.LowLevel.Unsafe;

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
	public struct ParticlePoolMinimaJob<J> : IJobFor
        where J : struct, IPoolSuperPosition
        {
        J poolJob;

		public void Execute (int z) => poolJob.CreateSuperPositions(z);

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            int resolution,
            JobHandle dependency
		)
        {
            // NativeStream minimaStream = new NativeStream(resolution, Allocator.Persistent);
            
            var job = new ParticlePoolMinimaJob<J>();
            job.poolJob = new J { minimaStream = minimaStream };
			job.poolJob.Setup(flow, heightMap, resolution);

            // no temporary allocations, so no need to dispose
			return job.ScheduleParallel(
                resolution, 16, dependency
			);
		}
	}

    public delegate JobHandle ParticlePoolMinimaJobDelegate(
            NativeSlice<float> heightMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            int resolution,
            JobHandle dependency
    );

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
	public struct ParticlePoolCollapse<J> : IJobFor
        where J : struct, IPoolSuperPosition
        {
        NativeStream.Reader streamReader;
        J poolJob;

		public void Execute (int index) {
            int count = streamReader.BeginForEachIndex(index);
            for(int n = 0; n < count; n ++){
                int minimaIdx = streamReader.Read<int>();
                poolJob.CollapseMinima(minimaIdx);
            }
            streamReader.EndForEachIndex();
        }

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> heightMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            int resolution,
            JobHandle dependency
		)
        {
            // NativeStream minimaStream = new NativeStream(resolution, Allocator.Persistent);
            
            var job = new ParticlePoolCollapse<J> { streamReader = minimaStream.AsReader()};
            job.poolJob = new J { minimaStream = minimaStream };
			job.poolJob.Setup(flow, heightMap, resolution);

			return job.ScheduleParallel(
                minimaStream.ForEachCount, 1, dependency
			);
		}
	}

    public delegate JobHandle ParticlePoolCollapseJobDelegate(
            NativeSlice<float> heightMap,
            NativeArray<Cardinal> flow,
            NativeStream minimaStream,
            int resolution,
            JobHandle dependency
    );

}