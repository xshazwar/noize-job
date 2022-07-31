using System;

using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.pipeline {

    [System.Serializable]
    public class DownsampleData : StageIO {
        // square output of the mesh (cut from the middle of the source data)
        public int resolution = 512;
        // square resolution of the source data
        public int inputResolution = 512;
        // In units of resolution pixels
        public NativeSlice<float> inputData;
    }
}