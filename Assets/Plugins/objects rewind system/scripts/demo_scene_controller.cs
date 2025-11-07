
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace object_rewind_system
{
    public class demo_scene_controller : MonoBehaviour
    {
        public GameObject[] rewindableObjects;
        public void StartRewind()
        {
            foreach (var obj in rewindableObjects)
            {
                var rewindComponent = obj.GetComponent<object_rewind>();
                if (rewindComponent != null)
                {
                    rewindComponent.Rewind();
                }
            }
        }
        public void StopRewind()
        {
            foreach (var obj in rewindableObjects)
            {
                var rewindComponent = obj.GetComponent<object_rewind>();
                if (rewindComponent != null)
                {
                    rewindComponent.StopRewind();
                }
            }
        }
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit))
            {
                object_rewind rewindManager = hit.collider.GetComponent<object_rewind>();

                if (rewindManager != null)
                {
                    rewindManager.Rewind();
                }
            }
        }
    }
}

    }
