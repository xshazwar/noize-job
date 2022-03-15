using UnityEngine;
using Unity.Collections;

namespace xshazwar.noize.mesh {

	public interface IMeshGenerator {

		Bounds Bounds { get; }

		int VertexCount { get; }

		int IndexCount { get; }

		int JobLength { get; }

		int Resolution { get; set; }

		void Execute<S> (int i, S streams) where S : struct, IMeshStreams;
	}

	public interface IMeshHeightGenerator {

		float TileSize { get; set; }
		float Height { get; set; }
		float NormalStrength { get; set; }
		
		Bounds Bounds { get; }

		int VertexCount { get; }

		int IndexCount { get; }

		int JobLength { get; }

		int InputResolution {get; set;}
		int Resolution { get; set; }
		int MarginPix { get; set; }

		void Execute<S> (int i, S streams, NativeSlice<float> height) where S : struct, IMeshStreams;
	}

}