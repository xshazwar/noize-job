using Unity.Mathematics;

namespace xshazwar.noize.mesh {

	public struct Vertex {
        // this could just be a float, but I don't think it matters much and I like the 
        // how explict the original implementation is.
		public float3 position;
		public float3 normal;
		public float4 tangent;
	}
}