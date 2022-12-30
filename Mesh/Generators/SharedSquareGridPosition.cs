using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.noize.mesh.Generators {

	public struct SharedSquareGridPosition : IMeshGenerator {

		public Bounds Bounds {get; set;}
        public int VertexCount => (Resolution + 1) * (Resolution + 1);

		public int IndexCount => 6 * Resolution * Resolution;

		public int JobLength => Resolution + 1;

		public int Resolution { get; set; }

		public void Execute<S> (int z, S streams) where S : struct, IMeshStreams {
			int vi = (Resolution + 1) * z, ti = 2 * Resolution * (z - 1);

			var vertex = new Vertex();
			vertex.normal.z = -1f;
			vertex.tangent.xw = float2(1f, -1f);

			vertex.position.x = -0.5f;
			vertex.position.z = (float)z / Resolution - 0.5f;
			vertex.position.y = 0f;
			vertex.texCoord0.y = ((float) z) / ((float) Resolution + 1);

			streams.SetVertex(vi, vertex);
			vi += 1;

			for (int x = 1; x <= Resolution; x++, vi++, ti += 2) {
				vertex.position.x = (float)x / Resolution - 0.5f;
				vertex.texCoord0.x = ((float) x) / ((float) Resolution + 1);
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