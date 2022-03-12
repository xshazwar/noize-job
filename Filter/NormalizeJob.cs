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
    public struct GetMapRangeJob: IJob {

        public const int MIN = 0;
        public const int MAX = 1;
        public const int RANGE = 2;
        [ReadOnly]
        NativeSlice<float> map;
        [WriteOnly]
        NativeArray<float> res;

        float HIGHEST_MIN;
        float LOWEST_MAX;

        public void Execute(){
            float min_ = HIGHEST_MIN;
            float max_ = LOWEST_MAX;
            for (int i = 0; i < map.Length; i++){
                min_ = min(min_, map[i]);
                max_ = max(max_, map[i]);
            }
            res[MIN] = min_;
            res[MAX] = max_;
            res[RANGE] = max_ - min_;
        }

        public JobHandle Schedule(NativeSlice<float> map_, NativeArray<float> res_, JobHandle dep, float lim_min = float.PositiveInfinity, float lim_max = float.NegativeInfinity)
        {
            var job = new GetMapRangeJob();
            job.map = map_;
            job.res = res_;
            job.HIGHEST_MIN = lim_min;
            job.LOWEST_MAX = lim_max;
            return job.Schedule(dep);

        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct MapNormalizeValues<F, RW> : IJobFor
        where F : struct, INormalizeMap
        where RW: struct, IRWTile
    {
        F fn;
        
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        RW data ;
        [ReadOnly]
        NativeSlice<float> args;

        public void Execute (int i) => fn.Execute<RW>(i, data, args);
        public static JobHandle ScheduleParallel (
			NativeSlice<float> src,
            NativeSlice<float> tmp,
            NativeSlice<float> args,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new MapNormalizeValues<F, RW>();
            job.fn.Resolution = resolution;
            job.fn.JobLength = resolution;
            job.args = args;
            job.data.Setup(src, tmp, resolution);
            JobHandle res = job.ScheduleParallel(
                job.fn.JobLength, 8, dependency
			);
            return TileHelpers.SWAP_RWTILE(src, tmp, res);
        }

    }

    public delegate JobHandle MapNormalizeValuesDelegate(
			NativeSlice<float> src,
            NativeSlice<float> tmp,
            NativeSlice<float> args,
            int resolution,
            JobHandle dependency
        );
}
