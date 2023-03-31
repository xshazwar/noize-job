using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.noize.mesh.Generators {
    public struct SquareGridHeightMap : IMeshHeightGenerator {

		public float NormalStrength {get; set;}
		public float Height {get; set;}
		public float TileSize {get; set;}
		public int MarginPix {get; set;}

		private float MarginPixF => (float) MarginPix;
		public Bounds Bounds => new Bounds(
			new Vector3(0.5f * TileSize, 0.5f * Height, 0.5f * TileSize),
			new Vector3(TileSize, Height, TileSize));

        public int VertexCount => (Resolution + 1) * (Resolution + 1);

		public int IndexCount => 6 * Resolution * Resolution;

		public int JobLength => Resolution + 1;

		public int Resolution { get; set; }
		public int InputResolution { get; set; }

		private int PixOffset => (int) ((InputResolution - Resolution) / 2) ;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float InterpolateEdge(float a, float b){
			return a - (b - a);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private float MarginScale(int x, int z){
			float h = 0f;
			if (x < MarginPix){
				h += (.0191f * Height * ((MarginPix - x ) / MarginPixF));
			}
			if (z < MarginPix){
				h += (.0192f * Height * ((MarginPix - z ) / MarginPixF));
			}
			if (x > Resolution - MarginPix){
				h +=  (.0193f * Height * (((MarginPix - (Resolution - x )) / (MarginPixF))));
			}
			if (z > Resolution - MarginPix){
				h +=  (.0194f * Height * (((MarginPix - (Resolution - z )) / (MarginPixF))));
			}
			return h;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, Resolution + 1);
            z = clamp(z, 0, Resolution + 1);
            return ((z + PixOffset) * InputResolution) + x + PixOffset;   
        }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SetVertexValues(ref Vertex v, int x, int z, NativeSlice<float> heights){
			float t = heights[getIdx(x, z)];
			v.position.y = (t * Height);
			float l = x > 0 ? heights[getIdx(x - 1, z)] : InterpolateEdge(t, heights[getIdx(x + 1, z)]);
			float r = x < Resolution - 1 ? heights[getIdx(x + 1, z)] : InterpolateEdge(t, heights[getIdx(x - 1, z)]);
			float u = z > 0 ? heights[getIdx(x, z - 1)] : InterpolateEdge(heights[getIdx(x, z + 1)], t);
			float d = z < Resolution - 1 ? heights[getIdx(x, z + 1)] : InterpolateEdge(heights[getIdx(x, z - 1)], t);
			float3 t1 = float3(4.0f, (r - l) /(2f), 0f);
			float3 t2 = float3(0, (u - d) /(2f), 4.0f);
			v.tangent.xyz = cross(t2, t1);
			v.normal = normalize(float3((l - r) / 2f * NormalStrength, 2f / Height, (u - d) / 2f * NormalStrength));
			v.texCoord0.x = ((float) x) / ((float) Resolution + 1);
			v.texCoord0.y = ((float) z) / ((float) Resolution + 1);
		}

		public void Execute<S> (int z, S streams, NativeSlice<float> heights) where S : struct, IMeshStreams {
			int vi = (Resolution + 1) * z, ti = 2 * Resolution * (z - 1);
			var vertex = new Vertex();
			vertex.position.x = - (0.5f * TileSize / Resolution);
			vertex.position.z = (float)z * TileSize / Resolution - 0.5f; //(0.5f * TileSize / Resolution);
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