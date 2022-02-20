using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using xshazwar.noize.pipeline;


namespace xshazwar.noize.cpu.mutate {
    
    public class GeneratorData : StageIO {
        public int resolution;
        public int xpos;
        public int zpos;
        public NativeSlice<float> data;

        public GeneratorData(int resolution, int xpos, int zpos, NativeSlice<float> data){
            this.resolution = resolution;
            this.xpos = xpos;
            this.zpos = zpos;
            this.data = data;
        }
    }
    [CreateAssetMenu(fileName = "NoiseGenerator", menuName = "Noize/Generators/NoiseSource", order = 1)]
    public class NoiseStage: PipelineStage {
        
        public enum FractalNoise {
            Sin,
            Perlin,
            PeriodicPerlin,
            Simplex,
            RotatedSimplex,
            Cellular
        }

        static MapNormalizeValuesDelegate normStage = MapNormalizeValues<NormalizeMap, RWTileData>.ScheduleParallel;

        static FractalJobDelegate[] jobs = {
            FractalJob<FractalGenerator<SinGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<PerlinGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<PeriodicPerlinGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<SimplexGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<RotatedSimplexGetter>, WriteTileData>.ScheduleParallel,
            FractalJob<FractalGenerator<CellularGetter>, WriteTileData>.ScheduleParallel
        };

        public FractalNoise noiseType;
        [Range(0f, 1f)]
        public float hurst = 0f;
        
        [Range(1, 24)]
        public int octaves = 1;

        [Range(5, 10000)]
        public int noiseSize = 1000;
        public override void Schedule( StageIO req ){
            GeneratorData d = (GeneratorData) req;
            Debug.Log($"{d.resolution}, {d.xpos}, {d.zpos}, {d.data.Length}");
            jobHandle = jobs[(int)noiseType](
                d.data, d.resolution, hurst, octaves, d.xpos, d.zpos, noiseSize, default);
        }
    }
}