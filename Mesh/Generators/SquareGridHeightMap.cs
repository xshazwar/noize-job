using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.Meshes.Generators {
    public struct SquareGridHeightMap : IMeshHeightGenerator {

		public float NormalStrength {get; set;}
		public float Height {get; set;}
		public float TileSize {get; set;}
		public int MarginPix {get; set;}
		public Bounds Bounds => new Bounds(
			new Vector3(0.5f * TileSize, 0f, 0.5f * TileSize),
			new Vector3(TileSize, Height, TileSize));

		//Need our heightmap (Native Slice)?
        // For Now we can use a sin generator
        public int VertexCount => (Resolution + 1) * (Resolution + 1);

		public int IndexCount => 6 * Resolution * Resolution;

		public int JobLength => Resolution + 1;

		public int Resolution { get; set; }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float InterpolateEdge(float a, float b){
			return a - (b - a);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float HeightScale(int x, int z){
			float h = Height;
			if (x < MarginPix || z < MarginPix){
				h *= .99f;
			}
			if (x > Resolution - MarginPix || z > Resolution - MarginPix){
				h *= .98f;
			}
			return h;
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
			v.position.y = t * HeightScale(x, z);
			float l = x > 0 ? heights[getIdx(x - 1, z)] : InterpolateEdge(t, heights[getIdx(x + 1, z)]);
			float r = x < Resolution - 1 ? heights[getIdx(x + 1, z)] : InterpolateEdge(t, heights[getIdx(x - 1, z)]);
			float u = z > 0 ? heights[getIdx(x, z - 1)] : InterpolateEdge(t, heights[getIdx(x, z + 1)]);
			float d = z < Resolution - 1 ? heights[getIdx(x, z + 1)] : InterpolateEdge(t, heights[getIdx(x, z - 1)]);
			float3 t1 = float3(4.0f, (r - l) /(2f), 0f);
			float3 t2 = float3(0, (u - d) /(2f), 4.0f);
			v.tangent.xyz = cross(t2, t1);
			v.normal = normalize(float3((l - r) / 2f * NormalStrength, 2f / Height, (u - d) / 2f * NormalStrength));
		}

		public void Execute<S> (int z, S streams, NativeSlice<float> heights) where S : struct, IMeshStreams {
			int vi = (Resolution + 1) * z, ti = 2 * Resolution * (z - 1);

			var vertex = new Vertex();
			vertex.position.x = - (0.5f * TileSize / Resolution);
			vertex.position.z = (float)z * TileSize / Resolution - (0.5f * TileSize / Resolution);
			SetVertexValues(ref vertex, 0, z, heights);
			streams.SetVertex(vi, vertex);
			vi += 1;

			for (int x = 1; x <= Resolution; x++, vi++, ti += 2) {
				vertex.position.x = (float)x * TileSize / Resolution - 0.5f;
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