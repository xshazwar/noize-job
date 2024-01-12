using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.noize.mesh.Generators {
    public struct OvershootSquareGridHeightMap : IMeshHeightGenerator {

		public float NormalStrength {get; set;}
		public float Height {get; set;}
		public float TileSize {get; set;}
		public int DataOverdraw {get; set;}
		float DataOverdrawWS {
			get {
			return DataOverdraw  * DataUnitWS;
		}}	

		float DataUnitWS {
			get {
				return TileSize / (DataResolution - 2 * DataOverdraw); //(Meters / DataUnit)
			}
		}

		readonly private float DataOverdrawF => (float) DataOverdraw;
		public Bounds Bounds => new Bounds(
			new Vector3(0.5f * TileSize, 0.5f * Height, 0.5f * TileSize),
			new Vector3(TileSize, Height, TileSize));

        public int VertexCount => (Resolution + 1) * (Resolution + 1);

		public int IndexCount => 6 * Resolution * Resolution;

		public int JobLength => Resolution + 1;

		public int Resolution { get; set; }
		public int DataResolution { get; set; }

		private int PixOffset => (int) ((DataResolution - Resolution) / 2) ;



		// mesh space value in data space
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float Sample(float wsX, float wsZ, out float2 weight, out float4 neighbors, ref NativeSlice<float> data){
			// l, r, u, d
			// offset the dsPos from the dataplane with DataOverdrawWS, then convert from WS into Dataspace;
			float2 dsPos = float2((wsX + DataOverdrawWS) / DataUnitWS, (wsZ + DataOverdrawWS) / DataUnitWS);
			weight = float2(dsPos.x - floor(dsPos.x), dsPos.y - floor(dsPos.y)); // (xWeight, yWeight)
			int4 idx = (int4) float4( // nw, sw, se, ne
				floor(dsPos.x) + (floor(dsPos.y) * DataResolution),
				floor(dsPos.x) + (ceil(dsPos.y) * DataResolution),
				ceil(dsPos.x) + (floor(dsPos.y) * DataResolution),
				ceil(dsPos.x) + (ceil(dsPos.y) * DataResolution)
			);
			float4 values = float4(
				data[idx.x],
				data[idx.y],
				data[idx.z],
				data[idx.w]
			);
			neighbors = float4(
				lerp(values.y, values.x, weight.y), // l
				lerp(values.z, values.w, weight.y), // r
				lerp(values.x, values.w, weight.x), // u
				lerp(values.y, values.z, weight.x)  // d
			);
			return (lerp(neighbors.x, neighbors.y, weight.x) + lerp(neighbors.w, neighbors.z, weight.y)) / 2f;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SetVertexValues(ref Vertex v, int x, int z, NativeSlice<float> heights){
			float4 n;
			float2 weight;
			float t = Sample(
				v.position.x , v.position.z , out weight, out n, ref heights);
			v.position.y = (t * Height);
			  // deduce terrain normal
			float3 normal = normalize(float3(n.x - n.y, 4f / Height, n.w - n.z));
			float3 m1  = cross(normal, float3(1,0,0));
			float3 m2 = cross(normal, float3(0,1,0));
			v.tangent.xyz = length(m1) > length(m2) ? m1 : m2;
			v.normal = normal;
			v.texCoord0.x = (v.position.x / TileSize);
			v.texCoord0.y = (v.position.z / TileSize);
		}

		public void Execute<S> (int z, S streams, NativeSlice<float> heights) where S : struct, IMeshStreams {
			int vi = (Resolution + 1) * z, ti = 2 * Resolution * (z - 1);

			var vertex = new Vertex();
			vertex.position.x = 0f;
			vertex.position.z = z * TileSize / Resolution;
			// vertex.position.x = - (0.5f * TileSize / Resolution);
			// vertex.position.z = z * TileSize / Resolution - 0.5f;

			SetVertexValues(ref vertex, 0, z, heights);
			streams.SetVertex(vi, vertex);
			vi += 1;

			for (int x = 1; x <= Resolution; x++, vi++, ti += 2) {
				vertex.position.x = x * TileSize / Resolution;
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