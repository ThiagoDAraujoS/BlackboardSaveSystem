using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Partition = System.Collections.Generic.Dictionary<string, object>;

// ReSharper disable once CheckNamespace
namespace BlackboardSaveSystem {
    [MovedFrom(true, "Core.State")]
    [RequireComponent(typeof(BlackboardClient))]
    public class TrackedTransform : TrackedBehaviour{
        [Header("Tracked Body")] [Tooltip("The actual moving object to track. If left empty, defaults to this GameObject.")] [SerializeField]
        protected Transform targetBody;

        [Tooltip("Should we track and save the rotation of the object as well?")] [SerializeField]
        protected bool saveRotation = true;

        protected override void OnAwake(){
            if (targetBody == null)
                targetBody = transform;
        }

        public override void OnSaveState(Partition state){
            state["body_position"] = new[]{ targetBody.position.x, targetBody.position.y, targetBody.position.z };
            if (saveRotation)
                state["body_rotation"] = new[]{ targetBody.eulerAngles.x, targetBody.eulerAngles.y, targetBody.eulerAngles.z };
            base.OnSaveState(state);
        }

        public override void OnLoadState(Partition state){
            base.OnLoadState(state);
            if (state.TryGetValue("body_position", out object rawPos)){
                IList   list     = (IList)rawPos;
                Vector3 position = new(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));

                if (targetBody.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent))
                    agent.Warp(position);
                else
                    targetBody.position = position;
            }

            if (saveRotation && state.TryGetValue("body_rotation", out object rawRot)){
                IList   list     = (IList)rawRot;
                Vector3 rotation = new(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]), Convert.ToSingle(list[2]));
                targetBody.rotation = Quaternion.Euler(rotation);
            }
        }
    }
}