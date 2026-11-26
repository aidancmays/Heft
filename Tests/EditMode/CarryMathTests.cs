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
        public void ThrowSpeed_MassFloorTamesLightThings()
        {
            Assert.AreEqual(5f, CarryMath.ThrowSpeed(15f, 1f, 12f, 2f), 1e-5f);
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
        public void Strain_RisesWithStretchThenWithTheSlipTimer()
        {
            Assert.AreEqual(0f, CarryMath.Strain(0f, 1.75f, 0f, 0.3f), 1e-5f);
            Assert.AreEqual(0.4f, CarryMath.Strain(0.875f, 1.75f, 0f, 0.3f), 1e-5f);
            Assert.AreEqual(0.8f, CarryMath.Strain(1.75f, 1.75f, 0f, 0.3f), 1e-5f);
            Assert.AreEqual(0.9f, CarryMath.Strain(2f, 1.75f, 0.15f, 0.3f), 1e-5f);
            Assert.AreEqual(1f, CarryMath.Strain(2f, 1.75f, 0.3f, 0.3f), 1e-5f);
            Assert.AreEqual(1f, CarryMath.Strain(2f, 1.75f, 9f, 0.3f), 1e-5f);
        }

        [Test]
        public void Strain_EdgeCases()
        {
            Assert.AreEqual(1f, CarryMath.Strain(0.1f, 0f, 0f, 0.3f), "no break distance: always at the limit");
            Assert.AreEqual(1f, CarryMath.Strain(2f, 1f, 0f, 0f), "no slip time: past the distance is letting go");
            Assert.AreEqual(0.5f, CarryMath.Strain(1f, 1f, 0f, 0.3f, 0.5f), 1e-5f);
        }

        [Test]
        public void Curve_PassesThroughItsEnds()
        {
            Vector3 a = new Vector3(1f, 2f, 3f), c = new Vector3(0f, 5f, 0f), b = new Vector3(-4f, 0f, 2f);
            Assert.AreEqual(0f, Vector3.Distance(a, CarryMath.Curve(a, c, b, 0f)), 1e-5f);
            Assert.AreEqual(0f, Vector3.Distance(b, CarryMath.Curve(a, c, b, 1f)), 1e-5f);
            Vector3 mid = CarryMath.Curve(a, c, b, 0.5f);
            Assert.AreEqual(0f, Vector3.Distance(0.25f * a + 0.5f * c + 0.25f * b, mid), 1e-5f);
        }

        [Test]
        public void TetherControl_BendsTowardThePull_SagsWhenSlack_StraightensUnderStrain()
        {
            Vector3 from = Vector3.zero, to = new Vector3(0f, 0f, 2f), pull = new Vector3(1f, 0f, 1f);
            Vector3 slack = CarryMath.TetherControl(from, to, pull, 0.5f, 0.1f, 0f);
            Vector3 taut = CarryMath.TetherControl(from, to, pull, 0.5f, 0.1f, 1f);
            Assert.AreEqual(0.5f, taut.x, 1e-5f, "half way from the middle to the pull");
            Assert.AreEqual(0f, taut.y, 1e-5f, "no sag at full strain");
            Assert.AreEqual(-0.2f, slack.y, 1e-5f, "sags a tenth of its 2 m length");
            Vector3 straight = CarryMath.TetherControl(from, to, pull, 0f, 0f, 0f);
            Assert.AreEqual(0f, Vector3.Distance(new Vector3(0f, 0f, 1f), straight), 1e-5f);
        }

        [Test]
        public void Leverage_StepWhenThresholdsMeet()
        {
            Assert.AreEqual(0f, CarryMath.Leverage(0.4f, 1, 0.5f, 0.5f));
            Assert.AreEqual(1f, CarryMath.Leverage(0.5f, 1, 0.5f, 0.5f));
        }
    }
}
