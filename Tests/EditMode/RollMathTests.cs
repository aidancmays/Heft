using Heft;
using NUnit.Framework;

namespace Heft.Tests
{
    public class RollMathTests
    {
        const float Dt = 0.02f;

        [Test]
        public void SideForceCancelsASmallSlideInOneStep()
        {
            // 10 kg on the wheel sliding 0.1 m/s sideways, full grip: 10 * 0.1 / 0.02 = 50 N back.
            float force = RollMath.SideForce(0.1f, 10f, 98.1f, 1f, 1.2f, Dt);
            Assert.That(force, Is.EqualTo(-50f).Within(0.01f));
        }

        [Test]
        public void SideForceIsCappedByFrictionSoItSkids()
        {
            float force = RollMath.SideForce(5f, 10f, 98.1f, 1f, 1.2f, Dt);
            Assert.That(force, Is.EqualTo(-1.2f * 98.1f).Within(0.01f));
        }

        [Test]
        public void RollingForceOpposesRollingAndIsSmall()
        {
            float force = RollMath.RollingForce(2f, 10f, 98.1f, 0.03f, Dt);
            Assert.That(force, Is.EqualTo(-0.03f * 98.1f).Within(0.001f));
        }

        [Test]
        public void RollingForceNeverPushesItBackwardsPastStill()
        {
            // Barely moving: the force only stops it, it doesn't reverse it.
            float force = RollMath.RollingForce(0.001f, 10f, 98.1f, 0.03f, Dt);
            Assert.That(force, Is.EqualTo(-0.001f * 10f / Dt).Within(1e-4f));
            Assert.That(RollMath.RollingForce(0f, 10f, 98.1f, 0.03f, Dt), Is.EqualTo(0f));
        }

        [Test]
        public void PushingAcrossIsFarHarderThanPushingAlong()
        {
            Assert.That(RollMath.Anisotropy(1.2f, 0.03f), Is.GreaterThan(20f));
        }
    }
}
