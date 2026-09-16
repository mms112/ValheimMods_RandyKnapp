using System.Collections.Generic;
using UnityEngine;

namespace AdvancedPortals
{
    public class AdvancedPortal : MonoBehaviour
    {
        public List<string> AllowedItems = new List<string>();
        public bool AllowEverything;
        public float minItemDur;
        public float maxRestedTime;

        private void Awake()
        {
        }
    }
}
