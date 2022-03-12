using System;

using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.pipeline {

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
}