using System;
using UnityEditor;
using UnityEngine;

namespace com.hhotatea.avatar_pose_library.editor
{
    internal enum TelemetryConsentDialogResult
    {
        Closed,
        Primary,
        Alternative
    }

    internal sealed class TelemetryConsentDialog : EditorWindow
    {
        private const float WindowWidth = 460f;
        private const float HorizontalMargin = 20f;
        private const float VerticalMargin = 14f;
        private const float ButtonWidth = 100f;
        private const float ButtonHeight = 24f;

        [NonSerialized] private Action<TelemetryConsentDialogResult> completed;
        [NonSerialized] private Action openPrivacyPolicy;
        [NonSerialized] private string message;
        [NonSerialized] private string primaryButton;
        [NonSerialized] private string alternativeButton;
        [NonSerialized] private string privacyPolicyLink;
        [NonSerialized] private TelemetryConsentDialogResult result;

        public static void Show(
            string title,
            string message,
            string primaryButton,
            string alternativeButton,
            string privacyPolicyLink,
            Action openPrivacyPolicy,
            Action<TelemetryConsentDialogResult> completed)
        {
            var window = CreateInstance<TelemetryConsentDialog>();
            window.titleContent = new GUIContent(title);
            window.message = message;
            window.primaryButton = primaryButton;
            window.alternativeButton = alternativeButton;
            window.privacyPolicyLink = privacyPolicyLink;
            window.openPrivacyPolicy = openPrivacyPolicy;
            window.completed = completed;

            var content = new GUIContent(message);
            var contentWidth = WindowWidth - HorizontalMargin * 2f;
            var messageHeight = EditorStyles.wordWrappedLabel.CalcHeight(
                content,
                contentWidth);
            var windowHeight = Mathf.Clamp(messageHeight + 116f, 180f, 520f);
            window.minSize = new Vector2(WindowWidth, windowHeight);
            window.maxSize = window.minSize;

            var mainWindow = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                mainWindow.center.x - WindowWidth / 2f,
                mainWindow.center.y - windowHeight / 2f,
                WindowWidth,
                windowHeight);
            window.ShowModalUtility();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape)
                {
                    Event.current.Use();
                    Close();
                    return;
                }

                if (Event.current.keyCode == KeyCode.Return
                    || Event.current.keyCode == KeyCode.KeypadEnter)
                {
                    Event.current.Use();
                    result = TelemetryConsentDialogResult.Primary;
                    Close();
                    return;
                }
            }

            EditorGUILayout.BeginVertical();
            GUILayout.Space(VerticalMargin);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(HorizontalMargin);
            EditorGUILayout.LabelField(
                message,
                EditorStyles.wordWrappedLabel,
                GUILayout.ExpandHeight(true));
            GUILayout.Space(HorizontalMargin);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(HorizontalMargin);
            if (GUILayout.Button(privacyPolicyLink, EditorStyles.linkLabel))
            {
                openPrivacyPolicy?.Invoke();
            }

            EditorGUIUtility.AddCursorRect(
                GUILayoutUtility.GetLastRect(),
                MouseCursor.Link);
            GUILayout.FlexibleSpace();
            GUILayout.Space(HorizontalMargin);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(VerticalMargin);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawButton(primaryButton, TelemetryConsentDialogResult.Primary);
            DrawButton(alternativeButton, TelemetryConsentDialogResult.Alternative);
            GUILayout.Space(HorizontalMargin);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(VerticalMargin);
            EditorGUILayout.EndVertical();
        }

        private void DrawButton(
            string label,
            TelemetryConsentDialogResult selectedResult)
        {
            if (GUILayout.Button(
                    label,
                    GUILayout.Width(ButtonWidth),
                    GUILayout.Height(ButtonHeight)))
            {
                result = selectedResult;
                Close();
            }
        }

        private void OnDestroy()
        {
            var callback = completed;
            completed = null;
            openPrivacyPolicy = null;
            callback?.Invoke(result);
        }
    }
}
