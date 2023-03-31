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
    [AddComponentMenu("Noize/Pipeline/ReducePipeline", 0)]
    public class ReducePipeline : BasePipeline, IPipeline
    {
        // Takes a single PipelineWork Item, request the same item from both upstream pipelines.
        // On result, applies it's own reduce pipeline to the two work items.


        #if UNITY_EDITOR
        protected System.Diagnostics.Stopwatch reduceWall;
        #endif
        
        public BasePipeline upstreamPipelineLeft;

        public BasePipeline upstreamPipelineRight;

        public bool upstreamsRunning = false;

        protected PipelineJoint currentWorkItem;
        
        protected int currentDataLength = 0;
        protected NativeArray<float> rightData;

        public override BasePipeline[] GetDependencies(){
            List<BasePipeline> pipesUp = new List<BasePipeline>(){
                upstreamPipelineLeft,
                upstreamPipelineRight,
                this
            };
            // pipesUp.Add(upstreamPipelineLeft, upstreamPipelineRight, this);
            pipesUp.AddRange(upstreamPipelineLeft.GetDependencies());
            pipesUp.AddRange(upstreamPipelineRight.GetDependencies());
            return pipesUp.ToArray();
        }

        public override void OnUpdate(){
            if (!pipelineRunning && !pipelineBeingScheduled){
                if (queue.Count > 0){
                    Debug.Log("scheduling upstream from queue");
                    PipelineWorkItem wi;
                    if (queue.TryDequeue(out wi)){
                        upstreamsRunning = true; 
                        ScheduleUpstreams(wi);
                    }
                }else{
                    return;
                }
            }else if (!pipelineRunning && queue.Count > 0){
                Debug.Log($"{alias} has life of {queue.Count} objects in queue for {pipelineBeingScheduled}");
            }
        }

        protected void ScheduleUpstreams(PipelineWorkItem wi){
            Debug.Log("Scheduling upstream work");
            #if UNITY_EDITOR
            reduceWall = System.Diagnostics.Stopwatch.StartNew();
            #endif
            // GeneratorData leftData = (GeneratorData) req;
            GeneratorData leftData = (GeneratorData) wi.data;
            if(leftData.data.Length != currentDataLength){
                Debug.LogWarning("New data length requires native array reallocation");
                currentDataLength = leftData.data.Length;
                if(rightData.IsCreated){
                    rightData.Dispose();
                }
                rightData = new NativeArray<float>(currentDataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            if(!rightData.IsCreated){
                Debug.LogWarning("Right data needs to be created!");
                    rightData = new NativeArray<float>(currentDataLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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
                // action = onResult
                action = wi.completeAction
            };
            upstreamPipelineLeft.Enqueue(currentWorkItem.stages[Upstream.LEFT], completeAction: OnCompleteLeft);
            upstreamPipelineRight.Enqueue(currentWorkItem.stages[Upstream.RIGHT], completeAction: OnCompleteRight);
        }

        protected void OnCompleteUpstream(StageIO res, Upstream side){
            GeneratorData d = (GeneratorData) res;
            currentWorkItem.status[side] = true;
            currentWorkItem.stages[side] = d;
            if(currentWorkItem.ready){
                // Debug.Log("Upstreams ready!");
                #if UNITY_EDITOR
                reduceWall.Stop();
                Debug.LogWarning($"ReduceUpstreams -> {res.uuid}: {reduceWall.ElapsedMilliseconds}ms >> {d.xpos}, {d.zpos}");
                #endif
                upstreamsRunning = false;
                Schedule(
                    new ReduceData {
                        uuid = d.uuid,
                        resolution = d.resolution,
                        data = currentWorkItem.stages[Upstream.LEFT].data,
                        rightData = currentWorkItem.stages[Upstream.RIGHT].data,
                        xpos = d.xpos,
                        zpos = d.zpos

                    },
                    completeAction: currentWorkItem.action
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

        public override void Destroy()
        {
            if(rightData.IsCreated){
                rightData.Dispose();
            }
            base.Destroy();
        }

    }
}