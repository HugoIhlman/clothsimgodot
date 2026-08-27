using Godot;
using System;
using Godot.NativeInterop;

public partial class clothCollider : Node3D
{
	[Export] private float radius = 0.5f;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public float[] PackColliderData(Transform3D clothinv)
	{
		var data = new float[16];
		var xform = GlobalTransform;
		var center = clothinv * xform.Origin;

		data[0] = center.X;
		data[1] = center.Y;
		data[2] = center.Z;
		data[3] = radius;
		data[4] = center.X;
		data[5] = center.Y;
		data[6] = center.Z;
		data[7] = 0.0f;
		
		return data;
	}
}
