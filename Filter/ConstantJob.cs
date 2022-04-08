using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {
    using Unity.Mathematics;

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
	public struct ConstantJob<G, DL> : IJobFor
		where G : struct, IConstantTiles
		where DL : struct, IRWTile{

		G generator;

		[NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
		DL dataL;

		public void Execute (int i) => generator.Execute(i, dataL);

		public static JobHandle ScheduleParallel (
			NativeSlice<float> srcL, //receives output
			NativeSlice<float> tmp, // long lived temporary buffer for rw tiles
            float constantValue,
            int resolution,
            JobHandle dependency
		) {
			var job = new ConstantJob<G, DL>();
			job.generator.Resolution = resolution;
            job.generator.JobLength = resolution;
            job.generator.ConstantValue = constantValue;
			job.dataL.Setup(
				srcL, tmp, resolution
			);
			JobHandle handle = job.ScheduleParallel(
				job.generator.JobLength, 1, dependency
			);
			return TileHelpers.SWAP_RWTILE(srcL, tmp, handle);
		}
	}

	public delegate JobHandle ConstantJobScheduleDelegate (
        NativeSlice<float> srcL, //receives output
        NativeSlice<float> tmp, // long lived temporary buffer for rw tiles
        float constantValue,
        int resolution,
        JobHandle dependency
	);
}