using System.Collections;
using UnityEngine;

namespace object_rewind_system
{
    public class object_rewind : MonoBehaviour
    {
        private Vector3[] positionBuffer;
        private Quaternion[] rotationBuffer;
        private Vector3 targetPosition;
        private Quaternion targetRotation;
        private Rigidbody rb;
        private Rigidbody2D rb2d;
        public int timeDurationInSeconds = 7;
        public int recordInterval = 4;
        public float playbackSmoothness = 0.9f;
        private int totalBufferSize;
        private int frameCounter = 0;
        private int writeIndex = 0;
        private int recordedFrames=0;
        private bool isRewinding = false;
        private int rewindIndex;
        private void Awake()
        {
            totalBufferSize = timeDurationInSeconds * 50 / recordInterval;
            positionBuffer = new Vector3[totalBufferSize];
            rotationBuffer = new Quaternion[totalBufferSize];
            rb = GetComponent<Rigidbody>();
            rb2d=GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            if (isRewinding)
            {
                RewindTransform();
            }
            else
            {
                RecordTransform();
            }
        }

        private void RecordTransform()
        {
            if (rb)
            rb.isKinematic = false;
            if (rb2d)
            rb2d.bodyType=RigidbodyType2D.Dynamic;
            frameCounter++;

            if (frameCounter == recordInterval)
            {
                frameCounter = 0;

                positionBuffer[writeIndex] = transform.position;
                rotationBuffer[writeIndex] = transform.rotation;

                writeIndex = (writeIndex + 1) % totalBufferSize;
                recordedFrames++;
            }
        }

        private void RewindTransform()
        {
            if (rb)
            rb.isKinematic = true;
            if (rb2d)
            rb2d.bodyType=RigidbodyType2D.Kinematic;
            if(recordedFrames-->0) 
            {
            targetPosition = positionBuffer[rewindIndex];
            targetRotation = rotationBuffer[rewindIndex];
            if (rb)
            {
            rb.MovePosition(Vector3.Slerp(rb.position, targetPosition, playbackSmoothness));
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, playbackSmoothness).normalized);
            }
            if (rb2d)
            {
            rb2d.MovePosition(Vector3.Slerp(rb2d.position, targetPosition, playbackSmoothness));
            rb2d.MoveRotation(Mathf.LerpAngle(rb2d.rotation, targetRotation.eulerAngles.z, playbackSmoothness));
            }
            }
            rewindIndex = (rewindIndex - 1 + totalBufferSize) % totalBufferSize;
            if (recordedFrames==0)
            {
                rewindIndex = writeIndex;
            }
            if (rewindIndex == writeIndex)
            {
                for (int i = 0; i < totalBufferSize; i++)
                {
                positionBuffer[i] = Vector3.zero;
                rotationBuffer[i] = Quaternion.identity;
                }
                StopRewind();
            }
        }
        public void Rewind()
        {
            if (!isRewinding)
            {
            isRewinding = true;
            rewindIndex = (writeIndex - 1 + totalBufferSize) % totalBufferSize;
            }
        }
        public void StopRewind()
        {
            if(isRewinding)
            {
            isRewinding = false;
            frameCounter = 0;
            recordedFrames=0;
            }
        }
    }
}
