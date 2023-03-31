using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

// using xshazwar.unity;
// #if UNITY_EDITOR
// using xshazwar.unity.editor;
// #endif

using xshazwar.noize.mesh;
using xshazwar.noize.mesh.Generators;
using xshazwar.noize.mesh.Streams;

using xshazwar.noize.pipeline;

namespace xshazwar.noize.mesh {

    public enum MeshType {
        SquareGridHeightMap,
        OvershootSquareGridHeightMap
    };

    [CreateAssetMenu(fileName = "MeshTileStage", menuName = "Noize/Output/MeshTile", order = 2)]
    public class MeshTileStage: PipelineStage {
        // TODO swap between jobs depending on mesh resolution to save memory
		static HeightMapMeshJobScheduleDelegate[] jobs = {
			HeightMapMeshJob<SquareGridHeightMap, PositionStream32>.ScheduleParallel,
            HeightMapMeshJob<OvershootSquareGridHeightMap, PositionStream32>.ScheduleParallel
		};

        private Mesh currentMesh;
        private Mesh.MeshDataArray meshDataArray;
		private Mesh.MeshData meshData;
        public MeshType meshType = MeshType.SquareGridHeightMap;

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            MeshStageData d = (MeshStageData) requirements.data;
            currentMesh = d.mesh;
            meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
			jobHandle = jobs[(int)meshType](currentMesh, meshData, d.resolution, d.inputResolution, d.marginPix, d.tileHeight, d.tileSize, d.data, dependency);
        }

        public override void OnStageComplete(){
            
            UnityEngine.Profiling.Profiler.BeginSample("ApplyMeshAndDisposeMemory");
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, currentMesh,
                MeshUpdateFlags.DontNotifyMeshUsers |
                MeshUpdateFlags.DontValidateIndices |
                MeshUpdateFlags.DontRecalculateBounds);
            UnityEngine.Profiling.Profiler.EndSample();
            
        }

        public override void OnDestroy(){}

    }
}