using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

using UnityEngine;
using UnityEngine.Profiling;

using Unity.Collections;
using Unity.Jobs;

using static Unity.Mathematics.math;
using Unity.Mathematics;

using xshazwar.noize.pipeline;
using xshazwar.noize;
using xshazwar.noize.scripts;
using xshazwar.noize.mesh;
using xshazwar.noize.geologic;

namespace xshazwar.noize.scripts {
    [AddComponentMenu("Noize/MeshTileRenderer", 0)]
    public class MeshTileRenderer : MonoBehaviour {
        
        public string activeSaveName;
        public MeshBakery bakery;
        
        public int tileHeight = 1000;
        public int tileSize = 1000;
        public int generatorResolution = 1000;

    }
}