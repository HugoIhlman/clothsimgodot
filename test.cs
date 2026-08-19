using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot.Collections;
using Godot.NativeInterop;
using Array = Godot.Collections.Array;

public partial class test : MeshInstance3D
{
	// Called when the node enters the scene tree for the first time.
	[Export] public int width = 32;
	[Export] public int height = 32;
	[Export] public float restDistance = 0.2f;
	
	private Array vertices;
	private Vector3 currentVert;
	private Vector3 prevVert;
	
	private RenderingDevice rd;
	private Rid shader;
	private Rid vertbuffer;
	private Rid uniformset;
	private Rid pipeline;

	struct GpuVertex
	{
		public Vector4 Position;
		public Vector4 PrevPosition;
		public Vector4 Normal;
		public Vector4 Flags;
	}
	
	public override void _Ready()
	{
		rd = RenderingServer.CreateLocalRenderingDevice();
		var shaderfile = GD.Load<RDShaderFile>("res://compute.glsl");
		var shaderbytecode = shaderfile.GetSpirV();
		shader = rd.ShaderCreateFromSpirV(shaderbytecode);
		MeshDataTool mdt = new MeshDataTool();
		ArrayMesh mesh = this.Mesh as ArrayMesh;
		if (mesh != null)
		{
			var surfaceArray = createSurfaceArray();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
		}
		mdt.CreateFromSurface(mesh, 0);
		
		
		var GpuVerticies = new GpuVertex[mdt.GetVertexCount()];
		for (int j = 0; j < mdt.GetVertexCount() ; j ++)
		{
			var position = mdt.GetVertex(j);
			var normal = mdt.GetVertexNormal(j);
			GpuVerticies[j].Position = new Vector4(position.X, position.Y, position.Z, 1f);
			GpuVerticies[j].Normal = new Vector4(normal.X, normal.Y, normal.Z, 1f);
			GpuVerticies[j].PrevPosition =  new Vector4(position.X, position.Y, position.Z, 1f);
			if (position.Z == 0)
			{
				GpuVerticies[j].Flags = new Vector4(1,0,0,1);
			}
			else
			{
				GpuVerticies[j].Flags = new Vector4(0,0,0,1);
			}
		}

		
		

		byte[] inputbytes = MemoryMarshal.AsBytes(GpuVerticies.AsSpan()).ToArray();
		vertbuffer = rd.StorageBufferCreate((uint)inputbytes.Length, inputbytes);
		var vuniform = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0
		};
		
		vuniform.AddId(vertbuffer);
		
		uniformset = rd.UniformSetCreate([vuniform], shader, 0);
		pipeline = rd.ComputePipelineCreate(shader);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		float[] pushConstants =
		{
			(float)width,
			(float)height,
			(float) delta,
			restDistance
		};
		
		byte[] pushConstantsBytes = MemoryMarshal.AsBytes(pushConstants.AsSpan()).ToArray();
		
		var computelist = rd.ComputeListBegin();
		rd.ComputeListBindComputePipeline(computelist, pipeline);
		rd.ComputeListBindUniformSet(computelist, uniformset,0);
		rd.ComputeListSetPushConstant(computelist, pushConstantsBytes, (uint)pushConstantsBytes.Length);
		rd.ComputeListDispatch(computelist, 2,2,1);
		rd.ComputeListEnd();
		rd.Submit();
		rd.Sync();
		var outputbytes = rd.BufferGetData(vertbuffer);
		GpuVertex[] updatedVertices = MemoryMarshal.Cast<byte, GpuVertex>(outputbytes).ToArray();
		updateGeometry(updatedVertices);
		
	}

	private Array createSurfaceArray()
	{
		Godot.Collections.Array surfaceArray = [];
		surfaceArray.Resize((int)Mesh.ArrayType.Max);

		List<Vector3> verts = [];
		List<Vector2> uvs = [];
		List<Vector3> normals = [];
		List<int> indices = [];
		float side = width / 8;
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				int index = y * (8 + 1) + x;
				Vector3 pos = new Vector3(x * side, 0, y * - side);
				verts.Add(pos);
				int tl = index;
				int tr = index + 1;
				int bl = index + (8 + 1) + 1;
				int br = index + (8 + 1);
				indices.AddRange(new int[]{tl,bl,br,tl,tr,bl});
				normals.Add(Vector3.Up);
			}
		}
		surfaceArray[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		
		return surfaceArray;
	}

	private Vector4 calculateNormals(Vector3 pos1, Vector3 pos2)
	{
		Vector3 normal = pos1.Cross(pos2).Normalized();
		return new Vector4(normal.X, normal.Y, normal.Z, 1f);
	}

	private void updateGeometry(GpuVertex[] vertices)
	{
		var arraymesh = new ArrayMesh();
		var surfacearray = new Array();
		surfacearray.Resize((int)Mesh.ArrayType.Max);
		Vector3[] vertarray = new Vector3[vertices.Length];
		Vector3[] normalarray = new Vector3[vertices.Length];
		List<int> indices = [];
		for (int i = 0; i < vertarray.Length; i++)
		{
			vertarray[i] = new Vector3(vertices[i].Position.X, vertices[i].Position.Y, vertices[i].Position.Z);
			normalarray[i] = new Vector3(vertices[i].Normal.X, vertices[i].Normal.Y, vertices[i].Normal.Z);
		}
		for (int y = 0; y < height - 1; y++)
		{
			for (int x = 0; x < width - 1; x++)
			{
				int index = y * width + x;
				int tl = index;
				int tr = tl +1;
				int bl = (y + 1) * width + x;
				int br = bl + 1;
				indices.AddRange(new int[]{tl,bl,tr,tr,bl,br});
			}
		}
		surfacearray[(int)Mesh.ArrayType.Vertex] = vertarray;
		surfacearray[(int)Mesh.ArrayType.Index] = indices.ToArray();
		surfacearray[(int)Mesh.ArrayType.Normal] = normalarray;
		arraymesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfacearray);
		this.Mesh = arraymesh;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
		{
			rd.FreeRid(shader);
			rd.FreeRid(uniformset);
			rd.FreeRid(pipeline);
			rd.FreeRid(vertbuffer);
		}
	}
}

