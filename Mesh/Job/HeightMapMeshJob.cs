using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace xshazwar.Meshes {

	[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
	public struct HeightMapMeshJob<G, S> : IJobFor
		where G : struct, IMeshHeightGenerator
		where S : struct, IMeshStreams {

		G generator;

        [NativeDisableParallelForRestriction]
		[ReadOnly]
        NativeSlice<float> heights;

		[WriteOnly]
		S streams;

		public void Execute (int i) => generator.Execute(i, streams, heights);

		public static JobHandle ScheduleParallel (
			Mesh mesh, Mesh.MeshData meshData, int resolution, NativeSlice<float> heights, JobHandle dependency
		) {
			var job = new HeightMapMeshJob<G, S>();
			job.generator.Resolution = resolution;
            job.heights = heights;
			job.streams.Setup(
				meshData,
				mesh.bounds = job.generator.Bounds,
				job.generator.VertexCount,
				job.generator.IndexCount
			);
			return job.ScheduleParallel(
				job.generator.JobLength, 1, dependency
			);
		}
	}

	public delegate JobHandle HeightMapMeshJobScheduleDelegate (
		Mesh mesh, Mesh.MeshData meshData, int resolution, NativeSlice<float> heights, JobHandle dependency
	);
}