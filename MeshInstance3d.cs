using Godot;
using System;

public partial class MeshInstance3d : MeshInstance3D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	private float time = 0;
	public override void _Process(double delta)
	{
		time += (float)delta;
		Vector3 scale = new Vector3(1.0f + MathF.Cos( time * 10f), 1,  1 );
		this.Scale = scale;
	}

}
