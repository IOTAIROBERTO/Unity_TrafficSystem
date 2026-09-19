using UnityEngine;
using System.Collections.Generic;

namespace InnovAscent.TrafficSystem
{
    public class VehiclePool
    {
        private GameObject prefab;
        private Transform parent;
        private readonly Stack<GameObject> pool = new Stack<GameObject>();

        public VehiclePool(GameObject prefab, int initialSize, Transform parent)
        {
            this.prefab = prefab;
            this.parent = parent;

            for (int i = 0; i < initialSize; i++)
            {
                GameObject obj = GameObject.Instantiate(prefab, parent);
                obj.SetActive(false);
                pool.Push(obj);
            }
        }

        public GameObject Get()
        {
            GameObject obj = pool.Count > 0 ? pool.Pop() : GameObject.Instantiate(prefab, parent);
            obj.SetActive(true);
            return obj;
        }

        public void Return(GameObject obj)
        {
            obj.SetActive(false);
            pool.Push(obj);
        }
    }
}
