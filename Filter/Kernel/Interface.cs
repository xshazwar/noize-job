using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {
    using Unity.Mathematics;

    public interface ICommonTileSettings {
        int JobLength {get; set;}
        int Resolution {get; set;}
    }

    public interface ITileSource: ICommonTileSettings {
        void SetPosition(int x, int z);
        void Execute<T>(int i, T tile) where  T : struct, IWriteOnlyTile; 
    }

    public interface IFractalSettings {
        public float Hurst {get; set;}
        public float StartingAmplitude {get; set;}
        public int OctaveCount {get; set;}
        public int NoiseSize {get; set;}
        public float StepDown {get; set;}
        public float DetuneRate {get; set;}
        public float NormalizationValue {get; set;}
    }

    public interface IMakeNoise {
        float NoiseValue(float x, float z);
    }

    public interface IMutateTiles: ICommonTileSettings {
        // total resolution including margin
        void Execute<T>(int i, T tile) where  T : struct, IRWTile; 
    }

    public interface IReduceTiles : ICommonTileSettings {
        // tile A is left side, B is right
        // result put onto A
        void Execute<T, V>(int i, T tileA, V tileB) 
                where  T : struct, IRWTile
                where V: struct, IReadOnlyTile; 
    }

    public interface IConstantTiles : ICommonTileSettings {
        // tile A is left side, factor is constant in operation
        // result put onto A

        public float ConstantValue {get; set;}
        void Execute<T>(int i, T tileA) 
                where  T : struct, IRWTile; 
    }

    public interface IApplyCurve: ICommonTileSettings {
        public int CurveSize {get; set;}
        public void Setup(int resolution, int jobLength, int curveSize);
        void Execute<T>(int i, T tile, NativeSlice<float> curve) where  T : struct, IRWTile;
    }

    public interface IKernelData {
        public void Setup(float kernelFactor, int kernelSize, NativeArray<float> kernel);
    }
    
    public interface IKernelOperator {
        public void ApplyKernel<T>(int x, int z, T tile) where  T : struct, IRWTile;
    }
    public interface IMathTiles : IMutateTiles {}

    public interface INormalizeMap: ICommonTileSettings {

        void Execute<RW>(int z, RW map, NativeSlice<float> args) 
            where  RW : struct, IRWTile;
    }
}