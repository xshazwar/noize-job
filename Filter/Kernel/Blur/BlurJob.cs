using System;
using System.Collections.Generic;

using Unity.Jobs;
using Unity.Collections;

using xshazwar.noize.filter;

namespace xshazwar.noize.filter.blur {
    public struct GaussFilter {    
        public static JobHandle Schedule(NativeSlice<float> src, NativeSlice<float> tmp, int width, GaussSigma sigma, int resolution, JobHandle dependency){
            List<float[]> kernels = GaussianKernel.GetKernel(sigma, width);
            NativeArray<float> kbx = new NativeArray<float>(kernels[0], Allocator.Persistent);
            NativeArray<float> kbz = new NativeArray<float>(kernels[1], Allocator.Persistent);
            JobHandle res = SeparableKernelFilter.ScheduleSeries<KernelSampleXOperator, KernelSampleZOperator>(src, tmp, resolution, width, kbx, kbz, 1f, dependency);
            return kbx.Dispose(
                kbz.Dispose(
                    res
            ));
            
        }
        
        public delegate JobHandle GaussFilterDelegate (
            NativeSlice<float> src,
            NativeSlice<float> tmp,
            int width,
            GaussSigma sigma,
            int resolution,
            JobHandle dependency
        );
    }

    public struct SmoothFilter {    
        public static JobHandle Schedule(NativeSlice<float> src, NativeSlice<float> tmp, int width, int resolution, JobHandle dependency){
            List<float[]> kernels = SmoothBlur.GetKernel(width);
            NativeArray<float> kbx = new NativeArray<float>(kernels[0], Allocator.Persistent);
            NativeArray<float> kbz = new NativeArray<float>(kernels[1], Allocator.Persistent);
            JobHandle res = SeparableKernelFilter.ScheduleSeries<KernelSampleXOperator, KernelSampleZOperator>(src, tmp, resolution, width, kbx, kbz, 1f, dependency);
            return kbx.Dispose(
                kbz.Dispose(
                    res
            ));
            
        }
        
        public delegate JobHandle SmoothFilterDelegate (
            NativeSlice<float> src,
            NativeSlice<float> tmp,
            int width,
            int resolution,
            JobHandle dependency
        );
    }
}