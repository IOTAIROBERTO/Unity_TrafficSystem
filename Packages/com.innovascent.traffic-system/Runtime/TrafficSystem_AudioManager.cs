using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace InnovAscent.TrafficSystem
{
    public class TrafficSystem_AudioManager : MonoBehaviour
    {
        [Header("Base Ambient Tracks (VR)")]
        public AudioSource baseTrack1;
        public AudioSource baseTrack2;

        [Range(0f, 1f)] public float baseVolume = 0.6f;

        [Header("Random Spatial Sounds")]
        public AudioSource randomSource;
        public List<AudioClip> randomClips;

        public float minDelay = 6f;
        public float maxDelay = 18f;

        [Range(0f, 1f)] public float randomVolume = 0.8f;
        [Range(0f, 1f)] public float playProbability = 0.75f;

        void Start()
        {
            SetupBaseTrack(baseTrack1);
            SetupBaseTrack(baseTrack2);

            StartCoroutine(RandomAmbientRoutine());
        }

        void SetupBaseTrack(AudioSource source)
        {
            if (!source) return;

            source.loop = true;
            source.volume = baseVolume;
            source.spatialBlend = 0.3f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 25f;
            source.Play();
        }

        IEnumerator RandomAmbientRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(minDelay, maxDelay));

                if (randomClips.Count == 0 || randomSource == null)
                    continue;

                if (Random.value <= playProbability)
                {
                    AudioClip clip = randomClips[Random.Range(0, randomClips.Count)];
                    randomSource.PlayOneShot(clip, randomVolume);
                }
            }
        }
    }
}
