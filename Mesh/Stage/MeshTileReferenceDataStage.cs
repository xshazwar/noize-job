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

    [CreateAssetMenu(fileName = "MeshTileReferenceDataStage", menuName = "Noize/Output/MeshTileReferenceData", order = 2)]
    public class MeshTileReferenceDataStage: PipelineStage {
        // TODO swap between jobs depending on mesh resolution to save memory
		static HeightMapMeshJobScheduleDelegate[] jobs = {
			HeightMapMeshJob<SquareGridHeightMap, PositionStream32>.ScheduleParallel,
            HeightMapMeshJob<OvershootSquareGridHeightMap, PositionStream32>.ScheduleParallel
		};

        private Mesh currentMesh;
        private Mesh.MeshDataArray meshDataArray;
		private Mesh.MeshData meshData;
        public MeshType meshType = MeshType.SquareGridHeightMap;
        public string contextAlias;

        private string getBufferName(MeshStageData d){
            return $"{d.xpos}_{d.zpos}__{d.inputResolution}__{contextAlias}";
        }

        public override bool IsSchedulable(PipelineWorkItem job){
            if(job.stageManager == null){
                return false;
            }
            MeshStageData gd = (MeshStageData) job.data;
            string bufferName = getBufferName(gd);
            if(!job.stageManager.BufferExists<NativeArray<float>>(bufferName)){
                return false;
            }
            bool locked = job.stageManager.IsLocked<NativeArray<float>>(bufferName);
            return !locked;

        }

        public override void Schedule(PipelineWorkItem requirements, JobHandle dependency){
            MeshStageData d = (MeshStageData) requirements.data;
            currentMesh = d.mesh;
            meshDataArray = Mesh.AllocateWritableMeshData(1);
			meshData = meshDataArray[0];
            int res = d.inputResolution * d.inputResolution;
            NativeArray<float> buffer = requirements.stageManager.GetBuffer<float, NativeArray<float>>(getBufferName(d), res);
            NativeSlice<float> contextTarget = new NativeSlice<float>(buffer);
			jobHandle = jobs[(int)meshType](currentMesh, meshData, d.resolution, d.inputResolution, d.marginPix, d.tileHeight, d.tileSize, contextTarget, dependency);
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