using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.processing.cpu.mutate {
    using Unity.Mathematics;    
    public interface ImTileData {
        void Setup(NativeSlice<float> source, int resolution);
    }

    public interface ImBufferedTileData {
        void Setup(NativeSlice<float> source, NativeSlice<float> dst, int resolution);
    }

    public interface IGetTileData {
        public float GetData(int x, int z);
    }

    public interface ISetTileData {
        void SetValue(int x, int z, float value);
    }

    public interface ICleanUpTiles{
        public JobHandle Dispose(JobHandle dep);
    }

    public interface IReadOnlyTile : ImTileData, IGetTileData{}
    public interface IWriteOnlyTile: ImTileData, ISetTileData{}
    public interface IRWTile: ImBufferedTileData, IGetTileData, ISetTileData{}
}