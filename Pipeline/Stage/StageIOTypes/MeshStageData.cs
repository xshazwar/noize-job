using System;

using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.pipeline {

    [System.Serializable]
    public class MeshStageData : StageIO {
        // square output of the mesh (cut from the middle of the source data)
        public int resolution = 512;
        // square resolution of the source data
        public int inputResolution = 512;
        // In units of resolution pixels
        public int marginPix = 5;
        public float tileSize = 512f;
        public float tileHeight = 512f;
        public Mesh mesh;
    }
}