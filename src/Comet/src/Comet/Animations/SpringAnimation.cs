using System;
using Microsoft.Maui;
using Microsoft.Maui.Animations;

namespace Comet
{
	/// <summary>
	/// Preset spring configurations for common animation styles.
	/// </summary>
	public enum SpringPreset
	{
		/// <summary>High energy with visible overshoot. mass=1, stiffness=170, damping=12</summary>
		Bouncy,
		/// <summary>Balanced feel with minimal overshoot. mass=1, stiffness=100, damping=20</summary>
		Smooth,
		/// <summary>Fast and responsive with no overshoot. mass=1, stiffness=300, damping=30</summary>
		Stiff,
		/// <summary>Slow and soft motion. mass=1, stiffness=80, damping=15</summary>
		Gentle
	}

	/// <summary>
	/// Spring-based animation using a mass-spring-damper model with RK4 integration.
	/// Provides natural, physics-driven motion with configurable bounce and settling behavior.
	/// </summary>
	public class SpringAnimation : ContextualAnimation
	{
		private double _velocity;
		private double _position;
		private bool _started;

		private const double VelocityThreshold = 0.001;
		private const double DisplacementThreshold = 0.001;
		private const double MaxDeltaTime = 0.064;

		/// <summary>Mass of the spring object. Higher values create slower, heavier motion.</summary>
		public double Mass { get; set; } = 1.0;

		/// <summary>Spring stiffness. Higher values create faster, snappier motion.</summary>
		public double Stiffness { get; set; } = 100.0;

		/// <summary>Damping coefficient. Higher values reduce oscillation.</summary>
		public double DampingCoefficient { get; set; } = 10.0;

		/// <summary>Initial velocity of the spring, useful for gesture-driven animations.</summary>
		public double InitialVelocity { get; set; }

		public SpringAnimation()
		{
		}

		public SpringAnimation(SpringPreset preset)
		{
			ApplyPreset(preset);
		}

		public SpringAnimation(double mass, double stiffness, double damping)
		{
			Mass = mass;
			Stiffness = stiffness;
			DampingCoefficient = damping;
		}

		/// <summary>
		/// Configures the spring parameters from a preset.
		/// </summary>
		public void ApplyPreset(SpringPreset preset)
		{
			switch (preset)
			{
				case SpringPreset.Bouncy:
					Mass = 1; Stiffness = 170; DampingCoefficient = 12;
					break;
				case SpringPreset.Smooth:
					Mass = 1; Stiffness = 100; DampingCoefficient = 20;
					break;
				case SpringPreset.Stiff:
					Mass = 1; Stiffness = 300; DampingCoefficient = 30;
					break;
				case SpringPreset.Gentle:
					Mass = 1; Stiffness = 80; DampingCoefficient = 15;
					break;
			}
		}

		/// <summary>
		/// Estimates the settling duration based on spring parameters.
		/// Uses the damping ratio and natural frequency to approximate when oscillation becomes negligible.
		/// </summary>
		public double EstimateDuration()
		{
			var omega = Math.Sqrt(Stiffness / Mass);
			var zeta = DampingCoefficient / (2.0 * Math.Sqrt(Stiffness * Mass));
			if (zeta > 0 && omega > 0)
				return Math.Min(4.0 / (zeta * omega), 10.0);
			return 3.0;
		}

		protected override void OnTick(double secondsSinceLastUpdate)
		{
			if (!_started)
			{
				_started = true;
				_position = 0.0;
				_velocity = InitialVelocity;
				Duration = EstimateDuration();
			}

			var dt = Math.Min(secondsSinceLastUpdate, MaxDeltaTime);

			// RK4 integration for numerical stability
			var (newPos, newVel) = IntegrateRK4(_position, _velocity, dt);
			_position = newPos;
			_velocity = newVel;

			Update(_position);

			var displacement = _position - 1.0;
			if (Math.Abs(_velocity) < VelocityThreshold && Math.Abs(displacement) < DisplacementThreshold)
			{
				Update(1.0);
				HasFinished = true;
			}
		}

		private (double position, double velocity) IntegrateRK4(double pos, double vel, double dt)
		{
			var (k1p, k1v) = Derivatives(pos, vel);
			var (k2p, k2v) = Derivatives(pos + k1p * dt * 0.5, vel + k1v * dt * 0.5);
			var (k3p, k3v) = Derivatives(pos + k2p * dt * 0.5, vel + k2v * dt * 0.5);
			var (k4p, k4v) = Derivatives(pos + k3p * dt, vel + k3v * dt);

			var newPos = pos + (dt / 6.0) * (k1p + 2.0 * k2p + 2.0 * k3p + k4p);
			var newVel = vel + (dt / 6.0) * (k1v + 2.0 * k2v + 2.0 * k3v + k4v);
			return (newPos, newVel);
		}

		private (double dPosition, double dVelocity) Derivatives(double pos, double vel)
		{
			// force = -stiffness * displacement - damping * velocity
			var displacement = pos - 1.0;
			var force = -Stiffness * displacement - DampingCoefficient * vel;
			return (vel, force / Mass);
		}

		protected override Animation CreateReverse() => new SpringAnimation
		{
			Mass = Mass,
			Stiffness = Stiffness,
			DampingCoefficient = DampingCoefficient,
			StartDelay = StartDelay + Duration,
			StartValue = EndValue,
			EndValue = StartValue,
			ContextualObject = ContextualObject,
			PropertyCascades = PropertyCascades,
			PropertyName = PropertyName,
			Lerp = Lerp,
		};
	}
}
