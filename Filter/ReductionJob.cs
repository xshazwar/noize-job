using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.noize.cpu.mutate {
    using Unity.Mathematics;

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
	public struct ReductionJob<G, DL, DR> : IJobFor
		where G : struct, IReduceTiles
		where DL : struct, IRWTile
        where DR : struct, IReadOnlyTile {

		G generator;

		[NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
		DL dataL;
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        DR dataR;

		public void Execute (int i) => generator.Execute(i, dataL, dataR);

		public static JobHandle ScheduleParallel (
			NativeSlice<float> srcL, //receives output
            NativeSlice<float> srcR,
			NativeSlice<float> tmp, // long lived temporary buffer for rw tiles
            int resolution,
            JobHandle dependency
		) {
			var job = new ReductionJob<G, DL, DR>();
			job.generator.Resolution = resolution;
            job.generator.JobLength = resolution;
			job.dataL.Setup(
				srcL, tmp, resolution
			);
            job.dataR.Setup(
				srcR, resolution
			);
			JobHandle handle = job.ScheduleParallel(
				job.generator.JobLength, 1, dependency
			);
			return TileHelpers.SWAP_RWTILE(srcL, tmp, handle);
		}
	}

	public delegate JobHandle ReductionJobScheduleDelegate (
        NativeSlice<float> srcL, //receives output
        NativeSlice<float> srcR,
        int resolution,
        JobHandle dependency
	);
}