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

        // 4 step square
        public void rectifyNeighborhood(int x, int z){
            int idx = (z * resolution) + x;
            int idxr = getIdx(x + 1, z);
            int idxd = getIdx(x, z + 1);
            int idxdr = getIdx(x + 1, z + 1);
            float v = data[idx];
            float vr = data[idxr];
            float vd = data[idxd];
            float vdr = data[idxdr];
            rectify(ref v, ref vr);
            rectify(ref v, ref vd);
            rectify(ref v, ref vdr);
            rectify(ref vr, ref vd);
            rectify(ref vr, ref vdr);
            rectify(ref vd, ref vdr);
            data[idx] = v;
            data[idxr] = vr;
            data[idxd] = vd;
            data[idxdr] = vdr;

        }

        public void rectify(ref float va, ref float vb){
            float diff = abs(va - vb);
            float angle = atan(diff / (1f / f_resolution));
            if(angle > talus){
                float excess = tan((angle - talus)) / f_resolution;
                if(va > vb){
                    vb += increment * 0.5f * excess;
                    va -= increment * 0.5f * excess;
                }
                else{
                    va += increment * 0.5f * excess;
                    vb -= increment * 0.5f * excess;
                }
            }
        }
        
        // 4 Step Square
        public void Execute (int z) {
            int offset = 0;
            if(flip % 2 != 0){
                offset += 1;
            }
            z *= 2;
            if (flip > 1){
                z += 1;
            }
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
                for(int flipflop = 0; flipflop < 4; flipflop++){
                    job.flip = flipflop;
                    handle = job.ScheduleParallel(
                        (int) resolution / 2, 1, handle
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