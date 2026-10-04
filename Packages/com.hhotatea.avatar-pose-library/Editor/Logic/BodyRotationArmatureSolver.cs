using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.hhotatea.avatar_pose_library.logic
{
    /// <summary>
    /// 体全体の回転を「アーマチュアの Transform カーブ」だけで表現するための計算役。
    ///
    /// 【なぜ制約を使わないのか】
    /// 以前はアバタールート直下に Transform の鎖を作り、
    /// VRCRotationConstraint / VRCPositionConstraint でアーマチュアを追従させていました。
    /// ところが VRChat の制約コンポーネントは VRChat のクライアントが解くもので、
    /// Unity のプレイモードでは 1 ミリも動きません。
    /// そのため「アップロードするまで動作を確認できない」うえ、
    /// プレイモードで試すと鎖だけが回って体が付いてこない、
    /// という原因の分からない症状になっていました。
    ///
    /// 【この方式】
    /// アーマチュア自身の localPosition / localRotation を直接アニメーションで書きます。
    /// 制約を使わないので Unity のプレイモードでも VRChat でも同じように動きます。
    /// 親子関係は一切変えないため、ボーンのバインディングパスも変わりません。
    ///
    /// 【支点の扱い】
    /// アーマチュアの原点は通常キャラクターの足元にあります。
    /// そのまま回すと体全体が弧を描いて移動してしまうため、
    /// Hips（腰）を支点にした剛体回転になるよう localPosition も同時に補正します。
    ///
    /// アーマチュアの親（＝アバタールート）のローカル空間で、
    ///   a : アーマチュアの localPosition（無回転時）
    ///   q : アーマチュアの localRotation（無回転時）
    ///   h : Hips の位置
    ///   R : 加えたい回転
    /// とすると、Hips を固定したままアーマチュアを R だけ回す値は
    ///   localPosition = h + R * (a - h)
    ///   localRotation = R * q
    /// になります（Hips の位置 a + q * p が h に留まることを展開すれば確かめられます）。
    /// </summary>
    public sealed class BodyRotationArmatureSolver
    {
        readonly Vector3 restLocalPosition_;
        readonly Quaternion restLocalRotation_;
        readonly Vector3 pivot_;

        BodyRotationArmatureSolver(
            string bindingPath,
            Vector3 restLocalPosition,
            Quaternion restLocalRotation,
            Vector3 pivot)
        {
            BindingPath = bindingPath;
            restLocalPosition_ = restLocalPosition;
            restLocalRotation_ = restLocalRotation;
            pivot_ = pivot;
        }

        /// <summary>
        /// 回転を書き込む Transform の、アバタールートからのバインディングパス。
        /// 通常は "Armature" です。
        /// </summary>
        public string BindingPath { get; }

        /// <summary>回転の支点（アバタールートのローカル空間）。</summary>
        public Vector3 Pivot => pivot_;

        /// <summary>
        /// アバターから回転の支点と基準姿勢を読み取ります。
        /// アーマチュアや Hips が見つからない場合は false を返し、理由を <paramref name="reason"/> に入れます。
        /// </summary>
        public static bool TryCreate(
            Transform avatarRoot,
            out BodyRotationArmatureSolver solver,
            out string reason)
        {
            solver = null;

            if (avatarRoot == null)
            {
                reason = "The avatar root is missing.";
                return false;
            }

            var armature = FindArmatureRoot(avatarRoot);
            if (armature == null)
            {
                reason =
                    "No armature root was found. Body rotation needs a bone named 'Hips' " +
                    "somewhere under the avatar root.";
                return false;
            }

            var path = RelativePath(avatarRoot, armature);
            if (path == null)
            {
                reason = $"'{armature.name}' is not inside the avatar root.";
                return false;
            }

            var hips = FindHips(avatarRoot);
            if (hips == null)
            {
                reason =
                    "No Hips bone was found. Body rotation needs a humanoid Animator or a " +
                    "bone named 'Hips' to use as the pivot.";
                return false;
            }

            if (armature == hips)
            {
                // Hips がアバタールート直下にある場合、アーマチュアと Hips が同じ Transform です。
                // そこへ回転を書くと、ポーズ側が RootT / RootQ で書いている Hips の値を
                // 毎フレーム上書きしてしまい、ポーズ自体が壊れます。
                // 回転できないだけで済ませるため、ここで諦めます。
                reason =
                    "'" + hips.name + "' is directly under the avatar root, so it is also the " +
                    "bone the pose animates. Rotating it would overwrite the pose. " +
                    "Add an armature object between the avatar root and the hips.";
                return false;
            }

            // 支点は Hips です。アーマチュアの原点（足元）を支点にすると、
            // 傾けたときに体全体が弧を描いて大きく移動してしまいます。
            var pivot = avatarRoot.InverseTransformPoint(hips.position);

            solver = new BodyRotationArmatureSolver(
                path,
                armature.localPosition,
                armature.localRotation,
                pivot);
            reason = null;
            return true;
        }

        /// <summary>
        /// 指定した角度だけ体を回したときの、アーマチュアの localPosition。
        /// </summary>
        public Vector3 SolvePosition(float xDegrees, float yDegrees, float zDegrees)
        {
            var rotation = Quaternion.Euler(xDegrees, yDegrees, zDegrees);
            return pivot_ + rotation * (restLocalPosition_ - pivot_);
        }

        /// <summary>
        /// 指定した角度だけ体を回したときの、アーマチュアの localRotation。
        /// </summary>
        public Quaternion SolveRotation(float xDegrees, float yDegrees, float zDegrees)
        {
            // 回転はアバタールートの軸で与えます（Quaternion.Euler は Ry * Rx * Rz）。
            return Quaternion.Euler(xDegrees, yDegrees, zDegrees) * restLocalRotation_;
        }

        /// <summary>
        /// Hips を含む、アバタールート直下の Transform を返します。通常は Armature オブジェクトです。
        /// </summary>
        public static Transform FindArmatureRoot(Transform avatarRoot)
        {
            var hips = FindHips(avatarRoot);
            if (hips == null)
            {
                return null;
            }

            // Hips から親をたどり、アバタールート直下の Transform を探します。
            var current = hips;
            while (current.parent != null && current.parent != avatarRoot)
            {
                current = current.parent;
            }

            return current.parent == avatarRoot ? current : null;
        }

        /// <summary>
        /// Hips ボーンを探します。Animator から取得できない場合は名前で探索します。
        /// </summary>
        public static Transform FindHips(Transform avatarRoot)
        {
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                var bone = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (bone != null)
                {
                    return bone;
                }
            }

            foreach (var child in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child == avatarRoot)
                {
                    continue;
                }

                if (string.Equals(child.name, "Hips", StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// アバタールートから対象までの相対パスを返します。到達できなければ null。
        /// </summary>
        public static string RelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return null;
            }

            if (target == root)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != root)
            {
                return null;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
