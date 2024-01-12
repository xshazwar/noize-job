using System.Runtime.CompilerServices;

using Unity.Collections;

using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace xshazwar.noize.mesh {

	public struct FlatHexagonalGridHeightMap : IMeshHeightGenerator {

		
		public static readonly float SQRT3 = 1.73205080757f;
		public float NormalStrength {get; set;}
		public float Height {get; set;}
		public float TileSize {get; set;}
		public int DataOverdraw {get; set;}
        private float DataOverdrawF => (float) DataOverdraw;

        public Bounds Bounds => new Bounds(
			new Vector3(0.5f * TileSize, 0.5f * Height, 0.5f * TileSize),
			new Vector3(TileSize, Height, TileSize));

		public int VertexCount => 7 * Resolution * Resolution;

		public int IndexCount => 18 * Resolution * Resolution;

		public int JobLength => Resolution;
		public int DataResolution { get; set; }
        private int PixOffset => (int) ((DataResolution - Resolution) / 2) ;
		public int Resolution { get; set; }

		private int2 evenq_to_axial(int x, int z){
			int q = x;
			int r = z - (x + (x&1)) / 2;
			return int2(q, r);
		}

		private int2 pix_to_axial(float x, float y){
			// Convert to their coordinate system
			x *= 1f/SQRT3;
			y *= -1f/SQRT3;
			// Algorithm from Charles Chambers
			// with modifications and comments by Chris Cox 2023
			// <https://gitlab.com/chriscox/hex-coordinates>
			float t = SQRT3 * y + 1;           // scaled y, plus phase
			float temp1 = floor( t + x );      // (y+x) diagonal, this calc needs floor
			float temp2 = ( t - x );           // (y-x) diagonal, no floor needed
			float temp3 = ( 2 * x + 1 );       // scaled horizontal, no floor needed, needs +1 to get correct phase
			float qf = (temp1 + temp3) / 3f;   // pseudo x with fraction
			float rf = (temp1 + temp2) / 3f;   // pseudo y with fraction
			float q = floor(qf);               // pseudo x, quantized and thus requires floor
			float r = floor(rf);               // pseudo y, quantized and thus requires floor
			return int2((int)q, (int) -r);
		}

		private float sample(float x, float z, NativeSlice<float> heights){
			int xf = (int) round(x);
			int zf = (int) round(z);
			float2 xz = lerp(
				float2(heights[getIdx(xf, zf)],heights[getIdx(xf, zf + 1)]), //xF
				float2(heights[getIdx(xf+1, zf)],heights[getIdx(xf+1, zf + 1)]), // xC
				float2( x - (float) xf, z - (float) z)
			);
			return csum(xz) / 2f;
		}

		private float2 flat_hex_to_pixel(int2 axial_hex, float size){
			float x = size * (3f/2f * axial_hex.x);
			float y = size * (SQRT3/2f * axial_hex.x  +  SQRT3 * axial_hex.y);
			return float2(x, y);
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int getIdx(int x, int z){
            // assumes overflow in safe zone
            x = clamp(x, 0 - PixOffset, Resolution + PixOffset);
            z = clamp(z, 0 - PixOffset, Resolution + PixOffset);
            return ((z + PixOffset) * DataResolution) + x + PixOffset;   
        }

        private float4 getNeighbors(int x, int z, NativeSlice<float> heights){
            var flip = (x & 1) == 0 ? -1 : 1;
			return new float4(
                heights[getIdx(x - 1, z)], // l
                heights[getIdx(x + 1, z)], // r
                lerp(heights[getIdx(x, z - 1)], heights[getIdx(x + flip, z - 1)], 0.5f), // u
				lerp(heights[getIdx(x, z + 1)], heights[getIdx(x + flip, z + 1)], 0.5f) // d
                // heights[getIdx(x, z + 1)]  // d
            );
        }


		// [MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void HexVertexValues(
            ref Vertex v,
            float t, // height
            float4 n // neighbors
            ){
            // weights (x, y lerp distance from central height t)
            // weights are same texCoord0 but centered on 0,0 (-0.5 -> 0.5)
            float2 w = new float2(-0.5f, -0.5f) + (v.texCoord0);  
            float l = n.x;
            float r = n.y;
            float u = n.z;
            float d = n.w;
            float2 nh = new float2(
                (w.x < .01f ? l : r),
                (w.y < .01f ? u : d)
            );
			float xh = lerp(t, nh.x, abs(w.x));
			float zh = lerp(t, nh.y, abs(w.y));
            
			v.position.y = (xh + zh) * Height / 2f;
			// v.position.y = 0f;
            // TODO rectify to relative position within hex
			float3 t1 = float3(4.0f, (r - l) /2f, 0f);
			float3 t2 = float3(0, (u - d) /2f, 4.0f);
			v.tangent.xyz = cross(t2, t1);
			v.normal = normalize(float3((l - r) / 2f * NormalStrength, 2f / Height, (u - d) / 2f * NormalStrength));
			// v.texCoord0.x = ((float) x) / (((float) Resolution) - 0.5f);
			// v.texCoord0.y = ((float) z) / (((float) Resolution) - 0.5f);
		}


		public void Execute<S> (int x, S streams, NativeSlice<float> heights) where S : struct, IMeshStreams {
			int vi = 7 * Resolution * x, ti = 6 * Resolution * x;

			float h = sqrt(3f) / 4f;

			float2 centerOffset = 0f;
			var stepSize = TileSize / Resolution;
			if (Resolution > 1) {
				centerOffset.x = -0.375f * stepSize;
				centerOffset.y = (((x & 1) == 0 ? 0.25f : 0f)) * h * stepSize;
			}

			for (int z = 0; z < Resolution; z++, vi += 7, ti += 6) {
				
				var center = (float2(0.75f * x, 2f * h * z) + centerOffset) * stepSize;
				var xCoordinates =
					center.x + float4(-0.5f, -0.25f, 0.25f, 0.5f) * stepSize;
				var zCoordinates = center.y + float2(h, -h) * stepSize;

                float hCenter = heights[getIdx(x, z)];
                float4 neighbors = getNeighbors(x, z, heights);
                
                // TODO texCoord0 needs to be stretched across the entire TileSize, not every hex
				var vertex = new Vertex();
				vertex.normal.y = 1f;
				vertex.tangent.xw = float2(1f, -1f);

				
                vertex.position.xz = center;
				vertex.texCoord0 = float2(0.5f, 0.5f);
                HexVertexValues(ref vertex, hCenter, neighbors);
				streams.SetVertex(vi + 0, vertex);

				vertex.position.x = xCoordinates.x;
				vertex.texCoord0.x = 0f;
                HexVertexValues(ref vertex, hCenter, neighbors);
				streams.SetVertex(vi + 1, vertex);
				
				vertex.position.x = xCoordinates.y;
				vertex.position.z = zCoordinates.x;
				vertex.texCoord0 = float2(0.25f, 0.5f + h);
				HexVertexValues(ref vertex, hCenter, neighbors);
                streams.SetVertex(vi + 2, vertex);

				vertex.position.x = xCoordinates.z;
				vertex.texCoord0.x = 0.75f;
                HexVertexValues(ref vertex, hCenter, neighbors);
				streams.SetVertex(vi + 3, vertex);

				vertex.position.x = xCoordinates.w;
				vertex.position.z = center.y;
				vertex.texCoord0 = float2(1f, 0.5f);
                HexVertexValues(ref vertex, hCenter, neighbors);
				streams.SetVertex(vi + 4, vertex);

				vertex.position.x = xCoordinates.z;
				vertex.position.z = zCoordinates.y;
				vertex.texCoord0 = float2(0.75f, 0.5f - h);
                HexVertexValues(ref vertex, hCenter, neighbors);
				streams.SetVertex(vi + 5, vertex);

				vertex.position.x = xCoordinates.y;
				vertex.texCoord0.x = 0.25f;
                HexVertexValues(ref vertex, hCenter, neighbors);
				streams.SetVertex(vi + 6, vertex);

				streams.SetTriangle(ti + 0, vi + int3(0, 1, 2));
				streams.SetTriangle(ti + 1, vi + int3(0, 2, 3));
				streams.SetTriangle(ti + 2, vi + int3(0, 3, 4));
				streams.SetTriangle(ti + 3, vi + int3(0, 4, 5));
				streams.SetTriangle(ti + 4, vi + int3(0, 5, 6));
				streams.SetTriangle(ti + 5, vi + int3(0, 6, 1));
			}
		}

	}
}