using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.generate {
    using Unity.Mathematics;

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
	public struct FractalJob<G, D> : IJobFor
        where G: struct, ITileSource, IFractalSettings
        where D : struct, IWriteOnlyTile
    {
        G generator;

        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
		D data;
		public void Execute (int i) => generator.Execute(i, data);

        public static float CalcFractalNormValue(float hurst, int octaves, float startingAmplitude){
            float G = math.exp2(-hurst);
            float a = 1f;
            float t = 0;
            for (int i = 0; i < octaves; i++){
                t += a * 1f;
                a *= G;
            }
            return t;
        }

		public static JobHandle ScheduleParallel (
			NativeSlice<float> src,
            int resolution,
            float hurst,
            float startingAmplitude,
            float stepdown,
            float detuneRate,
            int octaves,
            int xpos,
            int zpos,
            int noiseSize,
            JobHandle dependency
		)
        {
			var job = new FractalJob<G, D>();
			job.generator.Resolution = resolution;
            job.generator.JobLength = resolution;
            job.generator.Hurst = hurst;
            job.generator.DetuneRate = detuneRate;
            job.generator.StepDown = stepdown;
            job.generator.NoiseSize = noiseSize;
            job.generator.StartingAmplitude = startingAmplitude;
            job.generator.NormalizationValue = CalcFractalNormValue(hurst, octaves, startingAmplitude);
            job.generator.SetPosition(xpos, zpos);
            job.generator.OctaveCount = octaves;
			job.data.Setup(
				src, resolution
			);
			return job.ScheduleParallel(
				job.generator.JobLength, 1, dependency
			);
		}
	}

    public delegate JobHandle FractalJobDelegate (
        NativeSlice<float> src,
        int resolution,
        float hurst,
        float startingAmplitude,
        float stepdown,
        float detuneRate,
        int octaves,
        int xpos,
        int zpos,
        int noiseSize,
        JobHandle dependency
	);

    public struct FractalGenerator<N>: ITileSource, IFractalSettings
        where N: IMakeNoise
    {
        public int JobLength {get; set;}
        public int Resolution {get; set;}

        // [0, 1] recommended, but we allow up to 2
        public float Hurst {get; set;}
        public int OctaveCount {get; set;}
        public float StartingAmplitude {get; set;}
        public float StepDown {get; set;} // 2f
        public float DetuneRate {get; set;} // 0f [-.05 -> .05]

        public float NormalizationValue {get; set;}

        public int NoiseSize {get; set;}
        float2 Position;
        public N noiseGenerator;

        public void SetPosition(int x, int z){
            Position.x = (float) x;
            Position.y = (float) z;
        }

        float NoiseValue(int x, int z){
            float xi = ((float) x + Position.x) / (float) NoiseSize;
            float zi = ((float) z + Position.y) / (float) NoiseSize;
            float Detune = 0f;
            float G = math.exp2(-Hurst);
            float f = 1f;
            float a = StartingAmplitude;
            float t = 0;
            for (int i = 0; i < OctaveCount; i++){
                float xV = f * xi;
                float zV = f * zi;
                t += a * noiseGenerator.NoiseValue(xV, zV);
                Detune += DetuneRate;
                f *= (StepDown - Detune);
                a *= G;
            }
            return t / NormalizationValue;
        }

        
        public void Execute<T>(int z, T tile) where  T : struct, IWriteOnlyTile {
            for( int x = 0; x < Resolution; x++){
                tile.SetValue(x, z, NoiseValue(x, z));
            }
        }
    }

    public struct PerlinGetter: IMakeNoise {
        private const float RV = 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NoiseValue(float x, float z){
            float2 coord = float2(x, z) ;
            return Rectify(noise.cnoise(coord));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Rectify(float value){
            return (RV + value) / 2 * RV;
        }
    }

    public struct PeriodicPerlinGetter: IMakeNoise {
        private const float RV = 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NoiseValue(float x, float z){
            
            float2 period = float2(1010, 102);
            float2 coord = float2(x, z) ;
            return Rectify(noise.psrnoise(coord, period));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Rectify(float value){
            return (RV + value) / 2 * RV;
        }
    }

    public struct RotatedSimplexGetter: IMakeNoise {
        private const float RV = 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NoiseValue(float x, float z){
            
            float2 period = float2(1010, 102);
            float2 coord = float2(x, z) ;
            return Rectify(noise.psrnoise(coord, period, .62f));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Rectify(float value){
            return (RV + value) / 2 * RV;
        }
    }

    public struct SinGetter: IMakeNoise {
        private const float RV = 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NoiseValue(float x, float z){
            
            float2 coord = float2(x, z) ;
            return Rectify(math.sin(coord));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Rectify(float2 value){
            float2 v = 0.5f + (0.5f * value);
            return v.x * v.y;
        }
    }

    public struct SimplexGetter: IMakeNoise {
        
        private const float RV = 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NoiseValue(float x, float z){
            float2 coord = float2(x, z) ;
            return Rectify(noise.snoise(coord));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Rectify(float value){
            return (RV + value) / 2 * RV;
        }
    }

    public struct CellularGetter: IMakeNoise {
        private const float RV = 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NoiseValue(float x, float z){
            float2 coord = float2(x, z) ;
            return Rectify(noise.cellular(coord));

        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float Rectify(float2 value){
            float2 v = (RV + value) / 2 * RV;
            return v.x * v.y;
        }
    }
}
