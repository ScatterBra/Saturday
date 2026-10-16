using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SaturdayGame.Minigames
{
    /// <summary>
    /// 放在办公室里的小游戏入口：玩家走近按 E，叠加加载小游戏场景，暂停办公室；
    /// 小游戏调用 MinigameSession.Finish 后卸载场景，恢复办公室，并触发 onFinished。
    /// </summary>
    public class MinigameLauncher : MonoBehaviour
    {
        [Tooltip("小游戏场景名（需要加入 Build Settings）")]
        public string sceneName = "ChessMinigame";
        public Transform player;
        public float interactRadius = 2.5f;
        public Key interactKey = Key.E;
        public string prompt = "Press E to play";
        [Tooltip("小游戏期间关掉的组件，例如玩家移动")]
        public Behaviour[] disableWhilePlaying;
        [Tooltip("小游戏期间隐藏的物体，例如办公室相机")]
        public GameObject[] hideWhilePlaying;
        [Tooltip("小游戏结束后触发，参数是小游戏给的结果")]
        public UnityEvent<string> onFinished;

        bool playing;

        bool PlayerInRange =>
            player != null && Vector3.Distance(player.position, transform.position) <= interactRadius;

        void Update()
        {
            if (playing || !PlayerInRange) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[interactKey].wasPressedThisFrame) Launch();
        }

        /// <summary>直接启动小游戏（也可以由剧情脚本调用，不经过按键）。</summary>
        public void Launch()
        {
            if (!playing) StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            playing = true;
            string result = null;
            bool done = false;
            void OnFinished(string r)
            {
                result = r;
                done = true;
            }

            MinigameSession.IsHosted = true;
            MinigameSession.Finished += OnFinished;
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            SetOfficeActive(false);

            while (!done) yield return null;

            MinigameSession.Finished -= OnFinished;
            SetOfficeActive(true);
            yield return SceneManager.UnloadSceneAsync(sceneName);
            MinigameSession.IsHosted = false;
            playing = false;
            onFinished?.Invoke(result);
        }

        void SetOfficeActive(bool active)
        {
            foreach (var b in disableWhilePlaying)
                if (b != null) b.enabled = active;
            foreach (var go in hideWhilePlaying)
                if (go != null) go.SetActive(active);
        }

        void OnGUI()
        {
            if (playing || !PlayerInRange) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width / 2f - 150, Screen.height - 110, 300, 50), prompt, style);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
