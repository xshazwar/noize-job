using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.Meshes.Generators {
    public struct SquareGridHeightMap : IMeshHeightGenerator {

		private const float EPS = 1f;
		private const float _Height = 1000f;
		private const float _Scale = 1000f;
		public Bounds Bounds => new Bounds(Vector3.zero, new Vector3(_Scale, _Scale, _Height));

		//Need our heightmap (Native Slice)?
        // For Now we can use a sin generator
        public int VertexCount => (Resolution + 1) * (Resolution + 1);

		public int IndexCount => 6 * Resolution * Resolution;

		public int JobLength => Resolution + 1;

		public int Resolution { get; set; }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		float InterpolateEdge(float a, float b){
			return a - (b - a);
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, Resolution - 1);
            z = clamp(z, 0, Resolution - 1);
            return (z * Resolution) + x;   
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SetVertexValues(ref Vertex v, int x, int z, NativeSlice<float> heights){
			float t = heights[getIdx(x, z)];
			v.position.y = t * _Height;
			float l = x > 0 ? heights[getIdx(x - 1, z)] : InterpolateEdge(t, heights[getIdx(x + 1, z)]);
			float r = x < Resolution - 1 ? heights[getIdx(x + 1, z)] : InterpolateEdge(t, heights[getIdx(x - 1, z)]);
			float u = z > 0 ? heights[getIdx(x, z - 1)] : InterpolateEdge(t, heights[getIdx(x, z + 1)]);
			float d = z < Resolution - 1 ? heights[getIdx(x, z + 1)] : InterpolateEdge(t, heights[getIdx(x, z - 1)]);
			float3 t1 = new float3(4.0f, (r - l) /(2f), 0f);
			float3 t2 = new float3(0, (u - d) /(2f), 4.0f);
			v.tangent.xyz = cross(t2, t1);
			v.normal = normalize(float3((l - r) / 2f * EPS, 2f / _Height, (u - d) / 2f * EPS));
		}

		public void Execute<S> (int z, S streams, NativeSlice<float> heights) where S : struct, IMeshStreams {
			int vi = (Resolution + 1) * z, ti = 2 * Resolution * (z - 1);

			var vertex = new Vertex();
			vertex.normal.z = -1f;
			vertex.tangent.xw = float2(1f, -1f);
			vertex.position.x = -0.5f;
			vertex.position.z = (float)z * _Scale / Resolution - 0.5f;
			streams.SetVertex(vi, vertex);
			vi += 1;

			for (int x = 1; x <= Resolution; x++, vi++, ti += 2) {
				vertex.position.x = (float)x * _Scale / Resolution - 0.5f;
				SetVertexValues(ref vertex, x, z, heights);
				streams.SetVertex(vi, vertex);

				if (z > 0) {
					streams.SetTriangle(
						ti + 0, vi + int3(-Resolution - 2, -1, -Resolution - 1)
					);
					streams.SetTriangle(
						ti + 1, vi + int3(-Resolution - 1, -1, 0)
					);
				}
			}
		}
	}
}