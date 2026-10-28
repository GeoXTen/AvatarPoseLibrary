using System.Collections.Generic;
using System.Linq;
using com.hhotatea.avatar_pose_library.logic;
using com.hhotatea.avatar_pose_library.model;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace com.hhotatea.avatar_pose_library.tests
{
    public class BodyRotationTests
    {
        const string LibraryGuid = "BodyRotationTest";

        readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
        }

        GameObject Create(string name, Transform parent = null)
        {
            var gameObject = new GameObject(name);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            objects.Add(gameObject);
            return gameObject;
        }

        /// <summary>
        /// Avatar root -> Armature -> Hips, i.e. the layout the solver expects.
        /// </summary>
        GameObject CreateAvatar(out Transform armature, out Transform hips)
        {
            var root = Create("Avatar");
            root.transform.localPosition = Vector3.zero;

            armature = Create("Armature", root.transform).transform;
            armature.localPosition = new Vector3(0f, 0.05f, 0.02f);
            armature.localRotation = Quaternion.Euler(0f, 12f, 0f);

            hips = Create("Hips", armature).transform;
            hips.localPosition = new Vector3(0f, 0.9f, 0f);

            return root;
        }

        static AvatarPoseData CreateData()
        {
            return new AvatarPoseData
            {
                name = "Body Rotation",
                parameterOverride = LibraryGuid,
            }.UpdateParameter();
        }

        [Test]
        public void TryCreate_ResolvesBindingPathAndPivotFromTheHipsBone()
        {
            var root = CreateAvatar(out var armature, out var hips);

            var created = BodyRotationArmatureSolver.TryCreate(
                root.transform, out var solver, out var reason);

            Assert.That(created, Is.True, reason);
            Assert.That(solver, Is.Not.Null);
            Assert.That(solver.BindingPath, Is.EqualTo("Armature"));
            Assert.That(
                Vector3.Distance(solver.Pivot, root.transform.InverseTransformPoint(hips.position)),
                Is.LessThan(1e-4f));
            Assert.That(solver.Pivot.y, Is.GreaterThan(armature.localPosition.y));
        }

        [Test]
        public void TryCreate_RejectsHipsThatSitDirectlyUnderTheAvatarRoot()
        {
            var root = Create("Avatar");
            Create("Hips", root.transform);

            var created = BodyRotationArmatureSolver.TryCreate(
                root.transform, out var solver, out var reason);

            Assert.That(created, Is.False);
            Assert.That(solver, Is.Null);
            Assert.That(reason, Does.Contain("directly under the avatar root"));
        }

        [Test]
        public void TryCreate_RejectsAvatarsWithoutAHipsBone()
        {
            var root = Create("Avatar");
            Create("Armature", root.transform);

            var created = BodyRotationArmatureSolver.TryCreate(
                root.transform, out var solver, out var reason);

            Assert.That(created, Is.False);
            Assert.That(solver, Is.Null);
            Assert.That(reason, Does.Contain("Hips"));
        }

        [Test]
        public void Solve_KeepsTheRestTransformAtNeutral()
        {
            var root = CreateAvatar(out var armature, out _);
            BodyRotationArmatureSolver.TryCreate(root.transform, out var solver, out _);

            Assert.That(
                Vector3.Distance(solver.SolvePosition(0f, 0f, 0f), armature.localPosition),
                Is.LessThan(1e-5f));
            Assert.That(
                Quaternion.Angle(solver.SolveRotation(0f, 0f, 0f), armature.localRotation),
                Is.LessThan(1e-4f));
        }

        [Test]
        public void Solve_RotatesTheArmatureAroundTheHipsPivot()
        {
            var root = CreateAvatar(out var armature, out _);
            BodyRotationArmatureSolver.TryCreate(root.transform, out var solver, out _);

            var rest = armature.localPosition;
            var pivot = solver.Pivot;
            var rotation = Quaternion.Euler(0f, 90f, 0f);
            var expected = pivot + rotation * (rest - pivot);

            var position = solver.SolvePosition(0f, 90f, 0f);

            Assert.That(Vector3.Distance(position, expected), Is.LessThan(1e-5f));
            // The distance to the pivot never changes, so the body turns in place.
            Assert.That(
                Vector3.Distance(position, pivot),
                Is.EqualTo(Vector3.Distance(rest, pivot)).Within(1e-5f));
            Assert.That(
                Quaternion.Angle(solver.SolveRotation(0f, 90f, 0f), rotation * armature.localRotation),
                Is.LessThan(1e-4f));
        }

        [Test]
        public void BuildAnimator_CreatesNeutralAxisParametersAndOneOverrideLayer()
        {
            var root = CreateAvatar(out _, out _);
            BodyRotationArmatureSolver.TryCreate(root.transform, out var solver, out _);

            var controller = BodyRotationRigBuilder.BuildAnimator(CreateData(), solver);

            try
            {
                var expected = ConstVariables.BodyRotationAxes;
                var floats = controller.parameters
                    .Where(parameter => parameter.type == AnimatorControllerParameterType.Float)
                    .ToArray();

                Assert.That(floats.Length, Is.EqualTo(expected.Count()));
                foreach (var axis in expected)
                {
                    var parameter = floats.SingleOrDefault(
                        candidate => candidate.name == $"{axis.ParameterPrefix}_{LibraryGuid}");
                    Assert.That(parameter, Is.Not.Null, axis.ParameterPrefix);
                    Assert.That(parameter.defaultFloat, Is.EqualTo(axis.NeutralValue).Within(1e-5f));
                }

                Assert.That(controller.layers.Length, Is.EqualTo(1));
                var layer = controller.layers[0];
                Assert.That(
                    layer.name,
                    Is.EqualTo($"{ConstVariables.BodyRotationRigLayerPrefix}_{LibraryGuid}"));
                Assert.That(layer.defaultWeight, Is.EqualTo(1f));
                Assert.That(layer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));

                var state = layer.stateMachine.defaultState;
                Assert.That(state, Is.Not.Null);
                Assert.That(state.name, Is.EqualTo("Rotate"));
                // The rig must not use Write Defaults, otherwise the pose layers win.
                Assert.That(state.writeDefaultValues, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [Test]
        public void BuildAnimator_BuildsAFiveStepGridWithConstantTransformCurves()
        {
            var root = CreateAvatar(out var armature, out _);
            BodyRotationArmatureSolver.TryCreate(root.transform, out var solver, out _);

            var controller = BodyRotationRigBuilder.BuildAnimator(CreateData(), solver);

            try
            {
                var state = controller.layers[0].stateMachine.defaultState;
                var xTree = state.motion as BlendTree;

                Assert.That(xTree, Is.Not.Null);
                Assert.That(xTree.blendType, Is.EqualTo(BlendTreeType.Simple1D));
                Assert.That(
                    xTree.blendParameter,
                    Is.EqualTo($"{ConstVariables.BodyRotationAxisX.ParameterPrefix}_{LibraryGuid}"));
                Assert.That(xTree.children.Length, Is.EqualTo(ConstVariables.BodyRotationSteps));

                var clips = LeafClips(xTree).ToArray();
                var steps = ConstVariables.BodyRotationSteps;
                Assert.That(clips.Length, Is.EqualTo(steps * steps * steps));

                // The neutral corner has to reproduce the rest transform.
                var neutral = clips.Single(clip => clip.name == "AnimPoseBodyRotate_0_0_0");
                var bindings = AnimationUtility.GetCurveBindings(neutral);
                Assert.That(bindings.Length, Is.EqualTo(7));
                Assert.That(bindings.All(binding => binding.path == "Armature"), Is.True);

                var restPosition = armature.localPosition;
                var restPositionX = AnimationUtility.GetEditorCurve(
                    neutral,
                    Bindings(neutral, "m_LocalPosition").Single(
                        binding => binding.propertyName == "m_LocalPosition.x"));
                Assert.That(
                    restPositionX.keys[0].value,
                    Is.EqualTo(restPosition.x).Within(1e-4f));

                // The far corner is a quarter/half/full turn depending on the axis range.
                var rotated = clips.Single(clip => clip.name == "AnimPoseBodyRotate_360_0_0");
                var expected = solver.SolvePosition(360f, 0f, 0f);
                var positionCurves = Bindings(rotated, "m_LocalPosition").OrderBy(binding => binding.propertyName).ToArray();
                Assert.That(positionCurves.Length, Is.EqualTo(3));
                foreach (var binding in positionCurves)
                {
                    var value = AnimationUtility.GetEditorCurve(rotated, binding).keys[0].value;
                    var expectedValue =
                        binding.propertyName.EndsWith(".x") ? expected.x :
                        binding.propertyName.EndsWith(".y") ? expected.y : expected.z;
                    Assert.That(value, Is.EqualTo(expectedValue).Within(1e-4f), binding.propertyName);
                }
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        static IEnumerable<AnimationClip> LeafClips(BlendTree tree)
        {
            foreach (var child in tree.children)
            {
                if (child.motion is BlendTree nested)
                {
                    foreach (var clip in LeafClips(nested))
                    {
                        yield return clip;
                    }
                }
                else if (child.motion is AnimationClip clip)
                {
                    yield return clip;
                }
            }
        }

        static EditorCurveBinding[] Bindings(AnimationClip clip, string propertyPrefix)
        {
            return AnimationUtility.GetCurveBindings(clip)
                .Where(binding => binding.propertyName.StartsWith(propertyPrefix))
                .ToArray();
        }

        [Test]
        public void CleanupLegacyRig_RemovesTheConstraintRigObject()
        {
            var root = CreateAvatar(out _, out _);
            var legacy = Create(ConstVariables.BodyRotationRigRootName, root.transform);

            BodyRotationRigBuilder.CleanupLegacyRig(root);

            Assert.That(legacy == null, Is.True);
            Assert.That(root.transform.Find(ConstVariables.BodyRotationRigRootName), Is.Null);
        }

        [Test]
        public void BuildAnimator_ReturnsNullWhenTheSolverIsMissing()
        {
            Assert.That(BodyRotationRigBuilder.BuildAnimator(CreateData(), null), Is.Null);
            Assert.That(BodyRotationRigBuilder.BuildAnimator(null, null), Is.Null);
        }
    }
}
