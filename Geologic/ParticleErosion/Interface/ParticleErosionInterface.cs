using System;
using UnityEngine;

namespace xshazwar.noize.geologic {
    public interface IProvideGeodata {
        Action OnGeodataReady {get; set;}
        Action OnWaterUpdate {get; set;}
        public Texture2D GetWaterControlTexture();
        public Texture2D GetTerrainControlTexture();
    }
}