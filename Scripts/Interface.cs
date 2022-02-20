using System;
using Unity.Collections;

namespace xshazwar.noize.cpu.mutate {
    public interface IProvideTiles {
        public void GetData(out NativeSlice<float> data, out int resolution, out int tileSize);
    }

    public interface IUpdateImageChannel {
        public void UpdateImageChannel();
    }

    public interface IUpdateAllChannels {
        public void UpdateImageAllChannels();
    }

    public interface IHeightBroadcaster {
        // resolution, data
        public Action<int, NativeSlice<float>> OnHeightReady {get; set;}
    }

    public interface IHeightTarget {
        public void SetHeightValues(int resolution, NativeSlice<float> data);
    }
}