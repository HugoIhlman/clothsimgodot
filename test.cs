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
	[Export] public int width = 16;
	[Export] public int height = 16;
	[Export] public float restDistance = 0.2f;

	private int divs = 0;
	
	private Array vertices;
	private Vector3 currentVert;
	private Vector3 prevVert;
	
	private List<Vector3> pinnedVertices;
	
	private RenderingDevice rd;
	private Rid shader;
	private Rid vertbufferin;
	private Rid vertbufferout;
	private Rid uniformseta;
	private Rid uniformsetb;
	private Rid pipeline;
	private RDUniform offsetUniform;
	private RDUniform dataUniform;
	private MeshDataTool mdt;

	private List<uint>[] adjacencyMap;

	[StructLayout(LayoutKind.Sequential, Pack = 16)]
	struct GpuVertex
	{
		public Vector4 Position;
		public Vector4 PrevPosition;
		public Vector4 Normal;
		public Vector4 Flags;
	}
	
	[StructLayout(LayoutKind.Sequential, Pack = 16)]
	struct GpuParams
	{
		public uint width;
		public uint height;
		public float dt;
		public float rd;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 16)]
	struct NeigborOffset
	{
		public uint startindex;
		public uint count;
		public uint pad1;
		public uint pad2;
	}
	
	public override void _Ready()
	{
		divs = width - 1;
		rd = RenderingServer.CreateLocalRenderingDevice();
		var shaderfile = GD.Load<RDShaderFile>("res://compute.glsl");
		var shaderbytecode = shaderfile.GetSpirV();
		shader = rd.ShaderCreateFromSpirV(shaderbytecode);
		mdt = new MeshDataTool();
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

			if (pinnedVertices.Contains(position))
			{
				GpuVerticies[j].Flags = new Vector4(1,0,0,1);
			}
			else
			{
				GpuVerticies[j].Flags = new Vector4(0,0,0,1);
			}
			
		}
		
		byte[] inputbytes = MemoryMarshal.AsBytes(GpuVerticies.AsSpan()).ToArray();
		vertbufferin = rd.StorageBufferCreate((uint)inputbytes.Length, inputbytes);
		vertbufferout = rd.StorageBufferCreate((uint)inputbytes.Length, inputbytes);
		var vuniformina = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0
		};
		var vuniformoutb = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 1
		};
		
		vuniformina.AddId(vertbufferin);
		vuniformoutb.AddId(vertbufferout);
		
		InitAdjacencyBuffers(mesh);
		
		uniformseta = rd.UniformSetCreate([vuniformina,vuniformoutb, offsetUniform, dataUniform ], shader, 0);
		var vuniforminb = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0
		};
		var vuniformouta = new RDUniform
		{
			UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 1
		};
		vuniforminb.AddId(vertbufferout);
		vuniformouta.AddId(vertbufferin);
		uniformsetb = rd.UniformSetCreate([vuniforminb,vuniformouta, offsetUniform, dataUniform ], shader, 0);
		pipeline = rd.ComputePipelineCreate(shader);
	}

	void InitAdjacencyBuffers(ArrayMesh mesh)
	{
		var arrays = mesh.SurfaceGetArrays(0);
		Vector3[] verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
		int[] indices = (int[])arrays[(int)Mesh.ArrayType.Index];
		
		int vertexcount = verts.Length;
		
		adjacencyMap =  new List<uint>[vertexcount];
		for (int i = 0; i < vertexcount; i++)
		{
			adjacencyMap[i] = new List<uint>();
		}

		for (int i = 0; i < indices.Length; i += 3)
		{
			uint v0 =  (uint)indices[i];
			uint v1 =  (uint)indices[i + 1];
			uint v2 =  (uint)indices[i + 2];
			
			if (!adjacencyMap[v0].Contains(v1)) adjacencyMap[v0].Add(v1);
			if (!adjacencyMap[v0].Contains(v2)) adjacencyMap[v0].Add(v2);
			
			if (!adjacencyMap[v1].Contains(v0)) adjacencyMap[v1].Add(v0);
			if (!adjacencyMap[v1].Contains(v2)) adjacencyMap[v1].Add(v2);
			
			if (!adjacencyMap[v2].Contains(v0)) adjacencyMap[v2].Add(v0);
			if (!adjacencyMap[v2].Contains(v1)) adjacencyMap[v2].Add(v1);
		}

		List<uint> flattenedNeighborData = new List<uint>();
		NeigborOffset[] offsets = new NeigborOffset[vertexcount];

		for (int v = 0; v < vertexcount; v++)
		{
			offsets[v].startindex = (uint)flattenedNeighborData.Count;
			offsets[v].count =  (uint)adjacencyMap[v].Count;
			flattenedNeighborData.AddRange(adjacencyMap[v]);
		}
		
		byte[] offsetsBytes = MemoryMarshal.AsBytes(offsets.AsSpan()).ToArray();
		byte[] dataBytes = MemoryMarshal.AsBytes(flattenedNeighborData.ToArray().AsSpan()).ToArray();
		
		Rid offsetsRid = rd.StorageBufferCreate((uint)offsetsBytes.Length, offsetsBytes);
		Rid dataRid = rd.StorageBufferCreate((uint)dataBytes.Length, dataBytes);
		
		offsetUniform = new RDUniform{UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 2};
		dataUniform = new RDUniform{UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 3};
		
		offsetUniform.AddId(offsetsRid);
		dataUniform.AddId(dataRid);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		bool isevenframe = (Engine.GetFramesDrawn() % 2 == 0);
		Rid activeset = isevenframe ? uniformseta : uniformsetb;
		Rid activebuffer = isevenframe ? vertbufferout : vertbufferin;
		
		GpuParams param = new GpuParams()
		{
			width = (uint) width,
			height = (uint) height,
			dt = 1f / 60f,
			rd = restDistance
		};
		ReadOnlySpan<GpuParams> paramspan = MemoryMarshal.CreateReadOnlySpan(ref param, 1);
		byte[] pushConstantsBytes = MemoryMarshal.AsBytes(paramspan).ToArray();
		
		var arrays = this.Mesh.SurfaceGetArrays(0);
		Vector3[] verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
		int vertcount = verts.Length;
		
		var computelist = rd.ComputeListBegin();
		rd.ComputeListBindComputePipeline(computelist, pipeline);
		rd.ComputeListBindUniformSet(computelist, activeset,0);
		uint xGroups = (uint)Mathf.CeilToInt(vertcount / 32);
		uint yGroups = (uint)Mathf.CeilToInt((height * 4) / 16f);
		rd.ComputeListSetPushConstant(computelist, pushConstantsBytes, (uint)pushConstantsBytes.Length);
		rd.ComputeListDispatch(computelist, xGroups,1,1);
		rd.ComputeListEnd();
		rd.Submit();
		rd.Sync();
		var outputbytes = rd.BufferGetData(activebuffer);
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
		float side = width / divs;
		pinnedVertices = new List<Vector3>();
		for (int y = 0; y < divs + 1; y++)
		{
			for (int x = 0; x < divs + 1; x++)
			{
				Vector3 pos = new Vector3(x * side * restDistance, 0, y * -side * restDistance);
				if (y == 0 ||  y == divs || x == 0 ||  x == divs)
				{
					pinnedVertices.Add(pos);
				}
				verts.Add(pos);
				normals.Add(Vector3.Up);
			}
		}

		for (int i = 0; i < divs; i++)
		{
			for (int j = 0; j < divs; j++)
			{
				int index = i * (divs + 1) + j;
				int tl = index;
				int tr = index + 1;
				int bl = index + (divs + 1) + 1;
				int br = index + (divs + 1);
				indices.AddRange(new int[]{tl,bl,br,tl,tr,bl});
			}
		}
		surfaceArray[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		
		return surfaceArray;
	}

	private Vector3 calculateNormals(int vertexid)
	{
		var n_verts = adjacencyMap[vertexid];
		Vector3 sum = Vector3.Zero;
		Vector3 a = mdt.GetVertex(vertexid);

		for (int v = 1; v < n_verts.Count; v++)
		{
			Vector3 b = mdt.GetVertex((int)n_verts[v - 1]);
			Vector3 c = mdt.GetVertex((int)n_verts[v]);
			sum += (b - a).Cross(c - a);
		}

		return sum.Normalized();
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
			vertarray[i] = new Vector3(vertices[i].Position.X , vertices[i].Position.Y, vertices[i].Position.Z);
			normalarray[i] = Vector3.Up;
		}
		for (int y = 0; y < divs; y++)
		{
			for (int x = 0; x < divs; x++)
			{
				int index = y * (divs + 1) + x;
				int tl = index;
				int tr = index + 1;
				int bl = index + (divs + 1) + 1;
				int br = index + (divs + 1);
				indices.AddRange(new int[]{tl,bl,br,tl,tr,bl});
			}
		}
		surfacearray[(int)Mesh.ArrayType.Vertex] = vertarray;
		surfacearray[(int)Mesh.ArrayType.Index] = indices.ToArray();
		surfacearray[(int)Mesh.ArrayType.Normal] = normalarray;
		arraymesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfacearray);
		var st = new SurfaceTool();
		st.CreateFrom(arraymesh, 0);
		st.GenerateNormals();
		st.Commit(arraymesh);
		this.Mesh = arraymesh;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
		{
			rd.FreeRid(shader);
			rd.FreeRid(uniformsetb);
			rd.FreeRid(uniformseta);
			rd.FreeRid(pipeline);
			rd.FreeRid(vertbufferin);
			rd.FreeRid(vertbufferout);
		}
	}
}

