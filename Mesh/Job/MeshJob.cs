using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace xshazwar.noize.mesh {

	[BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = true)]
	public struct MeshJob<G, S> : IJobFor
		where G : struct, IMeshGenerator
		where S : struct, IMeshStreams {

		G generator;

		[WriteOnly]
		S streams;

		public void Execute (int i) => generator.Execute(i, streams);

		public static JobHandle ScheduleParallel (
			Mesh mesh, Mesh.MeshData meshData, int resolution, JobHandle dependency
		) {
			var job = new MeshJob<G, S>();
			job.generator.Resolution = resolution;
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

		public static JobHandle ScheduleParallel (
			Mesh mesh, Mesh.MeshData meshData, int resolution, JobHandle dependency = default(JobHandle), int TileSize = 1, int Height = 1 
		){
			Bounds bounds = new Bounds(
				new Vector3(0.5f * TileSize, 0.5f * Height, 0.5f * TileSize),
				new Vector3(TileSize, Height, TileSize));
			var job = new MeshJob<G, S>();
			job.generator.Resolution = resolution;
			job.generator.Bounds = bounds;
			job.streams.Setup(
				meshData,
				mesh.bounds = bounds,
				job.generator.VertexCount,
				job.generator.IndexCount
			);
			return job.ScheduleParallel(
				job.generator.JobLength, 1, dependency
			);
		}
	}

	public delegate JobHandle MeshJobScheduleDelegate (
		Mesh mesh, Mesh.MeshData meshData, int resolution, JobHandle dependency, int TileSize, int Height
	);
}