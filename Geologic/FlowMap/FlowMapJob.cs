using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.processing.cpu.mutate {
    using Unity.Mathematics;

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
	public struct FlowMapStepComputeFlow<F, RO, RW> : IJobFor
        where F : struct, IComputeFlowData
		where RO : struct, IReadOnlyTile
        where RW : struct, IRWTile
        {
		F flowOperator;

        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
		RO height;
        RO water;
        RW flowN;
        RW flowS;
        RW flowE;
        RW flowW;


		public void Execute (int i) => flowOperator.Execute<RO, RW>(i, height, water, flowN, flowS, flowE, flowW);

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> src,  
            // compute outflow this is RO
            NativeSlice<float> waterMap,
            // compute outflow then these are RW
            NativeSlice<float> flowMapN,
            NativeSlice<float> flowMapN__buff,
            NativeSlice<float> flowMapS,
            NativeSlice<float> flowMapS__buff,
            NativeSlice<float> flowMapE,
            NativeSlice<float> flowMapE__buff,
            NativeSlice<float> flowMapW,
            NativeSlice<float> flowMapW__buff,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new FlowMapStepComputeFlow<F, RO, RW>();
			job.height.Setup(
				src, resolution
			);
            job.flowOperator.Resolution = resolution;
            job.flowOperator.JobLength = resolution;

            job.water.Setup(waterMap, resolution);

            job.flowN.Setup(flowMapN, flowMapN__buff, resolution);
            job.flowS.Setup(flowMapS, flowMapS__buff, resolution);
            job.flowE.Setup(flowMapE, flowMapE__buff, resolution);
            job.flowW.Setup(flowMapW, flowMapW__buff, resolution);

            // no temporary allocations, so no need to dispose
			JobHandle res = job.ScheduleParallel(
                resolution, 1, dependency
			);

            return TileHelpers.SWAP_RWTILE(flowMapW, flowMapW__buff,
                TileHelpers.SWAP_RWTILE(flowMapE, flowMapE__buff,
                    TileHelpers.SWAP_RWTILE(flowMapS, flowMapS__buff,
                        TileHelpers.SWAP_RWTILE(flowMapN, flowMapN__buff, res))));
             
		}
	}

    public delegate JobHandle FlowMapStepComputeFlowDelegate(
        // compute outflow this is RO
        NativeSlice<float> src,  
        // compute outflow this is RO
        NativeSlice<float> waterMap,
        // compute outflow then these are RW
        NativeSlice<float> flowMapN,
        NativeSlice<float> flowMapN__buff,
        NativeSlice<float> flowMapS,
        NativeSlice<float> flowMapS__buff,
        NativeSlice<float> flowMapE,
        NativeSlice<float> flowMapE__buff,
        NativeSlice<float> flowMapW,
        NativeSlice<float> flowMapW__buff,
        int resolution,
        JobHandle dependency
    );

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct FlowMapStepUpdateWater<F, RO, RW> : IJobFor
        where F : struct, IComputeWaterLevel
		where RO : struct, IReadOnlyTile
        where RW : struct, IRWTile
        {

		F flowOperator;
	
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        RW water;
        [ReadOnly]
        RO flowN;
        [ReadOnly]
        RO flowS;
        [ReadOnly]
        RO flowE;
        [ReadOnly]
        RO flowW;

		public void Execute (int i) => flowOperator.Execute<RO, RW>(i, water, flowN, flowS, flowE, flowW);

		public static JobHandle ScheduleParallel ( 
            // compute outflow this is RO
            NativeSlice<float> waterMap,
            NativeSlice<float> waterMap__buff,
            // compute outflow then these are RW
            NativeSlice<float> flowMapN,
            NativeSlice<float> flowMapS,
            NativeSlice<float> flowMapE,
            NativeSlice<float> flowMapW,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new FlowMapStepUpdateWater<F, RO, RW>();

            job.flowOperator.Resolution = resolution;
            job.flowOperator.JobLength = resolution;
            job.water.Setup(waterMap, waterMap__buff, resolution);

            job.flowN.Setup(flowMapN, resolution);
            job.flowS.Setup(flowMapS, resolution);
            job.flowE.Setup(flowMapE, resolution);
            job.flowW.Setup(flowMapW, resolution);

            // no temporary allocations, so no need to dispose
			JobHandle res = job.ScheduleParallel(
                job.flowOperator.JobLength, 8, dependency
			);
            return TileHelpers.SWAP_RWTILE(waterMap, waterMap__buff, res);
		}
	}
    public delegate JobHandle FlowMapStepUpdateWaterDelegate( 
        // compute outflow this is RO
        NativeSlice<float> waterMap,
        NativeSlice<float> waterMap__buff,
        // compute outflow then these are RW
        NativeSlice<float> flowMapN,
        NativeSlice<float> flowMapS,
        NativeSlice<float> flowMapE,
        NativeSlice<float> flowMapW,
        int resolution,
        JobHandle dependency
    );

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct FlowMapWriteValues<F, RO, WO> : IJobFor
        where F : struct, IWriteFlowMap
		where RO : struct, IReadOnlyTile
        where WO : struct, IWriteOnlyTile
        {

		F flowOperator;
	
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [WriteOnly]
		WO height;
        [ReadOnly]
        RO flowN;
        [ReadOnly]
        RO flowS;
        [ReadOnly]
        RO flowE;
        [ReadOnly]
        RO flowW;
		public void Execute (int i) => flowOperator.Execute<RO, WO>(i, height, flowN, flowS, flowE, flowW);

		public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> src,  
            // compute outflow then these are RW
            NativeSlice<float> flowMapN,
            NativeSlice<float> flowMapS,
            NativeSlice<float> flowMapE,
            NativeSlice<float> flowMapW,
            int resolution,
            JobHandle dependency
		)
        {
            var job = new FlowMapWriteValues<F, RO, WO>();
			job.height.Setup(
				src, resolution
			);
            job.flowOperator.Resolution = resolution;
            job.flowOperator.JobLength = resolution;
            job.flowN.Setup(flowMapN, resolution);
            job.flowS.Setup(flowMapS, resolution);
            job.flowE.Setup(flowMapE, resolution);
            job.flowW.Setup(flowMapW, resolution);

			return job.ScheduleParallel(
                job.flowOperator.JobLength, 8, dependency
			);

		}
	}

    public delegate JobHandle FlowMapWriteValuesDelegate(
        NativeSlice<float> src,  
        NativeSlice<float> flowMapN,
        NativeSlice<float> flowMapS,
        NativeSlice<float> flowMapE,
        NativeSlice<float> flowMapW,
        int resolution,
        JobHandle dependency
    );

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
        NativeArray<float> args;

        public void Execute (int i) => fn.Execute<RW>(i, data, args);
        public static JobHandle ScheduleParallel (
            // compute outflow this is RO
			NativeSlice<float> src,
            int resolution,
            JobHandle dependency
		)
        {
            NativeArray<float> args_ = new NativeArray<float>(3, Allocator.TempJob);
            NativeArray<float> buff = new NativeArray<float>(resolution * resolution, Allocator.TempJob);
            NativeSlice<float> buffer = new NativeSlice<float>(buff);
            var rng = new GetMapRangeJob();
            JobHandle prev = rng.Schedule(src, args_, dependency, 0f);
            var job = new MapNormalizeValues<F, RW>();
            job.fn.Resolution = resolution;
            job.fn.JobLength = resolution;
            job.args = args_;
            job.data.Setup(src, buffer, resolution);
            JobHandle res = job.ScheduleParallel(
                job.fn.JobLength, 8, prev
			);
            JobHandle handle = TileHelpers.SWAP_RWTILE(src, buffer, res);
            return args_.Dispose(buff.Dispose(handle));
        }

    }

    public delegate JobHandle MapNormalizeValuesDelegate(
            NativeSlice<float> src,
            int resolution,
            JobHandle dependency);
		
}