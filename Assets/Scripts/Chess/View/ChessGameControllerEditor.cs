#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SaturdayGame.Chess.View
{
    /// <summary>在 Chess Game Controller 的 Inspector 底部加调试按钮（只在 Play 模式下能点）。</summary>
    [CustomEditor(typeof(ChessGameController))]
    public class ChessGameControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("调试", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                var controller = (ChessGameController)target;
                if (GUILayout.Button("跳到第三阶段")) controller.JumpToStage3();
                if (GUILayout.Button("重新开局")) controller.NewGame();
            }
            if (!Application.isPlaying) EditorGUILayout.HelpBox("进入 Play 模式后才能用。", MessageType.None);
        }
    }
}
#endif
