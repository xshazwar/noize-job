using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;

using xshazwar.noize;
using xshazwar.noize.pipeline;
#if UNITY_EDITOR
using static System.Diagnostics.Stopwatch;
using xshazwar.noize.editor;
#endif

using UnityEngine;
using Unity.Collections;

namespace xshazwar.noize.scripts {

    public class PipelineJoint {
        public Dictionary<Upstream, GeneratorData> stages;
        public Dictionary<Upstream, bool> status;

        public Action<StageIO> action;

        public bool ready => status[Upstream.LEFT] == true && status[Upstream.RIGHT] == true;
    }

    public enum Upstream {
        LEFT,
        RIGHT
    }
    [AddComponentMenu("Noize/Pipeline/Reduce", 0)]
    public class ReducePipeline : BasePipeline, IPipeline
    {
        // Takes a single PipelineWork Item, request the same item from both upstream pipelines.
        // On result, applies it's own reduce pipeline to the two work items.


        #if UNITY_EDITOR
        protected System.Diagnostics.Stopwatch reduceWall;
        #endif
        
        public GeneratorPipeline upstreamPipelineLeft;

        public GeneratorPipeline upstreamPipelineRight;

        public bool upstreamsRunning = false;

        protected PipelineJoint currentWorkItem;
        
        protected int currentDataLength = 0;
        protected NativeArray<float> rightData;

        public override BasePipeline[] GetDependencies(){
            return new BasePipeline[]{upstreamPipelineLeft, upstreamPipelineRight, this};
        }

        public override void OnUpdate(){
            if (upstreamsRunning){
                Debug.Log("waiting for upstream");
                return;
            }
            if (pipelineRunning){
                Debug.Log("waiting for self to complete");
                foreach(PipelineStage stage in stage_instances){
                    stage.OnUpdate();
                }
            }else if (!pipelineRunning && !pipelineQueued && !upstreamsRunning){
                if (queue.Count > 0){
                    Debug.Log("scheduling upstream from queue");
                    PipelineWorkItem wi;
                    if (queue.TryDequeue(out wi)){
                        upstreamsRunning = true; 
                        ScheduleUpstreams(wi.data, wi.action);
                    }
                }else{
                    return;
                }
            }else if (pipelineQueued && !pipelineRunning){
                Debug.Log("kicking off pipeline");
                pipelineRunning = true;
                stage_instances[0].ReceiveInput(pipelineInput);
                pipelineQueued = false;
            }
        }

        protected void ScheduleUpstreams(StageIO req, Action<StageIO> onResult){
            Debug.Log("Scheduling upstream work");
            #if UNITY_EDITOR
            reduceWall = System.Diagnostics.Stopwatch.StartNew();
            #endif
            GeneratorData leftData = (GeneratorData) req;
            if(leftData.data.Length != currentDataLength){
                Debug.LogWarning("New data length requires native array reallocation");
                currentDataLength = leftData.data.Length;
                if(rightData.IsCreated){
                    rightData.Dispose();
                }
                rightData = new NativeArray<float>(currentDataLength, Allocator.Persistent);
            }
            if(!rightData.IsCreated){
                Debug.LogWarning("Right data needs to be created!");
                    rightData = new NativeArray<float>(currentDataLength, Allocator.Persistent);
                }
            currentWorkItem = new PipelineJoint {
                status = new Dictionary<Upstream, bool> {
                    {Upstream.LEFT, false}, {Upstream.RIGHT, false}
                },
                stages = new Dictionary<Upstream, GeneratorData> {
                    {Upstream.LEFT, leftData},
                    {Upstream.RIGHT, new GeneratorData {
                        uuid = leftData.uuid,
                        resolution = leftData.resolution,
                        data = new NativeSlice<float>(rightData),
                        xpos = leftData.xpos,
                        zpos = leftData.zpos
                    }}
                },
                action = onResult
            };
            upstreamPipelineLeft.Enqueue(currentWorkItem.stages[Upstream.LEFT], OnCompleteLeft);
            upstreamPipelineRight.Enqueue(currentWorkItem.stages[Upstream.RIGHT], OnCompleteRight);
        }

        protected void OnCompleteUpstream(StageIO res, Upstream side){
            GeneratorData d = (GeneratorData) res;
            currentWorkItem.status[side] = true;
            currentWorkItem.stages[side] = d;
            if(currentWorkItem.ready){
                // Debug.Log("Upstreams ready!");
                #if UNITY_EDITOR
                reduceWall.Stop();
                Debug.LogWarning($"ReduceUpstreams -> {res.uuid}: {reduceWall.ElapsedMilliseconds}ms");
                #endif
                upstreamsRunning = false;
                Schedule(
                    new ReduceData {
                        uuid = d.uuid,
                        resolution = d.resolution,
                        data = currentWorkItem.stages[Upstream.LEFT].data,
                        rightData = currentWorkItem.stages[Upstream.RIGHT].data
                    },
                    currentWorkItem.action
                );
            }else{
                Debug.Log($"upstreams not complete: L {currentWorkItem.status[Upstream.LEFT]}, R {currentWorkItem.status[Upstream.RIGHT]}");
            }
        }

        protected void OnCompleteLeft(StageIO res){
            OnCompleteUpstream(res, Upstream.LEFT);
        }

        protected void OnCompleteRight(StageIO res){
            OnCompleteUpstream(res, Upstream.RIGHT);
        }

        public override void OnDestroy()
        {
            if(rightData.IsCreated){
                rightData.Dispose();
            }
            base.OnDestroy();
        }

    }
}