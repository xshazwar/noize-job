
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEditor;

using Unity.Collections;

namespace xshazwar.noize.geologic {

    public enum LivePoolDebugAction {
            INFO = 0,
            FILL = 1
            // EMPTY = 2
    }
    public class LivePoolDebugger : MonoBehaviour {

        LivePoolDebugAction action = LivePoolDebugAction.INFO;
        public float volume = 1f;

        private PoolDrawer drawer;

        private NativeParallelHashMap<PoolKey, Pool>.Enumerator poolIter;
        private NativeParallelMultiHashMap<int, int>.KeyValueEnumerator boundIter;
        private bool poolsReady = false;

        bool drawBoundaries = false;
        bool drawPools = false;
        bool drawFlags = true;

        private void NextAction(){
            action = (LivePoolDebugAction)(((int) action + 1) % Enum.GetNames(typeof(LivePoolDebugAction)).Length);
        }

        private void IncreaseVolume(){
            volume += 0.25f;
            volume = Mathf.Min(volume, 10f);
            Debug.Log($"volume {volume}");
        }

        private void DecreaseVolume(){
            volume -= 0.25f;
            volume = Mathf.Max(volume, 0f);
            Debug.Log($"volume {volume}");
        }


        void Start()
        {
            drawer = GetComponent<PoolDrawer>();
        }

        void Update(){
            if (Input.GetKeyDown(KeyCode.Tab)){
                NextAction();
            }
            if (Input.GetKeyDown(KeyCode.UpArrow)){
                IncreaseVolume();
            }
            if (Input.GetKeyDown(KeyCode.DownArrow)){
                DecreaseVolume();
            }
            if (Input.GetKeyDown(KeyCode.P)) drawPools = !drawPools;
            if (Input.GetKeyDown(KeyCode.B)) drawBoundaries = !drawBoundaries;
            if (Input.GetKeyDown(KeyCode.F)) drawFlags = !drawFlags;
        }

        void OnCollisionEnter(Collision collision){
            foreach (ContactPoint contact in collision.contacts)
            {
                Debug.DrawRay(contact.point, contact.normal, Color.white);
                DoAction(AtLocation(contact.point));
            }
        }

        private Vector2Int AtLocation(Vector3 loc){
            Vector2Int gp = new Vector2Int();
            gp.y = (int) (drawer.meshResolution * (loc.x - gameObject.transform.position.x) / (float) drawer.tileSize);
            gp.x = (int) (drawer.meshResolution * (loc.z - gameObject.transform.position.z) / (float) drawer.tileSize);
            
            Debug.Log($"HitLocation: {gp.x}, {gp.y}, h:{loc.y}");
            return gp;
        }

        private Vector2Int GridSpaceAtIdx(int idx){
            Vector2Int tmp = new Vector2Int();
            tmp.y = (idx % drawer.generatorResolution);
            tmp.x = (idx - tmp.y) / drawer.generatorResolution;
            tmp.x -= drawer.marginRes;
            tmp.y -= drawer.marginRes;
            return tmp;
        }


        private Vector2 WorldSpaceAtGS(Vector2Int pos){
            return new Vector2() {
                x = (((pos.y * (float) drawer.tileSize)  / (float) drawer.meshResolution) + gameObject.transform.position.x),
                y = (((pos.x * (float) drawer.tileSize)  / (float) drawer.meshResolution) + gameObject.transform.position.z)
            };  

        }

        private void DoAction(Vector2Int pos){
            switch(action){
                case LivePoolDebugAction.INFO:
                    PrintHeirarchy(pos);
                    break;
                case LivePoolDebugAction.FILL:
                    drawer.RegisterChange(pos, volume);
                    break;
                // case LivePoolDebugAction.EMPTY:
                //     drawer.RegisterChange(pos, volume);
                //     break;
            }
            Debug.Log(action);
        }

        private void PrintHeirarchy(Vector2Int pos){
            int minima = drawer.GetAssociatedMinima(pos);
            PoolKey key = drawer.GetAssociateFirstOrderKey(minima);
            HashSet<Pool> direct_;
            HashSet<Pool> peers_;
            drawer.GetAssociatedPools(key, out direct_, out peers_);
            List<Pool> direct = direct_.ToList<Pool>();
            direct.Sort();
            List<Pool> peers = peers_.ToList<Pool>();
            peers.Sort();
            Debug.Log($"Direct Line --- {direct.Count}");
            foreach( Pool d in direct){
                PrintPool(d);
            }
            Debug.Log($"Peers --- {peers.Count}");
            foreach( Pool p in peers){
                PrintPool(p);
            }
        }

        public Vector3 DrawPos(int idx, float height){
            Vector2Int gs = GridSpaceAtIdx(idx);  
            Vector2 ws = WorldSpaceAtGS(gs);
            return new Vector3() {
                x = ws.x,
                y = height * drawer.tileHeight,
                z = ws.y
            };
        }
        private void PrintPool(Pool p){
            Debug.Log($"idx| {p.indexMinima}:{p.order} vol| {p.volume} / {p.capacity} >> {p.supercededBy.idx}:{p.supercededBy.order}");
        }

        private void PoolGizmo(Pool p){
            Vector3 indicatorVector = Vector3.up * 250;
            Color color = GetOrderColor(p.order);
            Vector3 minimaPos = DrawPos(p.indexMinima, p.minimaHeight);
            Vector3 drainPos = DrawPos(p.indexDrain, p.drainHeight);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(minimaPos, indicatorVector);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(drainPos, indicatorVector);
            Handles.Label(minimaPos + 0.75f * (drainPos - minimaPos), $"{p.order}");
            Handles.Label(minimaPos + 10 * Vector3.down, $"{p.indexMinima}");
            Gizmos.color = Color.white;
            Gizmos.DrawLine(minimaPos, drainPos);
        }

        private Color GetOrderColor(byte b){
            Color c = new Color(0,0,0,255);
            c.r += (b & (1 << 1)) != 0 ? 255 : 128;
            c.b += (b & (1 << 2)) != 0 ? 255 : 0;
            c.g += (b & (1 << 3)) != 0 ? 255 : 64;
            return c;
        }

        private bool CheckPools(){
            if (!drawer.poolsReady){
                poolsReady = false;
                return false;
            }
            if (poolsReady) return true;
            // poolsReady = drawer.stateManager.BufferExists<NativeParallelHashMap<PoolKey, Pool>>(drawer.getBufferName("PARTERO_POOLS")) &&
            //     !drawer.stateManager.IsLocked<NativeParallelHashMap<PoolKey, Pool>>(drawer.getBufferName("PARTERO_POOLS"));
            // Debug.Log(poolsReady);
            poolsReady = drawer.ready;
            boundIter = drawer.boundary_BM.GetEnumerator();
            return poolsReady;
        }

        void OnDrawGizmos() 
        {
            if(!CheckPools()) return;
            if(drawBoundaries) DrawBoundaryConnections();
            if (drawPools) DrawPoolHeirarchy();
            
            
        }

        void DrawPoolHeirarchy(){
            poolIter = drawer.pools.GetEnumerator();
            while(poolIter.MoveNext()){
                PoolGizmo(poolIter.Current.Value);
            }
        }
        void DrawBoundaryConnections(){
            int k;
            int v;
            Vector2Int gs;
            Vector2 ws;
            Vector3 a;
            Vector3 b;
            boundIter.Reset();
            Color color = new Color(255, 255, 255);
            color.a = 0.02f;
            Gizmos.color = color;
            while(boundIter.MoveNext()){
                k = boundIter.Current.Key;
                v = boundIter.Current.Value;
                gs = GridSpaceAtIdx(k);  
                ws = WorldSpaceAtGS(gs);
                a = new Vector3(){
                    x = ws.x, y = drawer.heightMap[k] * drawer.tileHeight, z = ws.y
                };
                gs = GridSpaceAtIdx(v);  
                ws = WorldSpaceAtGS(gs);
                b = new Vector3(){
                    x = ws.x, y = drawer.heightMap[v] * drawer.tileHeight, z = ws.y
                };
                Gizmos.DrawLine(a, b);
                if(drawFlags){
                    Gizmos.color = Color.red;
                    Gizmos.DrawRay(a, 5 * Vector3.up);
                    Gizmos.color = color;
                }
            }

            
        }
    }
}