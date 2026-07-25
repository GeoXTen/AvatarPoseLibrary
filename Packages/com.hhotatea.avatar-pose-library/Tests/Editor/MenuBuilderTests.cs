using System.Collections.Generic;
using com.hhotatea.avatar_pose_library.logic;
using com.hhotatea.avatar_pose_library.model;
using NUnit.Framework;
using UnityEngine;

namespace com.hhotatea.avatar_pose_library.tests
{
    public class MenuBuilderTests
    {
        [Test]
        public void BuildPoseMenu_UsesClipNameWhenPoseNameIsBlank()
        {
            var clip = new AnimationClip { name = "Clip Name" };
            var data = new AvatarPoseData
            {
                name = "Library",
                categories = new List<PoseCategory>
                {
                    new PoseCategory
                    {
                        name = "Category",
                        poses = new List<PoseEntry>
                        {
                            new PoseEntry { name = string.Empty, animationClip = clip },
                        },
                    },
                },
            }.UpdateParameter();
            GameObject menu = null;

            try
            {
                menu = MenuBuilder.BuildPoseMenu(data);

                Assert.That(menu.transform.Find("Category/Clip Name"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(menu);
                Object.DestroyImmediate(clip);
            }
        }
    }
}