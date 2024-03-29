using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.generate {
    
    [CreateAssetMenu(fileName = "NoiseGenerator", menuName = "Noize/Generators/NoiseSource", order = 1)]
    public class NoiseStage: PipelineStage {
        
        public enum FractalNoise {
            Sin,
            Perlin,
            PeriodicPerlin,
            Simplex,
            RotatedSimplex,
            Cellular,
            DomainRotatedPerlin,
            DomainRotatedSimplex
        }

        static FractalJobDelegate[] jobs = {
            FractalJob<FractalGenerator<SinGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<PerlinGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<PeriodicPerlinGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<SimplexGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<RotatedSimplexGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<CellularGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<PerlinGetterDomainRotated>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<SimplexGetterDomainRotated>, WriteTileData>.ScheduleParallel
        };

        public FractalNoise noiseType;
        [Range(0f, 2f)]
        public float hurst = 0f;
        
        [Range(.01f, 5f)]
        public float startingAmplitude = 1f;
        
        [Range(1, 24)]
        public int octaves = 1;

        [Range(1.8f, 2.2f)]
        public float stepdown = 2f;
        
        [Range(-.05f, .05f)]
        public float detuneRate = 0f;

        [Range(5, 32000)]
        public int noiseSize = 1000;
        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency ){
            CheckRequirements<GeneratorData>(requirements);
            GeneratorData d = (GeneratorData) requirements.data;
            jobHandle = jobs[(int)noiseType](
                d.data, d.resolution, hurst, startingAmplitude, stepdown, detuneRate, octaves, d.xpos, d.zpos, noiseSize, dependency);
        }
    }
}