using xshazwar.Meshes;
using xshazwar.Meshes.Generators;
using xshazwar.Meshes.Streams;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine;
using UnityEngine.Rendering;

using xshazwar.unity;
#if UNITY_EDITOR
using xshazwar.unity.editor;
#endif
using xshazwar.noize.cpu.mutate;

namespace xshazwar.noize.scripts {

	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class ProceduralMesh : MonoBehaviour, IHeightTarget {

		// TODO swap between jobs depending on mesh resolution to save memory
		static HeightMapMeshJobScheduleDelegate[] jobs = {
			HeightMapMeshJob<SquareGridHeightMap, PositionStream32>.ScheduleParallel
		};

		public enum MeshType {
			SquareGridHeightMap,
			SharedSquareGridPosition
		};
		
		[SerializeField]
        [RequireInterface(typeof(IHeightBroadcaster))]
        public UnityEngine.Object dataSource;
		private IHeightBroadcaster _dataSource;

		MeshType meshType = MeshType.SquareGridHeightMap;
		private int resolution = 128;
        private int tileSize => resolution;
		private NativeSlice<float> data;
		Mesh mesh;

		private bool generateEnabled;
		private bool triggered;
		private JobHandle jobHandle; 
		Mesh.MeshDataArray meshDataArray;
		Mesh.MeshData meshData;

		void Start(){
			generateEnabled = false;
			triggered = false;
			if (dataSource != null){
				_dataSource = (IHeightBroadcaster) dataSource;
				_dataSource.OnHeightReady += SetHeightValues;
			}
		}
		
		void Awake () {
			mesh = new Mesh {
				name = "Procedural Mesh"
			};
			GetComponent<MeshFilter>().mesh = mesh;
		}

		void Update () {
			if (triggered){
                if (!jobHandle.IsCompleted){
                    return;
                }
                jobHandle.Complete();
                UnityEngine.Profiling.Profiler.BeginSample("Apply Mesh");
                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);
                UnityEngine.Profiling.Profiler.EndSample();
                triggered = false;
            }
            
            if (generateEnabled && !triggered){
                UnityEngine.Profiling.Profiler.BeginSample("Start Mesh Job");
                triggered = true;
                GenerateMesh();
                UnityEngine.Profiling.Profiler.EndSample();
                generateEnabled = false;
            }	
		}

		void GenerateMesh () {
			meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
			jobHandle = jobs[(int)meshType](mesh, meshData, resolution, data, default);
		}

		public void SetHeightValues(int resolution, NativeSlice<float> data){
			if (!triggered){
				this.resolution = resolution;
				this.data = data;
				generateEnabled = true;
			}
		}
	}
}