using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
}