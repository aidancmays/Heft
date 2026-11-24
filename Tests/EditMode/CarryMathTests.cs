using NUnit.Framework;
using UnityEngine;

namespace Heft.Tests
{
    public class CarryMathTests
    {
        static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        [Test]
        public void AtRestOnTarget_HoldsUpItsShareOfTheWeight()
        {
            Vector3 force = CarryMath.GripForce(Vector3.zero, Vector3.zero, 10f, 0.5f, Gravity, 150f, 20f, 1000f);
            Assert.AreEqual(9.81f * 5f, force.y, 1e-3f);
            Assert.AreEqual(0f, force.x, 1e-6f);
        }

        [Test]
        public void ForceIsCappedByStrength()
        {
            Vector3 force = CarryMath.GripForce(Vector3.up * 2f, Vector3.zero, 50f, 1f, Gravity, 150f, 20f, 120f);
            Assert.AreEqual(120f, force.magnitude, 1e-3f);
        }

        [Test]
        public void DampingOpposesRelativeVelocity()
        {
            Vector3 force = CarryMath.GripForce(Vector3.zero, Vector3.right * 2f, 1f, 1f, Vector3.zero, 150f, 20f, 1000f);
            Assert.Less(force.x, 0f);
        }

        [Test]
        public void Load_TooHeavyAloneButLiftableTogether()
        {
            float maxForce = 150f; // strength 10 at 15 N per point
            Assert.Greater(CarryMath.Load(20f, 1f, 9.81f, maxForce), 1f);
            Assert.Less(CarryMath.Load(20f, 0.5f, 9.81f, maxForce), 1f);
        }

        [Test]
        public void Load_ZeroWithoutGravity_InfiniteWithoutStrength()
        {
            Assert.AreEqual(0f, CarryMath.Load(20f, 1f, 0f, 150f));
            Assert.IsTrue(float.IsPositiveInfinity(CarryMath.Load(20f, 1f, 9.81f, 0f)));
        }

        [Test]
        public void ThrowSpeed_LighterGoesFaster_UpToTheCap()
        {
            Assert.AreEqual(3f, CarryMath.ThrowSpeed(15f, 5f, 12f), 1e-5f);
            Assert.AreEqual(12f, CarryMath.ThrowSpeed(15f, 0.5f, 12f), 1e-5f);
        }
    }
}
