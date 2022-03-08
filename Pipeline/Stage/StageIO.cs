using System;

namespace xshazwar.noize.pipeline {
    
    // Mark subclasses w/ [System.Serializable]
    public abstract class StageIO {
        public string uuid;
        public abstract void ImposeOn(ref StageIO other);
    }
}