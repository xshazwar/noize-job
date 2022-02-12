using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.processing.cpu.mutate {
    using Unity.Mathematics;

    public struct FlushWriteSlice : IJob {
        NativeSlice<float> read;
        NativeSlice<float> write;

        public static JobHandle Schedule(NativeSlice<float> read_, NativeSlice<float> write_, JobHandle deps){
            var job = new FlushWriteSlice();     
            job.read = read_;
            job.write = write_;
            return job.Schedule(deps);
        }

        public void Execute(){
            read.CopyFrom(write);
        }
    }

    public delegate JobHandle FlushWriteSliceDelegate(NativeSlice<float> read_, NativeSlice<float> write_, JobHandle deps);

    public struct SwapBuffers<T>: IJob {
        T a;
        T b;
        
        public static JobHandle Schedule(T a_, T b_, JobHandle deps){
            var job = new SwapBuffers<T>();     
            job.a = a_;
            job.b = b_;
            return job.Schedule(deps);
        }

        public void Execute(){
            T tmp = a;
            a = b;
            b = tmp;
        }
    }

    public delegate JobHandle SwapBuffersDelegate<T>(T a_, T b_, JobHandle deps);

    static class TileHelpers {
        public static FlushWriteSliceDelegate SWAP_RWTILE =  FlushWriteSlice.Schedule;
    
    }

    public struct RWTileData: IRWTile{
        
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        NativeSlice<float> src;

        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [WriteOnly]
        NativeSlice<float> dst;
        int resolution;

        public void Setup(NativeSlice<float> src_, NativeSlice<float> dst_, int resolution_){
            dst = dst_;
            // in this case we can avoid the alloc by passing in properly sized slice for reuse
            src = src_;
            resolution = resolution_;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, resolution - 1);
            z = clamp(z, 0, resolution - 1);
            return (z * resolution) + x;   
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetData(int x, int z){
            return src[getIdx(x,z)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetValue(int x, int z, float value){
            dst[getIdx(x,z)] = value;
        }
    }

    public struct ReadTileData: IReadOnlyTile{

        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        NativeSlice<float> src;
        int resolution;

        public void Setup(NativeSlice<float> src_, int resolution_){
            src = src_;
            resolution = resolution_;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, resolution - 1);
            z = clamp(z, 0, resolution - 1);
            return (z * resolution) + x;   
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetData(int x, int z){
            return src[getIdx(x,z)];
        }

    }
    public struct WriteTileData: IWriteOnlyTile{

        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        [WriteOnly]
        NativeSlice<float> dst;
        int resolution;

        public void Setup(NativeSlice<float> dst_, int resolution_){
            dst = dst_;
            resolution = resolution_;

        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // shouldn't need to overflow at all
            return (z * resolution) + x;  
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetValue(int x, int z, float value){
            dst[getIdx(x,z)] = value;
        }
    }
}