using System;

namespace xshazwar.noize.pipeline {
    
    // Mark subclasses w/ [System.Serializable]
    public abstract class StageIO {
        public abstract void ImposeOn(ref StageIO other);
    }
    
}