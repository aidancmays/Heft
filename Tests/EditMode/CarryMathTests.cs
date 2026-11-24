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

        [Test]
        public void Leverage_LightThingsAloneArePulledByTheMiddle()
        {
            Assert.AreEqual(0f, CarryMath.Leverage(0f, 1));
            Assert.AreEqual(0f, CarryMath.Leverage(0.05f, 1)); // a bottle
            Assert.AreEqual(0f, CarryMath.Leverage(0.5f, 1));
        }

        [Test]
        public void Leverage_HeavyThingsAndSharedLoadsPullFromTheGrip()
        {
            Assert.AreEqual(0.5f, CarryMath.Leverage(0.75f, 1), 1e-5f);
            Assert.AreEqual(1f, CarryMath.Leverage(1f, 1));
            Assert.AreEqual(1f, CarryMath.Leverage(3f, 1));
            Assert.AreEqual(1f, CarryMath.Leverage(float.PositiveInfinity, 1));
            Assert.AreEqual(1f, CarryMath.Leverage(0.05f, 2));
        }

        [Test]
        public void TurnHold_DefaultIsNoTurn()
        {
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, CarryMath.Normalized(default)), 1e-4f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, CarryMath.TurnHold(default, 0f, 0f)), 1e-4f);
        }

        [Test]
        public void TurnHold_YawTurnsAboutUp_PitchTipsTheFarSideUp()
        {
            Quaternion yawed = CarryMath.TurnHold(Quaternion.identity, 90f, 0f);
            Vector3 forward = yawed * Vector3.forward;
            Assert.AreEqual(1f, forward.x, 1e-4f, "yaw +90 turns forward to the right");

            Quaternion pitched = CarryMath.TurnHold(Quaternion.identity, 0f, 30f);
            Vector3 far = pitched * Vector3.forward;
            Assert.Greater(far.y, 0.4f, "positive pitch tips the far side up");
        }

        [Test]
        public void TurnHold_StaysInTheHoldersFrame()
        {
            // Already turned 90 about up: pitching still tips about the holder's right, not the object's.
            Quaternion turn = CarryMath.TurnHold(Quaternion.identity, 90f, 0f);
            turn = CarryMath.TurnHold(turn, 0f, 90f);
            Vector3 objectForward = turn * Vector3.forward; // was pointing right; about the holder's right axis it stays right
            Assert.AreEqual(1f, objectForward.x, 1e-4f);
            Vector3 objectUp = turn * Vector3.up; // up tips back toward the holder
            Assert.AreEqual(-1f, objectUp.z, 1e-4f);
        }

        [Test]
        public void TurnHold_StaysUnitLengthOverManySteps()
        {
            Quaternion turn = Quaternion.identity;
            for (int i = 0; i < 2000; i++) turn = CarryMath.TurnHold(turn, 1.3f, -0.7f);
            float length = Mathf.Sqrt(turn.x * turn.x + turn.y * turn.y + turn.z * turn.z + turn.w * turn.w);
            Assert.AreEqual(1f, length, 1e-4f);
        }

        [Test]
        public void HoldTarget_NoTurnKeepsTheGrabbedFacing()
        {
            Quaternion facing = Quaternion.Euler(0f, 40f, 0f);
            Quaternion relative = Quaternion.Euler(10f, 20f, 30f);
            Assert.AreEqual(0f, Quaternion.Angle(facing * relative, CarryMath.HoldTarget(facing, default, relative)), 1e-3f);
            Quaternion turned = CarryMath.HoldTarget(facing, CarryMath.TurnHold(default, 90f, 0f), relative);
            Assert.AreEqual(90f, Quaternion.Angle(facing * relative, turned), 1e-2f);
        }

        [Test]
        public void Leverage_StepWhenThresholdsMeet()
        {
            Assert.AreEqual(0f, CarryMath.Leverage(0.4f, 1, 0.5f, 0.5f));
            Assert.AreEqual(1f, CarryMath.Leverage(0.5f, 1, 0.5f, 0.5f));
        }
    }
}
