using Godot;
using System;
using Godot.NativeInterop;

public partial class clothCollider : Node3D
{
	[Export] private float radius = 0.5f;
	enum Shape {Sphere, Box}

	[Export] private Shape shape = Shape.Sphere;
	[Export] private Vector3 extents = new Vector3(0.5f, 0.5f, 0.5f);
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public float[] PackColliderData(Transform3D clothinv)
	{
		var data = new float[16];
		var xform = GlobalTransform;
		var center = clothinv * xform.Origin;

		if (shape == Shape.Box)
		{
			data[0] = center.X;
			data[1] = center.Y;
			data[2] = center.Z;
			data[3] = 0.0f;
			data[4] = extents.X;
			data[5] = extents.Y;
			data[6] = extents.Z;
			data[7] = 1.0f;
			var right = (clothinv.Basis * xform.Basis * Vector3.Right).Normalized();
			data[8] = right.X;
			data[9] = right.Y;
			data[10] = right.Z;
			data[11] = 0.0f;
			var up = (clothinv.Basis * xform.Basis * Vector3.Up).Normalized();
			data[12] = up.X;
			data[13] = up.Y;
			data[14] = up.Z;
			data[15] = 0.0f;
		}
		else
		{
			data[0] = center.X;
			data[1] = center.Y;
			data[2] = center.Z;
			data[3] = radius;
			data[4] = center.X;
			data[5] = center.Y;
			data[6] = center.Z;
			data[7] = 0.0f;
		}
		return data;
	}
}
