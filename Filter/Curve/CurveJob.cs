using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
	public struct CurveJob<G, D> : IJobFor
		where G : struct, IApplyCurve
		where D : struct, IRWTile{

		G generator;

		[NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
		D data;

		[NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
		[ReadOnly]
		NativeSlice<float> Curve;

		public void Execute (int i) => generator.Execute<D>(i, data, Curve);

		public static JobHandle ScheduleParallel (
			NativeSlice<float> src, //receives output
			NativeSlice<float> tmp, // long lived temporary buffer for rw tiles
            NativeSlice<float> curve, // discretized version of the curve
            int resolution,
            JobHandle dependency
		) {
			var job = new CurveJob<G, D>();
			job.Curve = curve;
			job.generator.Setup(resolution, resolution, curve.Length);
			job.data.Setup(
				src, tmp, resolution
			);
			JobHandle handle = job.ScheduleParallel(
				job.generator.JobLength, 1, dependency
			);
			return TileHelpers.SWAP_RWTILE(src, tmp, handle);
		}
	}

    public struct CurveOperator : IApplyCurve {
        
		public int Resolution {get; set;}
		public int JobLength {get; set;}
        public int CurveSize {get; set;}

		public void Setup(int resolution, int jobLength, int curveSize){
			Resolution = resolution;
			JobLength = jobLength;
			CurveSize = curveSize;
		}
        
		public float Apply(float v, NativeSlice<float> curve){
			// Expects [0,1] range
			// We'll grab adjacent curve values and lerp as best we can
			float rect = clamp(v, 0, 1) * CurveSize;
			float lowerIdx = floor(rect);
			float left =  curve[(int)lowerIdx];
			float right = curve[(int)lowerIdx + 1];
			return lerp(left, right, (rect - lowerIdx));
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Execute<T>(int z, T tile, NativeSlice<float> curve) where  T : struct, IRWTile {
			for (int x = 0; x < Resolution; x++ ){
				float res = Apply(tile.GetData(x, z), curve);
				tile.SetValue(x, z, res);
			}
		}
    }

	public delegate JobHandle CurveJobScheduleDelegate (
		NativeSlice<float> src, //receives output
		NativeSlice<float> tmp, // long lived temporary buffer for rw tiles
		NativeSlice<float> curve,
		int resolution,
		JobHandle dependency
	);
}
