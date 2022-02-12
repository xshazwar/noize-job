using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.processing.cpu.mutate {
    using Unity.Mathematics;

	[BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
	public struct GenericKernelJob<K, D> : IJobFor
        where K : struct, IMutateTiles, IKernelData
		where D : struct, IRWTile
        {

		K kernelPass;
	
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
		D data;

		public void Execute (int i) => kernelPass.Execute<D>(i, data);

		public static JobHandle ScheduleParallel (
			NativeSlice<float> src,
            NativeSlice<float> dst,
            int resolution,
            int kernelSize,
            NativeArray<float> kernelBody,
            float kernelFactor,
            JobHandle dependency
		)
        {
			var job = new GenericKernelJob<K, D>();
			job.kernelPass.Resolution = resolution;
            job.kernelPass.JobLength = resolution;
            job.kernelPass.Setup(kernelFactor, kernelSize, kernelBody);
			job.data.Setup(
				src, dst, resolution
			);
			JobHandle handle = job.ScheduleParallel(
				job.kernelPass.JobLength, 8, dependency
			);
            return TileHelpers.SWAP_RWTILE(src, dst, handle);

		}
	}

    public struct KernelTileMutation<O>: IMutateTiles, IKernelData
        where O: struct, IKernelOperator, IKernelData
        {

        public int Resolution {get; set;}
        public int JobLength {get; set;}
        public O kernelOp;

        public void Setup(float kernelFactor, int kernelSize, NativeArray<float> kernel){
            kernelOp.Setup(kernelFactor, kernelSize, kernel);
        }
        public void Execute<T>(int z, T tile) where  T : struct, IRWTile {
            for( int x = 0; x < Resolution; x++){
                kernelOp.ApplyKernel<T>(x, z, tile);
            }
        }
    }

    public enum KernelFilterType {
        Gauss5,
        Gauss3,
        Smooth3,
        Sobel3Horizontal,
        Sobel3Vertical,
        Sobel3_2D
    }

    public struct SeparableKernelFilter {
        public static float gauss5Factor = 1f/16f;
        public static float[] gauss5 = {1f,4f,6f,4f,1f};
        public static float gauss3Factor = 1f/4f;
        public static float[] gauss3 = {1f, 2f, 1f};
        public static float smooth3Factor =  1f / 3f ;
        public static float[] smooth3 = {1f, 1f, 1f};

        public static float sobel3Factor =  1f;
        public static float[] sobel3_HX = {-1f, 0f, 1f};
        public static float[] sobel3_HY = {1f, 2f, 1f};
         public static float[] sobel3_VY = {1f, 0f, -1f};
        public static float[] sobel3_VX = {1f, 2f, 1f};
        
        // Jobs that can operate on the previous steps output
        private static JobHandle ScheduleSeries(
            NativeSlice<float> src,
            int resolution,
            int kernelSize,
            NativeArray<float> kernelX,
            NativeArray<float> kernelZ,
            float kernelFactor,
            JobHandle dependency
        ){
            NativeArray<float> tmp = new NativeArray<float>(src.Length, Allocator.TempJob);
            JobHandle first = GenericKernelJob<KernelTileMutation<KernelSampleXOperator>, RWTileData>.ScheduleParallel(
                src, tmp, resolution, kernelSize, kernelX, kernelFactor, dependency
            );
            JobHandle res = GenericKernelJob<KernelTileMutation<KernelSampleZOperator>, RWTileData>.ScheduleParallel(
                src, tmp, resolution, kernelSize, kernelZ, kernelFactor, first
            );
            return tmp.Dispose(res);
        }


        // Jobs that can operate in parallel and require reduction
        private static JobHandle SchedulePL<T>(
            NativeSlice<float> src,
            int resolution,
            int kernelSize,
            NativeArray<float> kernelX,
            NativeArray<float> kernelZ,
            float kernelFactor,
            JobHandle dependency
        ) where T: struct, IReduceTiles {
            NativeArray<float> original = new NativeArray<float>(src.Length, Allocator.TempJob);
            NativeArray<float> tmp0 = new NativeArray<float>(src.Length, Allocator.TempJob);
            NativeArray<float> tmp1 = new NativeArray<float>(src.Length, Allocator.TempJob);
            
            NativeSlice<float> originalS = new NativeSlice<float>(original);
            NativeSlice<float> tmp0s = new NativeSlice<float>(tmp0);
            NativeSlice<float> tmp1s = new NativeSlice<float>(tmp1);

            originalS.CopyFrom(src);
            JobHandle xPass = GenericKernelJob<KernelTileMutation<KernelSampleXOperator>, RWTileData>.ScheduleParallel(
                src, tmp0s, resolution, kernelSize, kernelX, kernelFactor, dependency
            );
            JobHandle zPass = GenericKernelJob<KernelTileMutation<KernelSampleZOperator>, RWTileData>.ScheduleParallel(
                originalS, tmp1s, resolution, kernelSize, kernelZ, kernelFactor, dependency
            );
            JobHandle allPass = JobHandle.CombineDependencies(xPass, zPass);
            JobHandle reduce = ReductionJob<T, RWTileData, ReadTileData>.ScheduleParallel(
                src, originalS, resolution, allPass
            );
            return tmp1.Dispose(tmp0.Dispose(original.Dispose(reduce)));
        }

        private static JobHandle ScheduleSobel2D(
            NativeSlice<float> src,
            int resolution,
            JobHandle dependency
        ){
            NativeArray<float> original = new NativeArray<float>(src.Length, Allocator.TempJob);
            src.CopyTo(original);
            NativeArray<float> kbx_h = new NativeArray<float>(sobel3_HX, Allocator.TempJob);
            NativeArray<float> kby_h = new NativeArray<float>(sobel3_HY, Allocator.TempJob);
            NativeArray<float> kbx_v = new NativeArray<float>(sobel3_VX, Allocator.TempJob);
            NativeArray<float> kby_v = new NativeArray<float>(sobel3_VY, Allocator.TempJob);
            JobHandle horiz = SchedulePL<MultiplyTiles>(src, resolution, 3, kbx_h, kby_h, 1f, dependency);
            JobHandle vert = SchedulePL<MultiplyTiles>(original, resolution, 3, kbx_v, kby_v, 1f, dependency);
            JobHandle allPass = JobHandle.CombineDependencies(horiz, vert);
            JobHandle reducePass =  ReductionJob<RootSumSquaresTiles, RWTileData, ReadTileData>.ScheduleParallel(
                src, original, resolution, allPass
            );
            // chain disposal of nativeArrays after job complete
            return kby_v.Dispose(
                kbx_v.Dispose(
                    kby_h.Dispose(
                        kbx_h.Dispose(
                            original.Dispose(reducePass)))));
        }

        public static JobHandle Schedule(NativeSlice<float> src, KernelFilterType filter, int resolution, JobHandle dependency){
            float[] kernelBodyX = smooth3;
            float[] kernelBodyY = smooth3;
            float kernelFactor = smooth3Factor;
            int kernelSize = 3;

            switch(filter){
                case KernelFilterType.Sobel3_2D:
                    return ScheduleSobel2D(src, resolution, dependency);
                case KernelFilterType.Smooth3:
                    break;
                case KernelFilterType.Gauss5:
                    kernelBodyX = gauss5;
                    kernelBodyY = gauss5;
                    kernelFactor = gauss5Factor;
                    kernelSize = 5;
                    break;
                case KernelFilterType.Gauss3:
                    kernelBodyX = gauss3;
                    kernelBodyY = gauss3;
                    kernelFactor = gauss3Factor;
                    break;;
                case KernelFilterType.Sobel3Horizontal:
                    kernelBodyX = sobel3_HX;
                    kernelBodyY = sobel3_HY;
                    kernelFactor = sobel3Factor;;
                    break;
                case KernelFilterType.Sobel3Vertical:
                    kernelBodyX = sobel3_VX;
                    kernelBodyY = sobel3_VY;
                    kernelFactor = sobel3Factor;
                    break;           
            }
            NativeArray<float> kbx = new NativeArray<float>(kernelBodyX, Allocator.TempJob);
            NativeArray<float> kby = new NativeArray<float>(kernelBodyY, Allocator.TempJob);
            JobHandle res;

            switch(filter){
                case KernelFilterType.Sobel3Horizontal:
                case KernelFilterType.Sobel3Vertical:
                    res = SchedulePL<MultiplyTiles>(src, resolution, kernelSize, kbx, kby, kernelFactor, dependency);
                    break;
                default:
                    res = ScheduleSeries(src, resolution, kernelSize, kbx, kby, kernelFactor, dependency);
                    break;
            }
            // Dispose of native containers on completion
            return kbx.Dispose(
                kby.Dispose(
                    res
            ));
            
        }
    }
    public delegate JobHandle SeperableKernelFilterDelegate (
        NativeSlice<float> src,
        KernelFilterType filter,
        int resolution,
        JobHandle dependency
	);

    // KernelMinXOperator
    public struct ErosionKernelJob{
        private static JobHandle ScheduleSeries(
            NativeSlice<float> src,
            int resolution,
            int kernelSize,
            NativeArray<float> kernelX,
            NativeArray<float> kernelZ,
            float kernelFactor,
            JobHandle dependency
        ){
            NativeArray<float> tmp = new NativeArray<float>(src.Length, Allocator.TempJob);
            JobHandle first = GenericKernelJob<KernelTileMutation<KernelMinXOperator>, RWTileData>.ScheduleParallel(
                src, tmp, resolution, kernelSize, kernelX, kernelFactor, dependency
            );
            JobHandle res = GenericKernelJob<KernelTileMutation<KernelMinZOperator>, RWTileData>.ScheduleParallel(
                src, tmp, resolution, kernelSize, kernelZ, kernelFactor, first
            );
            return tmp.Dispose(res);
        }

        public static JobHandle Schedule(NativeSlice<float> src, int resolution, JobHandle dependency){
            NativeArray<float> kbx = new NativeArray<float>(SeparableKernelFilter.smooth3, Allocator.TempJob);
            NativeArray<float> kby = new NativeArray<float>(SeparableKernelFilter.smooth3, Allocator.TempJob);
            JobHandle res = ScheduleSeries(src, resolution, 3, kbx, kby, 1f, dependency);
            // Dispose of native containers on completion
            return kbx.Dispose(
                kby.Dispose(
                    res
            ));
        }
    }


    public delegate JobHandle ErosionKernelJobDelegate(NativeSlice<float> src, int resolution, JobHandle dependency);
}