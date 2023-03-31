using System;

using Unity.Collections;

namespace xshazwar.noize.pipeline {
    
    // Mark subclasses w/ [System.Serializable]
    public abstract class StageIO {
        public string uuid;
        public NativeSlice<float> data;
    }
}