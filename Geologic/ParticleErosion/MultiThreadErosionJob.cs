using System;

using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.tile;
using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct FillBeyerQueueJob: IJobParallelFor {
        FlowMaster fm;
        [NativeDisableContainerSafetyRestriction]
        NativeQueue<BeyerParticle>.ParallelWriter particleWriter;
        // ErosionParameters ep;
        // TileSetMeta tm;

        int generationRound;
        int maxParticles;
        int RND_SEED;
        int COUNT;


        public void Execute(int i){
            fm.CreateRandomParticles(i, generationRound, maxParticles, COUNT, RND_SEED + i, ref particleWriter);
        }

        public static JobHandle ScheduleParallel(
            NativeQueue<BeyerParticle> particles,
            ErosionParameters ep,
            TileSetMeta tm,
            int generationRound,
            int res,
            int maxParticles,
            JobHandle deps,
            int concurrency = 10
        ){
            int threads = concurrency;
            int currentParticles = particles.Count;
            int seed = UnityEngine.Random.Range(0, Int32.MaxValue);
            // int required = max(1000, maxParticles - currentParticles);
            int required = max(1, maxParticles - currentParticles);
            // Debug.Log($"{required} new particles needed");
            var job = new FillBeyerQueueJob {
                generationRound = generationRound,
                maxParticles = maxParticles,
                fm = new FlowMaster {
                    ep = ep,
                    tile = new WorldTile {
                        tm = tm
                    }
                },
                // ep = ep,
                // tm = tm,
                particleWriter = particles.AsParallelWriter(),
                RND_SEED = seed,
                COUNT = (int) max(floor(required / concurrency), 1)
            };
            return job.Schedule<FillBeyerQueueJob>(threads, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct CopyBeyerQueueJob: IJob {
        NativeList<BeyerParticle> particles;

        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        NativeQueue<BeyerParticle> particleQueue;

        public void Execute(){
            NativeArray<BeyerParticle> temp = particleQueue.ToArray(Allocator.Temp);
            particles.CopyFrom(temp);
        }

        public static JobHandle ScheduleRun(
            NativeList<BeyerParticle> particles,
            NativeQueue<BeyerParticle> particleQueue,
            JobHandle deps
        ){
            
            var job = new CopyBeyerQueueJob {
                particles = particles,
                particleQueue = particleQueue
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct TestPileSolverJob: IJob {
        
        // [NativeDisableContainerSafetyRestriction]
        PileSolver solver;

        public void Execute(){
            solver.Init(50);
            for(int x = 50; x < 250; x+= 25){
                for(int z = 50; z < 250; z +=25){
                    solver.HandlePile(new int2(x, z), (((float)x) / (float)(5))  , 0.0005f);
                }
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            TileSetMeta tm,
            JobHandle deps
        ){
            
            var job = new TestPileSolverJob {
                solver = new PileSolver{
                tile = new WorldTile {
                        tm = tm,
                        height = height
                    }
                }
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ClearQueueJob<T>: IJob where T: struct{
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        NativeQueue<T> queue;

        public void Execute(){
            queue.Clear();
        }

        public static JobHandle ScheduleRun(
            NativeQueue<T> queue,
            JobHandle deps
        ){
            
            var job = new ClearQueueJob<T> {
                queue = queue
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ClearMultiDict<K, V>: IJob where K: struct, IEquatable<K> where V: struct{
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        NativeParallelMultiHashMap<K, V> dict;

        public void Execute(){
            dict.Clear();
        }

        public static JobHandle ScheduleRun(
            NativeParallelMultiHashMap<K, V> dict,
            JobHandle deps
        ){
            
            var job = new ClearMultiDict<K, V> {
                dict = dict
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct QueuedBeyerCycleMultiThreadJob: IJobParallelForDefer {
        FlowMaster fm;
        [ReadOnly]
        NativeArray<BeyerParticle> particles;

        public void Execute(int i){
            NeighborhoodHelper nbh = new NeighborhoodHelper {
                nb = new NativeArray<int>(8, Allocator.Temp),
                nbSort = new NativeArray<int>(8, Allocator.Temp),
                nbDir = NeighborhoodHelper.generateLookupDir()
            };
            BeyerParticle p = particles[i];
            fm.BeyerSimultaneousDescentSingle(ref p, ref nbh);
        }

        public static JobHandle ScheduleParallel(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeList<BeyerParticle> particles,
            NativeParallelMultiHashMap<int, ErosiveEvent> events,
            ErosionParameters ep,
            TileSetMeta tm,
            int eventLimit,
            int res,
            JobHandle deps
        ){
            var job = new QueuedBeyerCycleMultiThreadJob {
                fm = new FlowMaster {
                    tile = new WorldTile {
                        tm = tm,
                        height = height,
                        pool = pool,
                        flow = flow,
                        track = track
                    },
                    ep = ep,
                    events = events,
                    eventWriter = events.AsParallelWriter()
                },
                particles = particles.AsDeferredJobArray()
            };
            return job.Schedule<QueuedBeyerCycleMultiThreadJob, BeyerParticle>(particles, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct UpdateFlowFromTrackJob: IJobFor {
        WorldTile tile;
        int flip;
        int res;
        float FLOW_LOSS_RATE;
        float SURFACE_EVAPORATION_RATE;

        public void Execute(int z){
            for (int x = 0; x < res; x++){
                tile.UpdateFlowMapFromTrack(x, z, FLOW_LOSS_RATE, SURFACE_EVAPORATION_RATE);
            }
        }

        public static JobHandle Schedule(
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            ErosionParameters ep,
            TileSetMeta tm,
            int res,
            JobHandle deps
        ){
            var job = new UpdateFlowFromTrackJob(){
                res = res,
                tile = new WorldTile {
                    tm = tm,
                    pool = pool,
                    flow = flow,
                    track = track
                },
                FLOW_LOSS_RATE = ep.FLOW_LOSS_RATE,
                SURFACE_EVAPORATION_RATE = ep.SURFACE_EVAPORATION_RATE
            };
            return job.ScheduleParallel(res, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct PoolAutomataJob: IJobFor {
        WorldTile tile;
        ErosionParameters ep;
        [NativeDisableContainerSafetyRestriction]
        [NoAlias]
        NativeQueue<BeyerParticle>.ParallelWriter particleWriter;
        int res;
        int xoff;
        int zoff;
        bool drainParticles;

        public void Execute (int z) {
            NativeArray<FloodedNeighbor> buff = new NativeArray<FloodedNeighbor>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            int offset = xoff;
            if(z % 2 != 0){
                offset += 1;
            }
            z *= 2;
            z += zoff;
            for (int x = offset; x < res; x += 2){
                if(tile.pool[tile.getIdx(x,z)] > 0f) {
                    tile.SpreadPool(x, z, ref buff, ref particleWriter, ref ep, drainParticles);
                }
            }
        }

        public static JobHandle Schedule(
            NativeArray<float> pool,
            NativeArray<float> height,
            NativeQueue<BeyerParticle> particleQueue,
            ErosionParameters ep,
            TileSetMeta tm,
            int iterations,
            int res,
            bool drainParticles,
            JobHandle deps
        ){
            JobHandle handle = deps;
            var job = new PoolAutomataJob(){
                ep = ep,
                res = res,
                drainParticles = drainParticles,
                particleWriter = particleQueue.AsParallelWriter(),
                tile = new WorldTile {
                    tm = tm,
                    pool = pool,
                    height = height
                    // res = new int2(res, res)
                }
            };
            for (int i = 0; i < iterations; i++){
                for(int xoff = 0; xoff < 2; xoff++){
                    for(int zoff = 0; zoff < 2; zoff ++){
                        job.xoff = xoff;
                        job.zoff = zoff;
                        handle = job.ScheduleParallel(
                            (int) (res / 2) , 1, handle
                        );
                    }  
                }
            }
            return handle;
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ProcessBeyerErosiveEventsJob: IJobFor {

        NativeQueue<ErosiveEvent>.ParallelWriter erosionWriter;
        FlowMaster fm;
        int res;
 
        public void Execute(int z){
            int idx = 0;
            float poolV = 0f;
            float trackV = 0f;
            float sedimentV = 0f;
            NativeParallelMultiHashMap<int, ErosiveEvent>.Enumerator eventIter;
            for (int x = 0; x < res; x++){
                poolV = 0f;
                trackV = 0f;
                sedimentV = 0f;
                idx = x * res + z;
                eventIter = fm.events.GetValuesForKey(idx);
                while(eventIter.MoveNext()){
                    fm.CombineBeyerEvents(eventIter.Current, ref poolV, ref trackV, ref sedimentV);
                }
                fm.HandleBeyerEvent(idx, poolV, trackV, sedimentV, ref erosionWriter);
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeArray<float> pool,
            NativeArray<float> flow,
            NativeArray<float> track,
            NativeQueue<ErosiveEvent> erosions,
            NativeParallelMultiHashMap<int, ErosiveEvent> events,
            ErosionParameters ep,
            TileSetMeta tm,
            int res,
            JobHandle deps
        ){
            var job = new ProcessBeyerErosiveEventsJob {
                res = res,
                erosionWriter = erosions.AsParallelWriter(),
                fm = new FlowMaster {
                    tile = new WorldTile {
                        tm = tm,
                        height = height,
                        pool = pool,
                        flow = flow,
                        track = track
                    },
                    ep = ep,
                    events = events
                }
            };
            return job.ScheduleParallel(res, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct CurvitureMapJob: IJobFor {
        
        WorldTile tile;
        [NativeDisableContainerSafetyRestriction]
        NativeSlice<byte> target;
        public int dataRes;
        public int offset;
        public int meshRes;

        public void Execute(int z){
            float l = tile.tm.PATCH_RES.x;
            float v = 0f;
            int srcZ = z + offset;
            int srcI = 0;
            int i = 0;
            int2 pos = new int2(srcZ, 0);
            // int2 pos = new int2(0, srcZ);
            for (int x = 0; x < meshRes; x++){
                pos.y = x + offset;
                i = z * meshRes + x;
                v = tile.Curviture(pos, l);
                target[i] = (byte) (clamp(v, 0f, 1f) * 255f);
            }
        }

        public static JobHandle ScheduleRun(
            Texture2D texture,
            NativeArray<float> height,
            TileSetMeta tm,
            ColorChannelByte target,
            int res,
            JobHandle deps
        ){
            int meshRes = texture.height;
            int offset = ((res - meshRes) / 2);
            NativeSlice<byte> data = new NativeSlice<RGBA32>(texture.GetRawTextureData<RGBA32>()).SliceWithStride<byte>((int) target);
            var job = new CurvitureMapJob {
                target = data,
                dataRes = res,
                meshRes = meshRes,
                offset = offset,
                tile = new WorldTile {
                    tm = tm,
                    height = height
                }
            };
            return job.ScheduleParallel(meshRes, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct ErodeHeightMaps: IJob {

        FlowMaster fm;
        NativeQueue<ErosiveEvent> erosions;
 
        public void Execute(){
            NativeArray<float> kernel3 = new NativeArray<float>(3, Allocator.Temp){
                [0] = 0.30780132912347f,
                [1] = 0.38439734175306006f,
                [2] = 0.30780132912347f
            };
            NativeArray<float> kernel5 = new NativeArray<float>(5, Allocator.Temp){
                [0] = 0.12007838424321349f,
                [1] = 0.23388075658535032f,
                [2] = 0.29208171834287244f,
                [3] = 0.23388075658535032f,
                [4] = 0.12007838424321349f
            };
            fm.WriteSedimentMap(ref erosions, 5, ref kernel5);
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> height,
            NativeQueue<ErosiveEvent> erosions,
            ErosionParameters ep,
            TileSetMeta tm,
            int res,
            JobHandle deps
        ){
            var job = new ErodeHeightMaps {
                erosions = erosions,
                fm = new FlowMaster {
                    ep = ep,
                    tile = new WorldTile {
                        // res = new int2(res, res),
                        tm = tm,
                        height = height
                    }
                }
            };
            return job.Schedule(deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct SetRGBA32Job : IJobFor
    {
        ColorChannelByte color;
        [ReadOnly]
        public NativeArray<float> src;
        [NativeDisableContainerSafetyRestriction]
        public NativeSlice<byte> data;
        public int dataRes;
        public int offset;
        public int meshRes;
        public float scale;

        public void Execute (int z)
        {
            // z += offset;
            int srcZ = z + offset;
            int srcI = 0;
            int i = 0;
            for (int x = 0; x < meshRes; x++){
            // for (int x = offset; x < dataRes - offset; x++){
                srcI = srcZ * dataRes + x + offset;
                i = z * meshRes + x;
                byte b = (byte) (clamp((src[srcI] * scale), 0, 1f) * 255f);
                data[i] = b;
            }
        }

        public static JobHandle ScheduleRun(
            NativeArray<float> src,
            Texture2D texture,
            ColorChannelByte target,
            JobHandle deps,
            float scale = 1f
        ){
            NativeSlice<byte> data = new NativeSlice<RGBA32>(texture.GetRawTextureData<RGBA32>()).SliceWithStride<byte>((int) target);
            int meshRes = texture.height;
            int dataRes = (int) sqrt(src.Length);
            int offset = ((dataRes - meshRes) / 2);
            // Debug.LogWarning($"{dataRes} >> {meshRes} + 2 * {offset}");
            var job = new SetRGBA32Job {
                color = target,
                src = src,
                data = data,
                dataRes = dataRes,
                meshRes = meshRes,
                offset = offset,
                scale = scale
            };
            return job.ScheduleParallel(meshRes, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct SetRGBA32ColorJob : IJobFor
    {
        int targetColorIdx;
        ColorChannelByte color;
        float filter;
        int idxCutoff;
        public NativeSlice<RGBA32> data;
        public void Execute (int i)
        {
            RGBA32 d = data[i];
            if(i <= idxCutoff){
                d[(ColorChannelByte)(targetColorIdx % 4)] = (byte) 255f;
                // d[(ColorChannelByte)(targetColorIdx % 4)] = (byte) ((0.5 + (sin(i / 520 ))) * 255);
            }else{
                d[(ColorChannelByte)(targetColorIdx % 4)] = (byte) 0;
            }
            d[(ColorChannelByte)((targetColorIdx + 1) % 4)] = (byte) 0;
            d[(ColorChannelByte)((targetColorIdx + 2) % 4)] = (byte) 0;
            d[(ColorChannelByte)((targetColorIdx + 3) % 4)] = (byte) 0;
            data[i] = d;
            // if(src[i] >= filter){
            //     data[i] = (byte) (src[i] * 256f);
            // }else{
            //     data[i] = (byte) 1f;
            // }
        }
        public static JobHandle ScheduleRun(
            Texture2D texture,
            ColorChannelByte target,
            JobHandle deps,
            float filterLevel = 0.05f
        ){
            // NativeSlice<byte> data = new NativeSlice<RGBA32>(texture.GetRawTextureData<RGBA32>()).SliceWithStride<byte>();
            int size = texture.width * texture.height;
            var job = new SetRGBA32ColorJob {
                idxCutoff = size, //texture.Length, // (int) (src.Length / 2),
                targetColorIdx = ((int) target) + 4,
                color = target,
                data = new NativeSlice<RGBA32>(texture.GetRawTextureData<RGBA32>()),
                filter = filterLevel
            };
            return job.ScheduleParallel(size, 1, deps);
        }
    }

    [BurstCompile(FloatPrecision.High, FloatMode.Fast, CompileSynchronously = true)]
    public struct SetTextureBlackJob : IJobFor
    {
        public NativeSlice<RGBA32> data;
        public void Execute (int i)
        {
            RGBA32 d = data[i];
            d.R = (byte) 0;
            d.G = (byte) 0;
            d.B = (byte) 0;
            d.A = (byte) 0;
            data[i] = d;
        }
        public static JobHandle ScheduleRun(
            Texture2D texture,
            JobHandle deps
        ){
            int size = texture.width * texture.height;
            var job = new SetTextureBlackJob {
                data = new NativeSlice<RGBA32>(texture.GetRawTextureData<RGBA32>())
            };
            return job.ScheduleParallel(size, 1, deps);
        }
    }
}