using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.Meshes.Generators {
    public struct SquareGridHeightMap : IMeshHeightGenerator {

		public Bounds Bounds => new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));

		//Need our heightmap (Native Slice)?
        // For Now we can use a sin generator
        public int VertexCount => (Resolution + 1) * (Resolution + 1);

		public int IndexCount => 6 * Resolution * Resolution;

		public int JobLength => Resolution + 1;

		public int Resolution { get; set; }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // overflows safely
            x = clamp(x, 0, Resolution - 1);
            z = clamp(z, 0, Resolution - 1);
            return (z * Resolution) + x;   
        }

		public void Execute<S> (int z, S streams, NativeSlice<float> heights) where S : struct, IMeshStreams {
			int vi = (Resolution + 1) * z, ti = 2 * Resolution * (z - 1);

			var vertex = new Vertex();
			vertex.position.x = -0.5f;
			vertex.position.z = (float)z / Resolution - 0.5f;
			streams.SetVertex(vi, vertex);
			vi += 1;

			for (int x = 1; x <= Resolution; x++, vi++, ti += 2) {
				vertex.position.x = (float)x / Resolution - 0.5f;
				vertex.position.y = heights[getIdx(x, z)];
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