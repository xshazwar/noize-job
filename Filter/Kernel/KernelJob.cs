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
        Sobel3_2D,
        Prewitt3Horizontal,
        Prewitt3Vertical
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
        public static float[] sobel3_HZ = {
            1f,
            2f,
            1f
        };
        public static float[] sobel3_VX = {1f, 2f, 1f};
        public static float[] sobel3_VZ = {
            1f, 
            0f,
           -1f
        };

        public static float prewitt3Factor =  1f;
        public static float[] prewitt3_HX = {1f, 0f, -1f};
        public static float[] prewitt3_HZ = {
            1f,
            1f,
            1f
        };
        public static float[] prewitt3_VX = {1f, 1f, 1f};
        public static float[] prewitt3_VZ = {
           -1f, 
            0f,
            1f
        };

        
        // Jobs that can operate on the previous steps output
        private static JobHandle ScheduleSeries<XO, ZO>(
            NativeSlice<float> src,
            NativeSlice<float> tmp,
            int resolution,
            int kernelSize,
            NativeArray<float> kernelX,
            NativeArray<float> kernelZ,
            float kernelFactor,
            JobHandle dependency
        ) 
            where XO: struct, IKernelOperator, IKernelData
            where ZO: struct, IKernelOperator, IKernelData
        {
            UnityEngine.Profiling.Profiler.BeginSample("Schedule X / Y Pass");
            JobHandle first = GenericKernelJob<KernelTileMutation<XO>, RWTileData>.ScheduleParallel(
                src, tmp, resolution, kernelSize, kernelX, kernelFactor, dependency
            );
            JobHandle res = GenericKernelJob<KernelTileMutation<ZO>, RWTileData>.ScheduleParallel(
                src, tmp, resolution, kernelSize, kernelZ, kernelFactor, first
            );
            UnityEngine.Profiling.Profiler.EndSample();
            return res;
        }


        // // Jobs that can operate in parallel and require reduction
        // private static JobHandle SchedulePL<T>(
        //     NativeSlice<float> src,
        //     int resolution,
        //     int kernelSize,
        //     NativeArray<float> kernelX,
        //     NativeArray<float> kernelZ,
        //     float kernelFactor,
        //     JobHandle dependency
        // ) where T: struct, IReduceTiles {
        //     NativeArray<float> original = new NativeArray<float>(src.Length, Allocator.TempJob);
        //     NativeArray<float> tmp0 = new NativeArray<float>(src.Length, Allocator.TempJob);
        //     NativeArray<float> tmp1 = new NativeArray<float>(src.Length, Allocator.TempJob);
            
        //     NativeSlice<float> originalS = new NativeSlice<float>(original);
        //     NativeSlice<float> tmp0s = new NativeSlice<float>(tmp0);
        //     NativeSlice<float> tmp1s = new NativeSlice<float>(tmp1);

        //     originalS.CopyFrom(src);
        //     JobHandle xPass = GenericKernelJob<KernelTileMutation<KernelSampleXOperator>, RWTileData>.ScheduleParallel(
        //         src, tmp0s, resolution, kernelSize, kernelX, kernelFactor, dependency
        //     );
        //     JobHandle zPass = GenericKernelJob<KernelTileMutation<KernelSampleZOperator>, RWTileData>.ScheduleParallel(
        //         originalS, tmp1s, resolution, kernelSize, kernelZ, kernelFactor, dependency
        //     );
        //     JobHandle allPass = JobHandle.CombineDependencies(xPass, zPass);
        //     JobHandle reduce = ReductionJob<T, RWTileData, ReadTileData>.ScheduleParallel(
        //         src, originalS, resolution, allPass
        //     );
        //     return tmp1.Dispose(tmp0.Dispose(original.Dispose(reduce)));
        // }

        private static JobHandle ScheduleReduce<RE, XO, ZO>(
            NativeSlice<float> src,
            NativeSlice<float> tmp,
            float[] AX,
            float[] AZ,
            float[] BX,
            float[] BZ,
            int resolution,
            JobHandle dependency
        )
            where RE: struct, IReduceTiles
            where XO: struct, IKernelOperator, IKernelData
            where ZO: struct, IKernelOperator, IKernelData
        {
            NativeArray<float> original = new NativeArray<float>(src.Length, Allocator.Persistent);
            NativeSlice<float> originalS = new NativeSlice<float>(original);

            src.CopyTo(original);
            NativeArray<float> kbx_h = new NativeArray<float>(AX, Allocator.Persistent);
            NativeArray<float> kbz_h = new NativeArray<float>(AZ, Allocator.Persistent);
            NativeArray<float> kbx_v = new NativeArray<float>(BX, Allocator.Persistent);
            NativeArray<float> kbz_v = new NativeArray<float>(BZ, Allocator.Persistent);
            
            JobHandle A = ScheduleSeries<XO, ZO>(src, tmp, resolution, 3, kbx_h, kbz_h, 1f, dependency);
            JobHandle B = ScheduleSeries<XO, ZO>(originalS, tmp, resolution, 3, kbx_v, kbz_v, 1f, A);
            JobHandle reducePass =  ReductionJob<RE, RWTileData, ReadTileData>.ScheduleParallel(
                src, originalS, tmp, resolution, B
            );
            var ch1 = JobHandle.CombineDependencies(original.Dispose(reducePass), kbx_h.Dispose(reducePass), kbz_h.Dispose(reducePass));
            return JobHandle.CombineDependencies(kbx_v.Dispose(reducePass), kbz_v.Dispose(reducePass), ch1);
        }

        public static JobHandle Schedule(NativeSlice<float> src, NativeSlice<float> tmp, KernelFilterType filter, int resolution, JobHandle dependency){
            float[] kernelBodyX = smooth3;
            float[] kernelBodyZ = smooth3;
            float kernelFactor = smooth3Factor;
            int kernelSize = 3;

            switch(filter){
                case KernelFilterType.Sobel3_2D:
                    return ScheduleReduce<RootSumSquaresTiles, KernelSampleXOperator, KernelSampleZOperator>(src, tmp, sobel3_HX, sobel3_HZ, sobel3_VX, sobel3_VZ, resolution, dependency);
                    // return ScheduleSobel2D(src, tmp, resolution, dependency);
                case KernelFilterType.Smooth3:
                    break;
                case KernelFilterType.Gauss5:
                    kernelBodyX = gauss5;
                    kernelBodyZ = gauss5;
                    kernelFactor = gauss5Factor;
                    kernelSize = 5;
                    break;
                case KernelFilterType.Gauss3:
                    kernelBodyX = gauss3;
                    kernelBodyZ = gauss3;
                    kernelFactor = gauss3Factor;
                    break;;
                case KernelFilterType.Sobel3Horizontal:
                    kernelBodyX = sobel3_HX;
                    kernelBodyZ = sobel3_HZ;
                    kernelFactor = sobel3Factor;;
                    break;
                case KernelFilterType.Sobel3Vertical:
                    kernelBodyX = sobel3_VX;
                    kernelBodyZ = sobel3_VZ;
                    kernelFactor = sobel3Factor;
                    break;   
                case KernelFilterType.Prewitt3Horizontal:
                    kernelBodyX = prewitt3_HX;
                    kernelBodyZ = prewitt3_HZ;
                    kernelFactor = prewitt3Factor;;
                    break;
                case KernelFilterType.Prewitt3Vertical:
                    kernelBodyX = prewitt3_VX;
                    kernelBodyZ = prewitt3_VZ;
                    kernelFactor = prewitt3Factor;
                    break;          
            }
            UnityEngine.Profiling.Profiler.BeginSample("Alloc kbx/y");
            NativeArray<float> kbx = new NativeArray<float>(kernelBodyX, Allocator.Persistent);
            NativeArray<float> kbz = new NativeArray<float>(kernelBodyZ, Allocator.Persistent);
            UnityEngine.Profiling.Profiler.EndSample();
            JobHandle res = ScheduleSeries<KernelSampleXOperator, KernelSampleZOperator>(src, tmp, resolution, kernelSize, kbx, kbz, kernelFactor, dependency);
            // Dispose of native containers on completion
            return kbx.Dispose(
                kbz.Dispose(
                    res
            ));
            
        }
    }
    public delegate JobHandle SeperableKernelFilterDelegate (
        NativeSlice<float> src,
        NativeSlice<float> tmp,
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
            NativeArray<float> kbz = new NativeArray<float>(SeparableKernelFilter.smooth3, Allocator.TempJob);
            JobHandle res = ScheduleSeries(src, resolution, 3, kbx, kbz, 1f, dependency);
            // Dispose of native containers on completion
            return kbx.Dispose(
                kbz.Dispose(
                    res
            ));
        }
    }


    public delegate JobHandle ErosionKernelJobDelegate(NativeSlice<float> src, int resolution, JobHandle dependency);
}