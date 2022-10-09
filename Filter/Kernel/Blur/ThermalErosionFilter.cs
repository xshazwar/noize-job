using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.filter;

namespace xshazwar.noize.filter.blur {
    using Unity.Mathematics;  
              
    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct ThermalErosionFilter : IJobFor
    {

        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        NativeSlice<float> data;
        int flip;
        float talus;
        float heightRatio;
        float increment;
        int resolution;
        float f_resolution;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, resolution - 1);
            z = clamp(z, 0, resolution - 1);
            return (z * resolution) + x;   
        }

        public void rectifyNeighborhood(int x, int z){
            int idx = getIdx(x, z);
            rectify(idx, getIdx(x + 1, z));
            rectify(idx, getIdx(x - 1, z));
            rectify(idx, getIdx(x, z + 1));
            rectify(idx, getIdx(x, z - 1));

        }

        public void rectify(int idxA, int idxB){
            float va = data[idxA];
            float vb = data[idxB];
            float diff = abs(va - vb);
            // float angle = asin((diff / (1f/(float)resolution)) * heightRatio);  // opposite / adjacent >> can take into account tile scale in talus
            float angle = atan(diff / (1f / f_resolution));
            float asdeg = angle * 180 / 3.14159f;
            // Debug.Log($"{asdeg} {angle / talus}");

            if(angle > talus){
                float excess = tan((angle - talus)) / f_resolution;
                if(va > vb){
                    vb += increment * excess;
                    va -= increment * excess;
                }else{
                    va += increment * excess;
                    vb -= increment * excess;
                }
                data[idxA] = va;
                data[idxB] = vb;
            }
        }
        

        public void Execute (int z) {
            int offset = z % 2 == 0 ? 0 : 1;
            for (int x = offset; x < resolution; x += 2){
                rectifyNeighborhood(x, z);
            }
        }
        public static JobHandle Schedule (
			NativeSlice<float> src,
            float talus, // degrees
            float incrementRatio,
            float meshHeightWidthRatio,
            int iterations,
            int resolution,
            JobHandle dep
		)
        {
            JobHandle handle = dep;
            var job = new ThermalErosionFilter();
            job.data = src;
            job.resolution = resolution;
            job.f_resolution = (float) resolution;
            job.increment = incrementRatio;
            job.heightRatio = meshHeightWidthRatio;
            job.talus = (talus / 90f) * 3.14159f/2f;
            for (int i = 0; i < iterations; i++){
                for(int flipflop = 0; flipflop <= 1; flipflop++){
                    job.flip = flipflop;
                    handle = job.ScheduleParallel(
                        resolution, 32, handle
                    );
                }
            }
            return handle;
        }
    }

    

    public delegate JobHandle ThermalErosionFilterDelegate(
			NativeSlice<float> src,
            float talus,
            float incrementRatio,
            float meshHeightWidthRatio,
            int iterations,
            int resolution,
            JobHandle dependency
        );
}