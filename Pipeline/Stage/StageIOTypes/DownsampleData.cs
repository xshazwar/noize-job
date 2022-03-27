using System;

using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.pipeline {

    [System.Serializable]
    public class DownsampleData : StageIO {

        [Range(8, 4096)]
        // square output of the mesh (cut from the middle of the source data)
        public int resolution = 512;
        // square resolution of the source data
        public int inputResolution = 512;
        // In units of resolution pixels
        public NativeSlice<float> inputData;
        public NativeSlice<float> data;

        public override void ImposeOn(ref StageIO d){
            DownsampleData data = (DownsampleData) d;
            data.resolution = resolution;
        }
    }
}