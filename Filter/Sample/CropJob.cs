using System;

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
    public struct CropJob<RO, WO> : IJobFor
        where RO: struct, IReadOnlyTile
        where WO: struct, IWriteOnlyTile
    {
        
        int InputResolution;
        int OutputResolution;
        int Offset;
        
        [ReadOnly]
        RO input;
        
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        WO data ;

        public void Execute (int z) {
            int zr = z + Offset;
            for (int x = 0; x < OutputResolution; x ++){
                data.SetValue(
                    x, z, input.GetData(x + Offset, zr)
                );
            }
        }
        public static JobHandle ScheduleParallel (
			NativeSlice<float> input,
            int inputResolution,
            NativeSlice<float> output,
            int outputResolution,
            JobHandle dependency
		)
        {
            var job = new CropJob<RO, WO>();
            job.InputResolution = inputResolution;
            job.OutputResolution = outputResolution;
            job.input.Setup(input, inputResolution);
            job.data.Setup(output, outputResolution);
            return job.ScheduleParallel(
                outputResolution, 1, dependency
			);
        }

    }

    public delegate JobHandle CropJobDelegate(
			NativeSlice<float> input,
            int inputResolution,
            NativeSlice<float> output,
            int outputResolution,
            JobHandle dependency
        );
}
