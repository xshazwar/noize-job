using System;
using System.Collections.Generic;

using Unity.Jobs;
using Unity.Collections;

using xshazwar.noize.filter;

namespace xshazwar.noize.filter.edge {
    public struct Edge1DFilter {    
        public static JobHandle Schedule(NativeSlice<float> src, NativeSlice<float> tmp, EdgeAlgorithm algo, EdgeDirection dir, int resolution, JobHandle dependency){
            List<float[]> kernels = EdgeDetectionKernel.Get1DKernel(algo, dir);
            NativeArray<float> kbx = new NativeArray<float>(kernels[0], Allocator.Persistent);
            NativeArray<float> kbz = new NativeArray<float>(kernels[1], Allocator.Persistent);
            JobHandle res = SeparableKernelFilter.ScheduleSeries<KernelSampleXOperator, KernelSampleZOperator>(src, tmp, resolution, 3, kbx, kbz, 1f, dependency);
            return kbx.Dispose(
                kbz.Dispose(
                    res
            ));   
        }
        
        public delegate JobHandle Edge1DFilterDelegate (
            NativeSlice<float> src,
            NativeSlice<float> tmp,
            EdgeAlgorithm algo,
            EdgeDirection dir,
            int resolution,
            JobHandle dependency
        );
    }

    public struct Edge2DFilter {    
        public static JobHandle Schedule(NativeSlice<float> src, NativeSlice<float> tmp, EdgeAlgorithm algo, int resolution, JobHandle dependency){
            List<float[]> k = EdgeDetectionKernel.Get2DKernel(algo);
            return SeparableKernelFilter.ScheduleReduce<RootSumSquaresTiles, KernelSampleXOperator, KernelSampleZOperator>(src, tmp, k[0], k[1], k[2], k[3], resolution, dependency);
        }
        
        public delegate JobHandle Edge2DFilterDelegate (
            NativeSlice<float> src,
            NativeSlice<float> tmp,
            EdgeAlgorithm algo,
            int resolution,
            JobHandle dependency
        );
    }

}