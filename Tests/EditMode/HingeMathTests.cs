using Heft;
using NUnit.Framework;
using UnityEngine;

namespace Heft.Tests
{
    public class HingeMathTests
    {
        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(-120f)]
        [TestCase(170f)]
        public void AngleAboutReadsATurnAboutTheAxis(float degrees)
        {
            Quaternion rotation = Quaternion.AngleAxis(degrees, Vector3.forward);
            Assert.That(HingeMath.AngleAbout(rotation, Vector3.forward), Is.EqualTo(degrees).Within(0.01f));
        }

        [Test]
        public void AngleAboutIsNegativeAboutTheOppositeAxis()
        {
            Quaternion rotation = Quaternion.AngleAxis(30f, Vector3.up);
            Assert.That(HingeMath.AngleAbout(rotation, Vector3.down), Is.EqualTo(-30f).Within(0.01f));
        }

        [Test]
        public void AngleAboutIgnoresTurnsAboutOtherAxes()
        {
            Quaternion rotation = Quaternion.AngleAxis(60f, Vector3.right);
            Assert.That(HingeMath.AngleAbout(rotation, Vector3.up), Is.EqualTo(0f).Within(0.01f));
        }
    }
}
