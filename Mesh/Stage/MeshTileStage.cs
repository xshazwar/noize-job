using System;
using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using UnityEngine.Rendering;

// using xshazwar.unity;
// #if UNITY_EDITOR
// using xshazwar.unity.editor;
// #endif

using xshazwar.Meshes;
using xshazwar.Meshes.Generators;
using xshazwar.Meshes.Streams;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.mesh {

    [System.Serializable]
    public class MeshStageData : StageIO {

        [Range(8, 4096)]
        public int resolution = 512;
        // In units of resolution pixels
        public int marginPix = 5;
        public float tileSize = 512f;
        public float tileHeight = 512f;
        public Mesh mesh;
        public NativeSlice<float> data;

        public override void ImposeOn(ref StageIO d){
            MeshStageData data = (MeshStageData) d;
            data.resolution = resolution;
        }
    }

    public enum MeshType {
        SquareGridHeightMap,
        SharedSquareGridPosition
    };

    [CreateAssetMenu(fileName = "MeshTileStage", menuName = "Noize/Output/MeshTile", order = 2)]
    public class MeshTileStage: PipelineStage {
        // TODO swap between jobs depending on mesh resolution to save memory
		static HeightMapMeshJobScheduleDelegate[] jobs = {
			HeightMapMeshJob<SquareGridHeightMap, PositionStream32>.ScheduleParallel
		};

        private Mesh currentMesh;
        private Mesh.MeshDataArray meshDataArray;
		private Mesh.MeshData meshData;
        MeshType meshType = MeshType.SquareGridHeightMap;

        void OnValidate(){}

        void Awake(){
        }

        public override void Schedule( StageIO req ){
            MeshStageData d = (MeshStageData) req;
            currentMesh = d.mesh;
            meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
			jobHandle = jobs[(int)meshType](currentMesh, meshData, d.resolution, d.marginPix, d.tileHeight, d.tileSize, d.data, default);
        }
        public override void OnStageComplete(){
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, currentMesh);
        }
    }
}