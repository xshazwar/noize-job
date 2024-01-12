using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

using Unity.Jobs;

using xshazwar.noize.mesh.Generators;
using xshazwar.noize.mesh.Streams;

namespace xshazwar.noize.mesh {
    public static class MeshHelper {

        public static Dictionary<int, Mesh> squareMeshCache = new Dictionary<int, Mesh>();

        static MeshJobScheduleDelegate meshJob = MeshJob<SharedSquareGridPosition, PositionStream32>.ScheduleParallel;
        static MeshJobScheduleDelegate hexMeshJob = MeshJob<FlatHexagonalGrid, PositionStream32>.ScheduleParallel;
    
        // private static Mesh makeSquarePlanarMesh(int resolution, float downscaleFactor = 1f){
            
        //     Mesh _mesh = new Mesh();
        //     int res = (int) resolution;
        //     if (res >= 256){
        //         // we MUST use 32 bit indices or things will be horribly wrong
        //         _mesh.indexFormat = IndexFormat.UInt32;
        //     }
            
        //     Vector3[] vertices = new Vector3[res * res];
        //     for (int i = 0, y = 0; y < res; y++) {
        //         for (int x = 0; x < res; x++, i++) {
        //             vertices[i] = new Vector3(x * downscaleFactor, 0, y * downscaleFactor);
        //         }
        //     }
        //     _mesh.vertices = vertices;
        //     int xSize = res -1;
        //     int ySize = res -1;
        //     int[] triangles = new int[xSize * ySize * 6];
        //     for (int ti = 0, vi = 0, y = 0; y < ySize; y++, vi++) {
        //         for (int x = 0; x < xSize; x++, ti += 6, vi++) {
        //             triangles[ti] = vi;
        //             triangles[ti + 3] = triangles[ti + 2] = vi + 1;
        //             triangles[ti + 4] = triangles[ti + 1] = vi + xSize + 1;
        //             triangles[ti + 5] = vi + xSize + 2;
        //         }
        //     }
        //     _mesh.triangles = triangles;
        //     return _mesh;
        // }

        public static Mesh makeSquarePlanarMesh(int resolution, int height, int size){
            UnityEngine.Profiling.Profiler.BeginSample("MakeMeshJob");
            Mesh currentMesh = new Mesh();
            Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
            Mesh.MeshData meshData= meshDataArray[0];
            // meshJob(currentMesh, meshData, resolution, default(JobHandle), size, height).Complete();
            hexMeshJob(currentMesh, meshData, resolution, default(JobHandle), size, height).Complete();
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, currentMesh,
                MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds
            );
            UnityEngine.Profiling.Profiler.EndSample();
            return currentMesh;
        }

        public static Mesh SquarePlanarMesh(int resolution, int height, int size){
            // we assume these are uniform per resolution in height and size
            if (!squareMeshCache.ContainsKey(resolution)){
                squareMeshCache[resolution] = makeSquarePlanarMesh(resolution, height, size);
            }
            return squareMeshCache[resolution];
        }
    }
}