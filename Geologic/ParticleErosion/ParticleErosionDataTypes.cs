using System;
using System.Collections.Generic;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;
using xshazwar.noize.filter;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    struct LazyFloatComparer: IComparer<float> {
        public int Compare(float a, float b){
            float diff = abs(b - a);
            if (abs(diff) < .000000001){
                return 0;
            }
            if (diff > 0){
                return 1;
            }
            return -1;
        }
    }

/*
// 
//   POOLS
// 
*/

    public struct PoolKey : IEquatable<PoolKey>, IComparable<PoolKey> {
        // This can be used to reference a minima or a drain. Anything that is ambigous between pool orders
        public int idx;
        // A minima can host multiple successive pools of different characteristics, so we allow an order here. Zero is the smallest pool
        public byte order;
        public byte n;
    
        public bool Equals(PoolKey other){
            if (idx != other.idx){
                return false;
            }
            return (order == other.order && n == other.n);
        }

        public int CompareTo(PoolKey obj){
            if (obj.Equals(this)){ return 0;}
            return GetHashCode() > obj.GetHashCode() ? 1 : -1;
        }
     
        public override int GetHashCode(){
            return idx.GetHashCode() ^ (order.GetHashCode() * n.GetHashCode());
            // return idx.GetHashCode() + (order.GetHashCode() * n.GetHashCode());
        }

        public PoolKey Clone(){
            return new PoolKey {
                idx = idx,
                order = order,
                n = n
            };
        }
    
    }

    public struct Pool {
        public int indexMinima;
        public float minimaHeight;
        public int indexDrain;
        public float drainHeight;
        public float capacity;
        public float volume;
        // Beta1 for regression
        public float b1;
        // Beta2 for regression
        public float b2;
        
        // In cases where two pools flow into each other. When both are full a new pool is created. This is the reference
        public PoolKey supercededBy;

        public void Init(int indexMinima_, float minimaHeight_, int indexDrain_, float drainHeight_){
            indexMinima = indexMinima_;
            minimaHeight = minimaHeight_;
            indexDrain = indexDrain_;
            drainHeight = drainHeight_;
            volume = 0f;
            supercededBy = new PoolKey {idx = -1};
        }

        public void EstimateHeight(float cellHeight, out float waterHeight){
            waterHeight = (b1 + b2 * log(volume)) - (cellHeight - minimaHeight);
        }
        
        public void SolvePool(NativeArray<float> heights){
            capacity = 0f;
            NativeArray<float> trainingVolumes = new NativeArray<float>(heights.Length, Allocator.Temp);

            heights.Sort<float, LazyFloatComparer>(new LazyFloatComparer());
            minimaHeight = heights[0];
            float volumeBehind = 0f;
            trainingVolumes[0] = .0000001f;
            for (int i = 0 ; i < heights.Length; i++){
                capacity += drainHeight - heights[i];
                if (i > 0){
                    volumeBehind += ((heights[i] - heights[i - 1]) * i);
                    trainingVolumes[i] = volumeBehind;
                }  
            }
            Regression regressor = new Regression();
            b1 = 0f;    
            b2 = 0f;
            regressor.LogRegression(heights, trainingVolumes, out b1, out b2);
        }
    }

    public static class CardinalExtension {

        public static readonly int[] NIBBLE_LU = new int[] {
            0, 1, 1, 2, 1, 2, 2, 3, 
            1, 2, 2, 3, 2, 3, 3, 4
        };

        public static readonly Cardinal WE = (Cardinal.E | Cardinal.W);
        public static readonly Cardinal NS = Cardinal.N | Cardinal.S;
        public static readonly Cardinal D1 = Cardinal.NE | Cardinal.SW;
        public static readonly Cardinal D2 = Cardinal.NW | Cardinal.SE;
        
        public static int HammingW(this Cardinal b){
            return NIBBLE_LU[((int) b) & 0x0F] + NIBBLE_LU[((int) b) >> 4];
        }

        public static bool StraightLine(this Cardinal b){
            return ((Cardinal)(b | WE)) == b || (((Cardinal) b | NS) == b);
        }

        public static bool Diagnal(this Cardinal b){
            return ((Cardinal)(b | D1)) == b || (((Cardinal) b | D2) == b);
        }

    
    }
    

    public enum Cardinal : byte {
        // Cardinal[i] where i < 8 === (Cardinal)( ( ((byte) 1) << i ) )
        
        // cardinals
        NW = 0b_00000001,
        SE = 0b_00000010,
        N  = 0b_00000100,
        S  = 0b_00001000,
        NE = 0b_00010000,
        SW = 0b_00100000,
        E  = 0b_01000000,
        W  = 0b_10000000,
        // convenience
        X  = 0b_00000000,
        A  = 0b_11111111

    }

}