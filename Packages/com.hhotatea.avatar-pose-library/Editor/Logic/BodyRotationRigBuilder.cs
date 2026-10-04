using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using com.hhotatea.avatar_pose_library.editor;
using com.hhotatea.avatar_pose_library.model;

namespace com.hhotatea.avatar_pose_library.logic
{
    /// <summary>
    /// ポーズを焼き込まずに体全体を回転させる仕組み。
    ///
    /// 【従来の方式】
    /// MotionBuilder.BuildBodyRotation がクリップの RootQ（Hips の回転）へ
    /// 「回転 * RootQ」を焼き込んだ複製を作っていました。
    /// 軸ごとの段数の組み合わせ分だけクリップが増えるため、
    /// 1ポーズあたり Steps^3 * 4 本（Steps=3 で 108 本）必要でした。
    ///
    /// 【一度試した方式（このバージョンで廃止）】
    /// アバタールート直下に Transform の鎖を作り、
    /// VRCRotationConstraint / VRCPositionConstraint でアーマチュアを追従させる方式。
    /// VRChat の制約は VRChat のクライアントが解くもので Unity のプレイモードでは動かないため、
    /// 「アップロードするまで確認できない」「プレイモードでは鎖だけ回って体が付いてこない」
    /// という、原因の分からない症状を生んでいました。
    ///
    /// 【この方式】
    /// アーマチュア自身の localPosition / localRotation を FX レイヤーのアニメーションで直接書きます。
    ///  - 制約を使わないので Unity のプレイモードでも VRChat でも同じように動く
    ///  - 親子関係を変えないので、ボーンのバインディングパスも既存クリップも無傷
    ///  - 高さスライダーは従来どおりポーズ側（アバタールートの Root Transform）で扱うため干渉しない
    ///
    /// 支点は Hips です。アーマチュアの原点（足元）ではなく Hips を中心に回すための
    /// localPosition の補正は <see cref="BodyRotationArmatureSolver"/> が計算します。
    ///
    ///   AvatarRoot
    ///   └─ Armature   ... localPosition と localRotation をこのレイヤーが書く
    ///      └─ Hips
    ///
    /// 3 軸を同時に掛け合わせる必要があるため、クリップ数は Steps^3 本です
    /// （Steps=5 で 125 本）。ポーズ数には依存しません。
    /// ブレンドツリーは軸ごとの Simple1D（X → Y → Z）を段組みにして、
    /// パラメーター空間を Steps^3 の格子として張ります。
    /// （Unity の公開 API に 3 パラメーターのブレンドツリーは存在しないため、
    ///  MakeLocomotionAnim の回転合成と同じ入れ子構成で表現します。）
    /// 格子の各点は角度が確定しているので、補間は隣接する格子点どうしの
    /// クォータニオン補間になり、軸を個別に足し合わせたときのような破綻は起きません。
    /// </summary>
    public static class BodyRotationRigBuilder
    {
        /// <summary>
        /// 回転用レイヤー・ステートの名前。
        /// </summary>
        const string RotationStateName = "Rotate";

        static string AxisParameter(ConstVariables.BodyRotationAxis axis, AvatarPoseData poseLibrary)
        {
            return $"{axis.ParameterPrefix}_{poseLibrary.Guid}";
        }

        /// <summary>
        /// 回転専用の AnimatorController を組み立てます。
        ///
        /// ポーズ側とは別コントローラーとしてマージします。
        /// Write Defaults を必ず false で扱う必要があるため、
        /// Modular Avatar の Write Defaults 自動一致（matchAvatarWriteDefaults）を
        /// 無効にしてマージできるよう、独立させています。
        /// </summary>
        /// <param name="poseLibrary">ポーズライブラリ</param>
        /// <param name="solver">回転の支点と基準姿勢。null の場合はリグを作れなかったことを意味します</param>
        /// <returns>回転用コントローラー。リグが作れない場合は null</returns>
        public static AnimatorController BuildAnimator(
            AvatarPoseData poseLibrary,
            BodyRotationArmatureSolver solver)
        {
            if (poseLibrary == null || solver == null)
            {
                return null;
            }

            var controller = new AnimatorController();

            // 既定値は式パラメーター・BaseAnimator 側と必ず揃えます。
            // ずれるとレイヤー起動直後に体が片側へ傾いた状態で始まります。
            // X/Z の無回転は 0、Y の無回転は 0.5 です。
            foreach (var axis in ConstVariables.BodyRotationAxes)
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = AxisParameter(axis, poseLibrary),
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = axis.NeutralValue,
                });
            }

            AddLayers(controller, poseLibrary, solver);
            return controller;
        }

        /// <summary>
        /// FX アニメーターへ回転用レイヤーを 1 本追加します。
        /// </summary>
        public static void AddLayers(
            AnimatorController controller,
            AvatarPoseData poseLibrary,
            BodyRotationArmatureSolver solver)
        {
            if (controller == null || poseLibrary == null || solver == null)
            {
                return;
            }

            AddRotationLayer(controller, poseLibrary, solver);
        }

        static void AddRotationLayer(
            AnimatorController controller,
            AvatarPoseData poseLibrary,
            BodyRotationArmatureSolver solver)
        {
            var layerName = $"{ConstVariables.BodyRotationRigLayerPrefix}_{poseLibrary.Guid}";
            var layer = new AnimatorControllerLayer
            {
                name = layerName,
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine(),
                blendingMode = AnimatorLayerBlendingMode.Override,
            };
            controller.AddLayer(layer);

            // AddLayer 後の実体は controller 側にあるため、そこから引き直します。
            var layerIndex = controller.layers.Length - 1;
            var actualLayer = controller.layers[layerIndex];

            var state = actualLayer.stateMachine.AddState(RotationStateName);
            // ポーズ側のクリップを消さないよう、Write Defaults は必ず切ります。
            state.writeDefaultValues = false;
            // 既定ステートにしないとレイヤーが何も出力しません。
            actualLayer.stateMachine.defaultState = state;

            var xAxis = ConstVariables.BodyRotationAxisX;
            var yAxis = ConstVariables.BodyRotationAxisY;
            var zAxis = ConstVariables.BodyRotationAxisZ;

            var steps = Mathf.Max(2, ConstVariables.BodyRotationSteps);

            // 3 パラメーターのブレンドツリーは Unity の公開 API に無いため、
            // MakeLocomotionAnim と同じく軸ごとの Simple1D を段組みで表現します。
            // どの軸を外側にしても格子点は同じ (steps^3) になるので、
            // X → Y → Z の順で入れ子にします。
            var xTree = new BlendTree
            {
                name = layerName,
                blendType = BlendTreeType.Simple1D,
                blendParameter = AxisParameter(xAxis, poseLibrary),
                useAutomaticThresholds = false,
            };
            state.motion = xTree;

            for (var xIndex = 0; xIndex < steps; xIndex++)
            {
                var xRate = StepRate(xIndex, steps);
                var xAngle = xAxis.AngleAt(xRate);

                var yTree = new BlendTree
                {
                    name = $"{layerName}_Y{xIndex}",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = AxisParameter(yAxis, poseLibrary),
                    useAutomaticThresholds = false,
                };

                for (var yIndex = 0; yIndex < steps; yIndex++)
                {
                    var yRate = StepRate(yIndex, steps);
                    var yAngle = yAxis.AngleAt(yRate);

                    var zTree = new BlendTree
                    {
                        name = $"{layerName}_Z{xIndex}_{yIndex}",
                        blendType = BlendTreeType.Simple1D,
                        blendParameter = AxisParameter(zAxis, poseLibrary),
                        useAutomaticThresholds = false,
                    };

                    for (var zIndex = 0; zIndex < steps; zIndex++)
                    {
                        var zRate = StepRate(zIndex, steps);
                        var zAngle = zAxis.AngleAt(zRate);

                        // パラメーター（0..1）の格子点に、その角度の姿勢を置きます。
                        zTree.AddChild(
                            BuildRotationClip(solver, xAngle, yAngle, zAngle),
                            zRate);
                    }

                    yTree.AddChild(zTree, yRate);
                }

                xTree.AddChild(yTree, xRate);
            }
        }

        static float StepRate(int index, int steps)
        {
            return steps <= 1 ? 0f : index / (float)(steps - 1);
        }

        /// <summary>
        /// 1 つの角度の組み合わせに対応する、アーマチュアの姿勢を書き込んだクリップを作ります。
        /// 書き込む先はアーマチュア自身のローカル TRS で、ボーンではありません。
        /// </summary>
        static AnimationClip BuildRotationClip(
            BodyRotationArmatureSolver solver,
            float xAngle,
            float yAngle,
            float zAngle)
        {
            var clip = new AnimationClip
            {
                name = $"AnimPoseBodyRotate_{xAngle:0.##}_{yAngle:0.##}_{zAngle:0.##}",
                frameRate = 60f,
                legacy = false,
            };

            var path = solver.BindingPath;
            var position = solver.SolvePosition(xAngle, yAngle, zAngle);
            var rotation = solver.SolveRotation(xAngle, yAngle, zAngle);

            SetConstant(clip, path, "m_LocalPosition.x", position.x);
            SetConstant(clip, path, "m_LocalPosition.y", position.y);
            SetConstant(clip, path, "m_LocalPosition.z", position.z);

            SetConstant(clip, path, "m_LocalRotation.x", rotation.x);
            SetConstant(clip, path, "m_LocalRotation.y", rotation.y);
            SetConstant(clip, path, "m_LocalRotation.z", rotation.z);
            SetConstant(clip, path, "m_LocalRotation.w", rotation.w);

            return clip;
        }

        static void SetConstant(AnimationClip clip, string path, string property, float value)
        {
            clip.SetCurve(path, typeof(Transform), property, AnimationCurve.Constant(0f, 1f, value));
        }

        /// <summary>
        /// 旧バージョンがシーンに残したリグと、それに紐づく制約を片付けます。
        ///
        /// 制約方式だった頃はアバタールート直下に AnimPoseBodyRotateRig を作り、
        /// アーマチュアに VRCRotationConstraint / VRCPositionConstraint を付けていました。
        /// 今はどちらも使いませんが、当時のシーンをそのまま開くと残っていることがあります。
        /// 残った制約は参照先を失って「何も動かさない置物」になるため、ここで外します。
        /// </summary>
        public static void CleanupLegacyRig(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var legacyRig = avatarRoot.transform.Find(ConstVariables.BodyRotationRigRootName);

            RemoveConstraintsReferencing(avatarRoot.transform, legacyRig);
            RemoveDanglingConstraints(avatarRoot.transform);

            if (legacyRig != null)
            {
                Object.DestroyImmediate(legacyRig.gameObject);
            }
        }

        /// <summary>
        /// 旧バージョンが使っていた Unity の制約の型名。
        ///
        /// このパッケージは com.unity.animation.rigging に依存していません。
        /// 型を直接書くと入っていない環境でコンパイルが通らなくなるため、
        /// 入っている場合だけ片付けられるように名前で照合します。
        /// </summary>
        const string LegacyRotationConstraintTypeName = "UnityEngine.Animations.RotationConstraint";

        const string LegacyPositionConstraintTypeName = "UnityEngine.Animations.PositionConstraint";

        /// <summary>
        /// <paramref name="source"/> 配下を参照している制約を外します。
        /// </summary>
        static void RemoveConstraintsReferencing(Transform avatarRoot, Transform source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var constraint in avatarRoot.GetComponentsInChildren<VRCRotationConstraint>(true))
            {
                if (UsesSource(constraint, source))
                {
                    Object.DestroyImmediate(constraint);
                }
            }

            foreach (var constraint in avatarRoot.GetComponentsInChildren<VRCPositionConstraint>(true))
            {
                if (UsesSource(constraint, source))
                {
                    Object.DestroyImmediate(constraint);
                }
            }

            RemoveLegacyConstraintsReferencing(avatarRoot, source);
        }

        /// <summary>
        /// 参照先を失った制約を外します。
        /// ソースが 1 本も残っていない制約は何も動かせないため、外しても挙動は変わりません。
        /// </summary>
        static void RemoveDanglingConstraints(Transform avatarRoot)
        {
            foreach (var constraint in avatarRoot.GetComponentsInChildren<VRCRotationConstraint>(true))
            {
                if (IsDangling(constraint))
                {
                    Object.DestroyImmediate(constraint);
                }
            }

            foreach (var constraint in avatarRoot.GetComponentsInChildren<VRCPositionConstraint>(true))
            {
                if (IsDangling(constraint))
                {
                    Object.DestroyImmediate(constraint);
                }
            }
        }

        static bool IsDangling(VRCConstraintBase constraint)
        {
            if (constraint.Sources.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < constraint.Sources.Count; index++)
            {
                if (constraint.Sources[index].SourceTransform != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 制約が指定した Transform（またはその配下）をソースに使っているかを調べます。
        ///
        /// 回転制約と位置制約は共通の基底クラスを持つため、1 本で両方を扱えます。
        /// </summary>
        static bool UsesSource(VRCConstraintBase constraint, Transform source)
        {
            for (var index = 0; index < constraint.Sources.Count; index++)
            {
                var candidate = constraint.Sources[index].SourceTransform;
                if (candidate == null)
                {
                    continue;
                }

                if (candidate == source || candidate.IsChildOf(source))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 旧バージョンの Unity の制約のうち、<paramref name="source"/> 配下を参照しているものを外します。
        ///
        /// パッケージが入っていない環境ではその型自体が存在しないため、
        /// 名前が一致するコンポーネントが 1 つも無いという形で何もせずに戻ります。
        /// </summary>
        static void RemoveLegacyConstraintsReferencing(Transform avatarRoot, Transform source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var component in avatarRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                var typeName = component.GetType().FullName;

                if (typeName != LegacyRotationConstraintTypeName && typeName != LegacyPositionConstraintTypeName)
                {
                    continue;
                }

                if (UsesLegacySource(component, source))
                {
                    Object.DestroyImmediate(component);
                }
            }
        }

        /// <summary>
        /// 旧バージョンの Unity の制約が <paramref name="source"/> 配下を参照しているかを調べます。
        ///
        /// sourceCount / GetSource(int) / ConstraintSource.sourceTransform は
        /// パッケージの公開 API ですが、型を直接参照できないので名前で引きます。
        /// 引けなかった場合は「参照していない」とみなします。
        /// </summary>
        static bool UsesLegacySource(Component constraint, Transform source)
        {
            var type = constraint.GetType();

            var sourceCountProperty = type.GetProperty("sourceCount");
            var getSourceMethod = type.GetMethod("GetSource", new[] { typeof(int) });

            if (sourceCountProperty == null || getSourceMethod == null)
            {
                return false;
            }

            var sourceCount = (int)sourceCountProperty.GetValue(constraint, null);

            for (var index = 0; index < sourceCount; index++)
            {
                var element = getSourceMethod.Invoke(constraint, new object[] { index });

                if (element == null)
                {
                    continue;
                }

                // sourceTransform は ConstraintSource の公開フィールドです。
                // 将来プロパティに変わるとここで null になり、
                // 「参照していない」とみなして何もしません。
                var sourceTransformField = element.GetType().GetField("sourceTransform");

                if (sourceTransformField == null)
                {
                    continue;
                }

                var candidate = sourceTransformField.GetValue(element) as Transform;

                if (candidate != null && (candidate == source || candidate.IsChildOf(source)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
