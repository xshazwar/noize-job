using UnityEngine;
using Unity.Collections;

namespace xshazwar.Meshes {

	public interface IMeshGenerator {

		Bounds Bounds { get; }

		int VertexCount { get; }

		int IndexCount { get; }

		int JobLength { get; }

		int Resolution { get; set; }

		void Execute<S> (int i, S streams) where S : struct, IMeshStreams;
	}

	public interface IMeshHeightGenerator {

		Bounds Bounds { get; }

		int VertexCount { get; }

		int IndexCount { get; }

		int JobLength { get; }

		int Resolution { get; set; }

		void Execute<S> (int i, S streams, NativeSlice<float> height) where S : struct, IMeshStreams;
	}

}