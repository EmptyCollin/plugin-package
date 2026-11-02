using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>运行期调度泵（DESIGN §8.2 P4）：场景激活风暴走预算队列，不逐对象同帧重建。</summary>
    public static class StretchRuntimePump
    {
        private class Driver : MonoBehaviour
        {
            private void Update()
            {
                StretchScheduler.Pump();
            }
        }

        private static Driver _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Boot()
        {
            StretchScheduler.Clear();
            if (_driver != null) return;
            var go = new GameObject("[StretchScheduler]") { hideFlags = HideFlags.DontSave };
            _driver = go.AddComponent<Driver>();
            Object.DontDestroyOnLoad(go);
        }
    }
}
