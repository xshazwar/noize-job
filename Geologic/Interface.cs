using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;
using Unity.Profiling;

using xshazwar.noize.filter;
using xshazwar.noize.pipeline;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;


    // Flow Map

    public interface IComputeFlowData: ICommonTileSettings {
        void Execute<RO, RW>(int i, RO height, RO water, RW flowN, RW flowS, RW flowE, RW flowW) 
            where  RO : struct, IReadOnlyTile
            where  RW : struct, IRWTile;
    }

   public interface IComputeWaterLevel: ICommonTileSettings {

        void Execute<RO, RW>(int i, RW water, RO flowN, RO flowS, RO flowE, RO flowW) 
            where  RO : struct, IReadOnlyTile
            where  RW : struct, IRWTile;
    }

    

    public interface IWriteFlowMap: ICommonTileSettings {

        void Execute<RO, WO>(int z, WO height, RO flowN, RO flowS, RO flowE, RO flowW) 
            where  RO : struct, IReadOnlyTile
            where  WO : struct, IWriteOnlyTile;
    }

    // Particle Erosion

    public interface IPoolSuperPosition {
        void CreateSuperPositions(int z, NativeStream.Writer minimaStream);
        void CollapseMinima(
            int minimaIdx,
            NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterBM,
            NativeParallelMultiHashMap<int, int>.ParallelWriter boundaryWriterMB,
            NativeParallelHashMap<int, int>.ParallelWriter catchmentWriter,
            ProfilerMarker? profiler = null
        );

        void SolvePoolHeirarchy(
            NativeParallelMultiHashMap<int, int> boundaryMapMemberToMinima,
            NativeParallelMultiHashMap<int, int> boundaryMapMinimaToMembers,
            NativeParallelHashMap<int, int> catchmentMap
        );
        void SetupCollapse(
            int resolution,
            NativeArray<Cardinal> flow_,
            NativeSlice<float> heightMap_,
            NativeSlice<float> outMap_);
        
        void SetupPoolGeneration(
            int resolution,
            NativeSlice<float> heightMap_,
            NativeSlice<float> outMap_
        );


    }

    public interface IParticle {

        void Reset<P>(int2 maxPos, P prototype) where P : struct, IParticle;

        void SetPosition(int x, int y);
        public int2 GetPosition();

        void Consume<P>(P part) where P : struct, IParticle;

        void Effect<RW>(RW tile)
            where RW: struct, IRWTile;
    }

    public interface IParticleManager {
        void Execute<P>(NativeSlice<P> particles) where P : struct, IParticle;
    }

    public interface IParticleErode : ICommonTileSettings {

        void Execute<P, RW>(int i, NativeSlice<P> particles, RW tile)
            where P : struct, IParticle
            where RW: struct, IRWTile;

    }
}