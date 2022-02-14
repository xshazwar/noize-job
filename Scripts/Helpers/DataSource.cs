using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections;

using xshazwar.noize.cpu.mutate;

namespace xshazwar.noize.scripts {
    public class DataSourceSingleChannel<T> : IProvideTiles, IUpdateImageChannel where T: MonoBehaviour, IProvideTiles, IUpdateImageChannel {
        public T source;
        
        public void GetData(out NativeSlice<float> d, out int res, out int ts){
            source.GetData(out d, out res, out ts);
        }

        public void UpdateImageChannel(){
            source.UpdateImageChannel();
        }
    }

    public class DataSourceMultiChannel<T> : IProvideTiles, IUpdateAllChannels where T: MonoBehaviour, IProvideTiles, IUpdateAllChannels {
        public T source;
        
        public void GetData(out NativeSlice<float> d, out int res, out int ts){
            source.GetData(out d, out res, out ts);
        }

        public void UpdateImageAllChannels(){
            source.UpdateImageAllChannels();
        }
    }
}