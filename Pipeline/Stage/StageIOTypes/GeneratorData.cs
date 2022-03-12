using System;

using Unity.Collections;
using UnityEngine;

namespace xshazwar.noize.pipeline {

    [System.Serializable]
    public class GeneratorData : StageIO {

        [Range(8, 4096)]
        public int resolution = 512;
        public int xpos = 0;
        public int zpos = 0;
        public NativeSlice<float> data;
        public override void ImposeOn(ref StageIO d){
            GeneratorData data = (GeneratorData) d;
            data.resolution = resolution;
            data.xpos = xpos;
            data.zpos = zpos;
        }
    }
}