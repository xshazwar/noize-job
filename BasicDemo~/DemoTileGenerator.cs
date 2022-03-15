using UnityEngine;

using xshazwar.noize.scripts;

[AddComponentMenu("Noize/Demo/DemoTileGenerator", 0)]
    [RequireComponent(typeof(MeshTileGenerator))]
    public class DemoTileGenerator : MonoBehaviour {

        public int xRange = 1;
        public int zRange = 1;
        public void Start(){
            MeshTileGenerator generator = GetComponent<MeshTileGenerator>();
            for (int x = 0; x <= xRange; x ++){
                for (int z = 0; z <= zRange; z ++){
                    Vector2Int coord = new Vector2Int(x, z);
                    generator.Enqueue(coord.ToString(), coord);
                }
            }

        }
    }