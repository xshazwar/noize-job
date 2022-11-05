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

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, resolution - 1);
            z = clamp(z, 0, resolution - 1);
            return (z * resolution) + x;   
        }

        public int4 idxNeighborhood(int x, int z){
            return new int4(
                (z * resolution) + x,
                getIdx(x + 1, z),
                getIdx(x, z + 1),
                getIdx(x + 1, z + 1)
            );
        }

        public float4 collectNeighborhood(int4 idx){
            return new float4(
                data[idx.x],
                data[idx.y],
                data[idx.z],
                data[idx.w]
            );
        }

        public void setNeighborhood(int4 idx, float4 val){
            data[idx.x] = val.x;
            data[idx.y] = val.y;
            data[idx.z] = val.z;
            data[idx.w] = val.w;
        }

        // // 4 step square
        // public rectifyNeighborhood(float4 val){
        //     int idx = (z * resolution) + x;
        //     int idxr = getIdx(x + 1, z);
        //     int idxd = getIdx(x, z + 1);
        //     int idxdr = getIdx(x + 1, z + 1);
        //     float v = data[idx];
        //     float vr = data[idxr];
        //     float vd = data[idxd];
        //     float vdr = data[idxdr];
        //     rectify(ref v, ref vr);
        //     rectify(ref v, ref vd);
        //     rectify(ref v, ref vdr);
        //     rectify(ref vr, ref vd);
        //     rectify(ref vr, ref vdr);
        //     rectify(ref vd, ref vdr);
        //     data[idx] = v;
        //     data[idxr] = vr;
        //     data[idxd] = vd;
        //     data[idxdr] = vdr;
        // }

        // 4 step square
        public void rectifyNeighborhood(ref float4 v){
            v.xy = rectify(v.xy);
            v.xz = rectify(v.xz);
            v.xw = rectify(v.xw);
            v.yz = rectify(v.yz);
            v.yw = rectify(v.yw);
            v.zw = rectify(v.zw);
        }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 rectify(float2 v){
            float diff = abs(v.x - v.y);
            float angle = atan(diff / (1f / f_resolution));
            if(angle > talus){
                float excess = tan((angle - talus)) / f_resolution;
                if(v.x > v.y){
                    v.y += increment * 0.5f * excess;
                    v.x -= increment * 0.5f * excess;
                }
                else{
                    v.x += increment * 0.5f * excess;
                    v.y -= increment * 0.5f * excess;
                }
            }
            return v;
        }
        
        // 4 Step Square
        public void Execute (int z) {
            int offset = 1;
            z += 1;
            if(flip % 2 != 0){
                offset += 1;
            }
            z *= 2;
            if (flip > 1){
                z -= 1;
            }
            float4 val;
            int x = offset;
            int4 idx = idxNeighborhood(x, z);
            while(x < resolution - 1){
                val = collectNeighborhood(idx);
                rectifyNeighborhood(ref val);
                setNeighborhood(idx, val);
                x+=2; idx +=2;
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
                        ((int) resolution / 2) - 1, 32, handle
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