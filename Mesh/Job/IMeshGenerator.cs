using System.Runtime.CompilerServices;

using UnityEngine;
using Unity.Collections;

using Unity.Mathematics;

using static Unity.Mathematics.math;
using System.Runtime.InteropServices;
using System;
using Unity.Burst;

namespace xshazwar.noize.mesh {

	public interface IMeshGenerator {

		Bounds Bounds { get; set;}

		int VertexCount { get; }

		int IndexCount { get; }

		int JobLength { get; }

		int Resolution { get; set; }

		void Execute<S> (int i, S streams) where S : struct, IMeshStreams;
	}

	public interface IMeshHeightGenerator {

		// WorldSpace Scaling
		float TileSize { get; set; }
		
		// How many WS units a single mesh unit takes
		
		float MeshUnitWS{
			get {
				return TileSize / Resolution; // (Meters / MeshUnit)
			}
		}
		float Height { get; set; }
		Bounds Bounds { get; }
		
		// Mesh Requirements
		int VertexCount { get; }

		int IndexCount { get; }

		int JobLength { get; }

		// Only for visuals
		float NormalStrength { get; set; }
		
		// Resolution of dataset (assumed to be a square array or DR * DR)
		int DataResolution {get; set;}
		float DataUnitWS {
			get {
				return TileSize / (DataResolution - 2 * DataOverdraw); //(Meters / DataUnit)
			}
		}

		float DataUnitMS {
			get {
				return Resolution / (DataResolution - 2 * DataOverdraw); // MeshUnit / DataUnit
			}
		}
		// Units of Data overdraw 
		int DataOverdraw { get; set; }
		// Size of data overdraw in WS units
		float DataOverdrawWS {
			get {
			return DataOverdraw  * DataUnitWS;
		}}	
		// Size of a meshunit in data space
		float MeshUnitDS{
			get {
				return DataResolution / Resolution; // DataUnit / MeshUnit
			}
		}
		// Units of Mesh, if multidimensional, assumed to be the same (R) in all dimensions
		int Resolution { get; set; }


		public virtual float2 dataspacePosition(float wsX, float wsZ){
			return float2((wsX + DataOverdrawWS) / DataUnitWS, (wsZ + DataOverdrawWS) / DataUnitWS);
		}

		public virtual void TestRange(){
			var start = dataspacePosition(0f, 0f);
			var end = dataspacePosition(TileSize, TileSize);
			Debug.Log($"Stated Data Range for TileSize {TileSize} and DR {DataResolution} {start.x}, {start.y} >> {end.x}, {end.y}, maxIdx {DataResolution * DataResolution} <=? {end.x + end.y * DataResolution - 1}");
		}

		// mesh space value in data space
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public virtual float Sample(float wsX, float wsZ, out float2 weight, out float4 neighbors, ref NativeSlice<float> data){
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

		void Execute<S> (int i, S streams, NativeSlice<float> height) where S : struct, IMeshStreams;
	}

}