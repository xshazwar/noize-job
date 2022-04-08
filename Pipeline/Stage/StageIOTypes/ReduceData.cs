using System;

using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.pipeline {

    [System.Serializable]
    public class ReduceData : StageIO {

        [Range(8, 4096)]
        public int resolution = 512;
        public NativeSlice<float> data;
        public NativeSlice<float> rightData;

        public override void ImposeOn(ref StageIO d){
            ReduceData data = (ReduceData) d;
            data.resolution = resolution;
        }
    }
}