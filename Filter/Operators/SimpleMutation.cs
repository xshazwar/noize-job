using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

using static Unity.Mathematics.math;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.filter {
    using Unity.Mathematics;

    public struct ConstantMultiply: IConstantTiles {
        public float ConstantValue {get; set;}
        public int JobLength {get; set;}
        public int Resolution {get; set;}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T>(int x, int z, T tileA)
                where T : struct, IRWTile {
            float val = tileA.GetData(x, z) * ConstantValue;
            tileA.SetValue(x, z, val);
        }

        public void Execute<T>(int z, T tileA)
                where  T : struct, IRWTile {
            for( int x = 0; x < Resolution; x++){
                DoOp<T>(x, z, tileA);
            }
        }
    }

    public struct ConstantBinarize: IConstantTiles {
        public float ConstantValue {get; set;}
        public int JobLength {get; set;}
        public int Resolution {get; set;}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T>(int x, int z, T tileA)
                where T : struct, IRWTile {
            float val = tileA.GetData(x, z) >= ConstantValue ? 1 : 0;
            tileA.SetValue(x, z, val);
        }

        public void Execute<T>(int z, T tileA)
                where  T : struct, IRWTile {
            for( int x = 0; x < Resolution; x++){
                DoOp<T>(x, z, tileA);
            }
        }
    }

    public struct SubtractTiles: IReduceTiles{
        public int JobLength {get; set;}
        public int Resolution {get; set;}
        // tile A is left side, B is right
        // result put onto A
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T, V>(int x, int z, T tileA, V tileB)
                where T : struct, IRWTile
                where V : struct, IReadOnlyTile {
            float val = tileA.GetData(x, z) - tileB.GetData(x, z);
            tileA.SetValue(x, z, val);
        }

        public void Execute<T, V>(int z, T tileA, V tileB)
                where  T : struct, IRWTile
                where V: struct, IReadOnlyTile{
            for( int x = 0; x < Resolution; x++){
                DoOp<T, V>(x, z, tileA, tileB);
            }
        }
    }

    public struct MultiplyTiles: IReduceTiles{
        public int JobLength {get; set;}
        public int Resolution {get; set;}
        // tile A is left side, B is right
        // result put onto A
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T, V>(int x, int z, T tileA, V tileB)
                where T : struct, IRWTile
                where V : struct, IReadOnlyTile {
            float val = tileA.GetData(x, z) * tileB.GetData(x, z);
            tileA.SetValue(x, z, val);
        }

        public void Execute<T, V>(int z, T tileA, V tileB)
                where  T : struct, IRWTile
                where V: struct, IReadOnlyTile{
            for( int x = 0; x < Resolution; x++){
                DoOp<T, V>(x, z, tileA, tileB);
            }
        }
    }

    public struct MinTiles: IReduceTiles{
        public int JobLength {get; set;}
        public int Resolution {get; set;}
        // tile A is left side, B is right
        // result put onto A
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T, V>(int x, int z, T tileA, V tileB)
                where T : struct, IRWTile
                where V : struct, IReadOnlyTile {
            float val = min(tileA.GetData(x, z), tileB.GetData(x, z));
            tileA.SetValue(x, z, val);
        }

        public void Execute<T, V>(int z, T tileA, V tileB)
                where  T : struct, IRWTile
                where V: struct, IReadOnlyTile{
            for( int x = 0; x < Resolution; x++){
                DoOp<T, V>(x, z, tileA, tileB);
            }
        }
    }

    public struct MaxTiles: IReduceTiles{
        public int JobLength {get; set;}
        public int Resolution {get; set;}
        // tile A is left side, B is right
        // result put onto A
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T, V>(int x, int z, T tileA, V tileB)
                where T : struct, IRWTile
                where V : struct, IReadOnlyTile {
            float val = max(tileA.GetData(x, z), tileB.GetData(x, z));
            tileA.SetValue(x, z, val);
        }

        public void Execute<T, V>(int z, T tileA, V tileB)
                where  T : struct, IRWTile
                where V: struct, IReadOnlyTile{
            for( int x = 0; x < Resolution; x++){
                DoOp<T, V>(x, z, tileA, tileB);
            }
        }
    }

    public struct RootSumSquaresTiles: IReduceTiles{
        public int JobLength {get; set;}
        public int Resolution {get; set;}
        // tile A is left side, B is right
        // result put onto A
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoOp<T, V>(int x, int z, T tileA, V tileB)
                where T : struct, IRWTile
                where V : struct, IReadOnlyTile {
            float a = tileA.GetData(x, z);
            float b = tileB.GetData(x, z);
            float val = sqrt((a * a) + (b * b));
            tileA.SetValue(x, z, val);
        }

        public void Execute<T, V>(int z, T tileA, V tileB)
                where  T : struct, IRWTile
                where V: struct, IReadOnlyTile{
            for( int x = 0; x < Resolution; x++){
                DoOp<T, V>(x, z, tileA, tileB);
            }
        }
    }
}