using Unity.Collections.LowLevel.Unsafe;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.processing.cpu.mutate {
    using Unity.Mathematics;

    public struct ComputeFlowStep: IComputeFlowData {
        public int Resolution {get; set;}
        public int JobLength {get; set;}
        private const float TIMESTEP = 0.2f;
        void CalculateCell<RO, RW>(int x, int z, RO height, RO water, RW flowN, RW flowS, RW flowE, RW flowW) 
            where  RO : struct, IReadOnlyTile
            where  RW : struct, IRWTile
        {

            float height_0 = height.GetData(x, z);
            float water_0 = water.GetData(x, z);

            int xW = x - 1;
            int xE = x + 1;
            int zS = z - 1;
            int zN = z + 1;
            
            float totalHt = water_0 + height_0;

            float4 diff = new float4( //W, E, S, N
                totalHt - (water.GetData(xW, z) + height.GetData(xW, z)),
                totalHt - (water.GetData(xE, z) + height.GetData(xE, z)),
                totalHt - (water.GetData(x, zS) + height.GetData(x, zS)),
                totalHt - (water.GetData(x, zN) + height.GetData(x, zN))
            );

            float4 flow = new float4( //W, E, S, N
                max(0, flowW.GetData(x, z) + diff.x ),
                max(0, flowE.GetData(x, z) + diff.y ),
                max(0, flowS.GetData(x, z) + diff.z ),
                max(0, flowN.GetData(x, z) + diff.w )
            );

            float sum_ = csum(flow);

            if (sum_ > 0){
                float K = water_0 / (sum_ * TIMESTEP);
                K = clamp(K, 0, 1);

                flowW.SetValue(x, z, flow.x * K);
                flowE.SetValue(x, z, flow.y * K);
                flowS.SetValue(x, z, flow.z * K);
                flowN.SetValue(x, z, flow.w * K);
            }else{
                flowW.SetValue(x, z, 0);
                flowE.SetValue(x, z, 0);
                flowS.SetValue(x, z, 0);
                flowN.SetValue(x, z, 0);
            }
        }

        public void Execute<RO, RW>(int z, RO height, RO water, RW flowN, RW flowS, RW flowE, RW flowW) 
            where  RO : struct, IReadOnlyTile
            where  RW : struct, IRWTile {
            for( int x = 0; x < Resolution; x++){
                CalculateCell<RO, RW>(x, z, height, water, flowN, flowS, flowE, flowW);
            }
        }
    }

    public struct UpdateWaterStep: IComputeWaterLevel {
        public int Resolution {get; set;}
        public int JobLength {get; set;}
        private const float TIMESTEP = 0.2f;

        public void CalculateCell<RO, RW>(int x, int z, RW water, RO flowN, RO flowS, RO flowE, RO flowW) 
            where  RO : struct, IReadOnlyTile
            where  RW : struct, IRWTile {
            
            float flowOUT = flowW.GetData(x, z) +
                flowE.GetData(x, z) +
                flowS.GetData(x, z) +
                flowN.GetData(x, z);
            float flowIN = 0;
            
            //from W flowing E
            flowIN += flowE.GetData(x - 1, z);
            //from E flowing W
            flowIN += flowW.GetData(x + 1, z);
            //from S flowing N
            flowIN += flowN.GetData(x, z - 1);
            //from N flowing S
            flowIN += flowS.GetData(x, z + 1);

            float ht = water.GetData(x, z) + ((flowIN - flowOUT) * TIMESTEP);
            ht = max(0, ht);
            water.SetValue(x, z, ht);
            
        }

        public void Execute<RO, RW>(int z, RW water, RO flowN, RO flowS, RO flowE, RO flowW) 
            where  RO : struct, IReadOnlyTile
            where  RW : struct, IRWTile {
            for( int x = 0; x < Resolution; x++){
                CalculateCell<RO, RW>(x, z, water, flowN, flowS, flowE, flowW);
            }
        }
    }

    public struct CreateVelocityField: IWriteFlowMap {
        public int Resolution {get; set;}
        public int JobLength {get; set;}
        private const float TIMESTEP = 0.2f;

        public void CalculateCell<RO, WO>(int x, int z, WO height, RO flowN, RO flowS, RO flowE, RO flowW) 
            where  RO : struct, IReadOnlyTile
            where  WO : struct, IWriteOnlyTile {
            float4 dd = new float4(
                flowE.GetData(x - 1, z) - flowW.GetData(x, z), // dl
                flowE.GetData(x, z) - flowW.GetData(x + 1, z), // dr
                flowS.GetData(x, z + 1) - flowN.GetData(x, z), // dt
                flowS.GetData(x, z) - flowN.GetData(x, z - 1)  // db
            );
            // float vx = (dd.x + dd.y) * 0.5f;
            // float vy = (dd.z + dd.w) * 0.5f;

            // height.SetValue(x, z, sqrt(vx*vx + vy*vy));

            float2 velocity = new float2(
                (dd.x + dd.y) * 0.5f,
                (dd.z + dd.w) * 0.5f
            );
            height.SetValue(x, z, sqrt(lengthsq(velocity)));
        }

        public void Execute<RO, WO>(int z, WO height, RO flowN, RO flowS, RO flowE, RO flowW) 
            where  RO : struct, IReadOnlyTile
            where  WO : struct, IWriteOnlyTile {
            for( int x = 0; x < Resolution; x++){
                CalculateCell<RO, WO>(x, z, height, flowN, flowS, flowE, flowW);
            }
        }
    }

    public struct NormalizeMap : INormalizeMap {
        public int Resolution {get; set;}
        public int JobLength {get; set;}
        public float MAX {get; set;}
        public float MIN {get; set;}
        public float NORMTO {get; set;}

        public void CalculateCell<RW>(int x, int z, RW map, NativeArray<float> args) 
            where  RW : struct, IRWTile
        {
                float v = map.GetData(x, z);
                if (args[2] < 1e-12f){
                    v = 0;
                }
                map.SetValue(x, z, (v - args[0]) / args[2]);
        }

        public void Execute<RW>(int z, RW map, NativeArray<float> args) 
            where  RW : struct, IRWTile{
            for( int x = 0; x < Resolution; x++){
                CalculateCell<RW>(x, z, map, args);
            }
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct GetMapRangeJob: IJob {

        public const int MIN = 0;
        public const int MAX = 1;
        public const int RANGE = 2;
        [ReadOnly]
        NativeSlice<float> map;
        [WriteOnly]
        NativeArray<float> res;

        float HIGHEST_MIN;
        float LOWEST_MAX;

        public void Execute(){
            // float min_ = float.PositiveInfinity;
            float min_ = HIGHEST_MIN;
            float max_ = LOWEST_MAX;
            for (int i = 0; i < map.Length; i++){
                min_ = min(min_, map[i]);
                max_ = max(max_, map[i]);
            }
            res[MIN] = min_;
            res[MAX] = max_;
            res[RANGE] = max_ - min_;
        }

        public JobHandle Schedule(NativeSlice<float> map_, NativeArray<float> res_, JobHandle dep, float lim_min = float.PositiveInfinity, float lim_max = float.NegativeInfinity)
        {
            var job = new GetMapRangeJob();
            job.map = map_;
            job.res = res_;
            job.HIGHEST_MIN = lim_min;
            job.LOWEST_MAX = lim_max;
            return job.Schedule(dep);

        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct FillArrayJob: IJobFor{
        
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [WriteOnly]
        NativeArray<float> data;

        public int resolution;
        public float value;
        public void Execute(int z){
            for (int x = 0; x < resolution; x++){
                data[(z * resolution) + x] = value;
            }  
        }

        public static JobHandle ScheduleParallel(NativeArray<float> data, int resolution, float value, JobHandle deps){
            var job = new FillArrayJob();
            job.data = data;
            job.value = value;
            job.resolution = resolution;
            return job.ScheduleParallel(
                resolution, 8, deps
            );
        }
    }

    public delegate JobHandle FillArrayJobDelegate(NativeArray<float> data, int resolution, float value, JobHandle deps);

}