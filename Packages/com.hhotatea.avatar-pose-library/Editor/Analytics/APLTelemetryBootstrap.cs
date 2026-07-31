using System;
using com.hhotatea.avatar_pose_library.component;
using UnityEditor;
using UnityEngine;

namespace com.hhotatea.avatar_pose_library.editor
{
    [InitializeOnLoad]
    public static class APLTelemetryBootstrap
    {
        private const string SessionInitializedKey =
            "com.hhotatea.avatar-pose-library.telemetry.session-initialized";

        static APLTelemetryBootstrap()
        {
            EditorApplication.delayCall += TryInitialize;
        }

        private static void TryInitialize()
        {
            if (SessionState.GetBool(SessionInitializedKey, false))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryInitialize;
                return;
            }

            if (Application.isBatchMode)
            {
                FinishInitialization(TelemetryPreferences.HasSelection);
                return;
            }

            var configuration = DynamicVariables.TelemetryConfiguration;
            if (configuration == null
                || (!configuration.CanSendLogs && !configuration.CanSendErrors))
            {
                FinishInitialization(false);
                return;
            }

            // Version acquisition, including its session-start event, is
            // independent of the telemetry consent choice.
            _ = DynamicVariables.LatestVersion;

            if (TelemetryPreferences.RequiresChoice(configuration))
            {
                ShowPrivacyChoice();
                return;
            }

            FinishInitialization(true);
        }

        public static void ShowPrivacyChoice()
        {
            var configuration = DynamicVariables.TelemetryConfiguration;
            if (configuration == null)
            {
                return;
            }

            var inspector = DynamicVariables.Settings.Inspector;
            var accepted = EditorUtility.DisplayDialog(
                inspector.telemetryPrivacyDialogTitle,
                inspector.telemetryPrivacyDialogMessage,
                inspector.telemetryDetailedConsentButton,
                inspector.telemetryMinimalConsentButton);

            TelemetryPreferences.SetMode(
                accepted ? TelemetryMode.Detailed : TelemetryMode.Minimal,
                configuration);

            if (!SessionState.GetBool(SessionInitializedKey, false))
            {
                FinishInitialization(true);
            }
        }

        public static void RequestDetailedErrorConsent(Action<bool> completed)
        {
            if (TelemetryPreferences.IsDetailed)
            {
                completed?.Invoke(true);
                return;
            }

            if (Application.isBatchMode)
            {
                completed?.Invoke(false);
                return;
            }

            var configuration = DynamicVariables.TelemetryConfiguration;
            var inspector = DynamicVariables.Settings.Inspector;
            var accepted = EditorUtility.DisplayDialog(
                inspector.telemetryErrorDialogTitle,
                inspector.telemetryErrorDialogMessage,
                inspector.telemetryYesButton,
                inspector.telemetryNoButton);
            if (accepted)
            {
                TelemetryPreferences.SetMode(TelemetryMode.Detailed, configuration);
            }

            completed?.Invoke(accepted);
        }

        private static void FinishInitialization(bool startSession)
        {
            SessionState.SetBool(SessionInitializedKey, true);
            if (startSession)
            {
                StartSession();
            }
        }

        private static void StartSession()
        {
            if (TelemetryPreferences.ConsumeFirstSessionPending())
            {
                APLTelemetry.SendFirstSession();
            }

            _ = DynamicVariables.LatestVersion;

            var current = DynamicVariables.CurrentVersion.ToString();
            var previous = TelemetryPreferences.LastAplVersion;
            if (!string.IsNullOrWhiteSpace(previous)
                && !string.Equals(previous, current, StringComparison.Ordinal))
            {
                APLTelemetry.SendVersionChanged(previous);
            }

            TelemetryPreferences.LastAplVersion = current;
        }
    }
}
