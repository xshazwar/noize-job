using xshazwar.Meshes;
using xshazwar.Meshes.Generators;
using xshazwar.Meshes.Streams;
using UnityEngine;
using UnityEngine.Rendering;

using xshazwar.noize.cpu.mutate;

namespace xshazwar.noize.scripts {

	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class ProceduralMesh : MonoBehaviour{

		static MeshJobScheduleDelegate[] jobs = {
			MeshJob<SharedSquareGridPosition, PositionStream16>.ScheduleParallel
		};

		public enum MeshType {
			SharedSquareGridPosition
		};

		[SerializeField]
		MeshType meshType;

		[SerializeField, Range(1, 255)]
		int resolution = 128;
        private int tileSize => resolution;

		Mesh mesh;

		void Awake () {
			mesh = new Mesh {
				name = "Procedural Mesh"
			};
			GetComponent<MeshFilter>().mesh = mesh;
		}

		void OnValidate () => enabled = true;

		void Update () {
			UnityEngine.Profiling.Profiler.BeginSample("GenMesh");
			GenerateMesh();
			UnityEngine.Profiling.Profiler.EndSample();
			// enabled = false;
		}

		void GenerateMesh () {
			Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
			Mesh.MeshData meshData = meshDataArray[0];
			jobs[(int)meshType](mesh, meshData, resolution, default).Complete();
			Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
		}

        // public void GetData(out NativeSlice<float> d, out int res, out int ts){
        //     d = this.data;
        //     res = resolution;
        //     ts = tileSize;
        // }

        // public void UpdateImageChannel(){
        //     texture.Apply(false);
        // }

        // public void UpdateImageAllChannels(){
        //     red.CopyFrom(data);
        //     green.CopyFrom(data);
        //     texture.Apply(false);
        // }
	}
}