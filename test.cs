using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot.Collections;
using Godot.NativeInterop;
using Array = Godot.Collections.Array;

public partial class test : MeshInstance3D
{
	// Called when the node enters the scene tree for the first time.
	[Export] public int width = 16;
	[Export] public int height = 16;
	[Export] public float restDistance = 0.2f;
	[Export] public Vector3 wind = Vector3.Zero;
	[Export] public float turbulence = 0.3f;
	[Export] public PackedScene colliderprefab {get; set;}

	private List<clothCollider> _colliders = [];

	private ArrayMesh arrayMesh;
	private bool meshDone = false;
	private bool inProgress = false;
	
	private int divs = 0;
	
	private Array vertices;
	private Vector3 currentVert;
	private Vector3 prevVert;

	private Vector2[] uvs;

	private int[] _indices;

	private int selectedVert = -1;
	
	private List<Vector3> pinnedVertices;
	
	private RenderingDevice rd;
	private Rid shader;
	private Rid vertbufferin;
	private Rid vertbufferout;
	private Rid uniformseta;
	private Rid uniformsetb;
	private Rid pipeline;
	private Rid colliders;
	private RDUniform offsetUniform;
	private RDUniform dataUniform;
	private RDUniform bendOffsetUniform;
	private RDUniform bendDataUniform;
	private MeshDataTool mdt;

	private GpuVertex[] bert;

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
		public uint collidercount;
		public float wind_x;
		public float wind_y;
		public float wind_z;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 16)]
	struct NeigborOffset
	{
		public uint startindex;
		public uint count;
		public uint pad1;
		public uint pad2;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 16)]
	struct BendNeighbor
	{
		public uint index;
		public float restdistance;
		public float pad1;
		public float pad2;
	}
	
	public override void _Ready()
	{
		divs = width - 1;
		//GetViewport().DebugDraw = Viewport.DebugDrawEnum.Wireframe;
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
		InitBendingBuffers(mesh);

		findColliders(GetParent());


		byte[] colliderByteData = updateColliderCount();
		colliders = rd.StorageBufferCreate((uint)colliderByteData.Length * 4, null);
		var cuniform = new RDUniform
		{
			UniformType =  RenderingDevice.UniformType.StorageBuffer, Binding = 6
		};
		cuniform.AddId(colliders);
		
		uniformseta = rd.UniformSetCreate([vuniformina,vuniformoutb, offsetUniform, dataUniform, bendOffsetUniform, bendDataUniform, cuniform ], shader, 0);
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
		uniformsetb = rd.UniformSetCreate([vuniforminb,vuniformouta, offsetUniform, dataUniform, bendOffsetUniform, bendDataUniform, cuniform ], shader, 0);
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

		var flattenedNeighborData = new List<BendNeighbor>();
		NeigborOffset[] offsets = new NeigborOffset[vertexcount];

		for (int v = 0; v < vertexcount; v++)
		{
			offsets[v].startindex = (uint)flattenedNeighborData.Count;
			offsets[v].count =  (uint)adjacencyMap[v].Count;
			foreach (var n in adjacencyMap[v])
			{
				flattenedNeighborData.Add(new BendNeighbor{index = n, restdistance = verts[v].DistanceTo(verts[(int)n])});	
			}
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

	void InitBendingBuffers(ArrayMesh mesh)
	{
		var arrays = mesh.SurfaceGetArrays(0);
		Vector3[] verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
		int[] indices = (int[])arrays[(int)Mesh.ArrayType.Index];

		var edgeToOpp = new System.Collections.Generic.Dictionary<(uint, uint), List<uint>>();

		void addEdgeToOpp(uint v0, uint v1, uint v2)
		{
			var key = v0 < v1 ? (v0,v1) :  (v1,v0);
			if (!edgeToOpp.TryGetValue(key, out var list))
			{
				list = new List<uint>();
				edgeToOpp[key] = list;
			}
			list.Add(v2);
		}

		for (int i = 0; i < indices.Length; i += 3)
		{
			uint v0 = (uint)indices[i];
			uint v1 = (uint)indices[i + 1];
			uint v2 = (uint)indices[i + 2];
			addEdgeToOpp(v0, v1, v2);
			addEdgeToOpp(v1, v2, v0);
			addEdgeToOpp(v2, v0, v1);
		}
		int vertcount = verts.Length;
		var bendmap = new List<uint>[vertcount];
		for (int i = 0; i < vertcount; i++)
		{
			bendmap[i] = new List<uint>();
		}

		foreach (var kvp in edgeToOpp)
		{
			var opp = kvp.Value;
			if (opp.Count == 2)
			{
				bendmap[opp[0]].Add(opp[1]);
				bendmap[opp[1]].Add(opp[0]);
			}
		}

		var flattened = new List<BendNeighbor>();
		var offsets = new NeigborOffset[vertcount];

		for (int i = 0; i < vertcount; i++)
		{
			offsets[i].startindex = (uint)flattened.Count;
			offsets[i].count = (uint)bendmap[i].Count;
			foreach (var partner in bendmap[i])
			{
				flattened.Add(new BendNeighbor{index = partner, restdistance = verts[i].DistanceTo(verts[(int)partner]) });
			}
		}
		
		byte[] offsetsBytes = MemoryMarshal.AsBytes(offsets.AsSpan()).ToArray();
		byte[] dataBytes = MemoryMarshal.AsBytes(flattened.ToArray().AsSpan()).ToArray();
		
		Rid bendOffsetRid = rd.StorageBufferCreate((uint)offsetsBytes.Length, offsetsBytes);
		Rid dataRid = rd.StorageBufferCreate((uint)dataBytes.Length, dataBytes);
		bendOffsetUniform = new RDUniform{UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 4};
		bendDataUniform = new RDUniform{UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 5};
		bendOffsetUniform.AddId(bendOffsetRid);
		bendDataUniform.AddId(dataRid);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_colliders.Count > 3)
		{
			deleteElderCollider();
		}
		bool isevenframe = (Engine.GetFramesDrawn() % 2 == 0);
		Rid activeset = isevenframe ? uniformseta : uniformsetb;
		Rid activebuffer = isevenframe ? vertbufferout : vertbufferin;
		
		var t = Time.GetTicksMsec() / 1000.0f;
		var gust = new Vector3(
			Mathf.Sin(t * 1.7f) + Mathf.Sin(t * 3.1f + 1.3f),
			Mathf.Sin(t * 1.3f + 2.0f) + Mathf.Sin(t * 2.7f + 0.7f),
			Mathf.Sin(t * 2.1f + 4.0f) + Mathf.Sin(t * 1.9f + 3.1f));
		var eff_wind = wind + wind.Length() * gust * turbulence;
		var local_wind = GlobalTransform.Basis.Inverse() * eff_wind;
		GpuParams param = new GpuParams()
		{
			width = (uint) width,
			height = (uint) height,
			dt = 1f / 60f,
			rd = restDistance,
			collidercount = (uint)_colliders.Count,
			wind_x = local_wind.X,
			wind_y = local_wind.Y,
			wind_z = local_wind.Z
		};
		ReadOnlySpan<GpuParams> paramspan = MemoryMarshal.CreateReadOnlySpan(ref param, 1);
		byte[] pushConstantsBytes = MemoryMarshal.AsBytes(paramspan).ToArray();
		
		var arrays = this.Mesh.SurfaceGetArrays(0);
		Vector3[] verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
		int vertcount = verts.Length;
		
		if(selectedVert >= 0) updateSelection();
		
		var computelist = rd.ComputeListBegin();
		rd.ComputeListBindComputePipeline(computelist, pipeline);
		rd.ComputeListBindUniformSet(computelist, activeset,0);
		uint xGroups = (uint)Mathf.CeilToInt(vertcount / 32);
		rd.ComputeListSetPushConstant(computelist, pushConstantsBytes, (uint)pushConstantsBytes.Length);
		rd.ComputeListDispatch(computelist, xGroups,1,1);
		rd.ComputeListEnd();
		rd.Submit();
		rd.Sync();
		var outputbytes = rd.BufferGetData(activebuffer);
		bert = MemoryMarshal.Cast<byte, GpuVertex>(outputbytes).ToArray();
		updateGeometry(bert);
		updateColliders();
	}

	private Array createSurfaceArray()
	{
		Godot.Collections.Array surfaceArray = [];
		surfaceArray.Resize((int)Mesh.ArrayType.Max);

		List<Vector3> verts = [];
		List<Vector3> normals = [];
		List<Vector2> uv = [];
		List<int> indices = [];
		float side = width / divs;
		pinnedVertices = new List<Vector3>();
		for (int y = 0; y < divs + 1; y++)
		{
			for (int x = 0; x < divs + 1; x++)
			{
				Vector3 pos = new Vector3(x * side, 0, y * -side );
				if (y == 0)
				{
					pinnedVertices.Add(pos);
				}
				verts.Add(pos);
				normals.Add(Vector3.Up);
			}
		}
		
		int gridvertcount = verts.Count;
		
		int[,] centeridx = new int[divs, divs];

		for (int i = 0; i < divs; i++)
		{
			for (int j = 0; j < divs; j++)
			{
				int tlIdx = i * (divs + 1) + j;
				int trIdx = tlIdx + 1;
				int blIdx = tlIdx + (divs + 1);
				int brIdx = blIdx + 1;

				Vector3 center = (verts[tlIdx] + verts[trIdx] + verts[blIdx] + verts[brIdx]) / 4f;
				centeridx[i, j] = verts.Count;
				verts.Add(center);
				normals.Add(Vector3.Up);
			}
		}

		foreach (var e in verts)
		{
			uv.Add(new Vector2(e.X / width, 1 - (e.Z / width)));
		}

		for (int i = 0; i < divs; i++)
		{
			for (int j = 0; j < divs; j++)
			{
				int tl = i * (divs + 1) + j;
				int tr = tl + 1;
				int bl = tl + (divs + 1);
				int br = bl + 1; 
				int c = ((divs + 1) * (divs + 1)) + (i*divs+j);
				indices.AddRange(new int[]{tl,tr,c,tr,br,c,br,bl,c,bl,tl,c});
			}
		}
		surfaceArray[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();
		surfaceArray[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		surfaceArray[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
		
		 _indices = indices.ToArray();
		 
		 uvs = new Vector2[verts.Count + (divs * divs)];
		 for (int i = 0; i < verts.Count; i++)
		 {
			 uvs[i] = new Vector2(uv[i].X, uv[i].Y);
		 }
		
		return surfaceArray;
	}

	private void updateGeometry(GpuVertex[] vertices)
	{
		var arraymesh = new ArrayMesh();
		var surfacearray = new Array();
		surfacearray.Resize((int)Mesh.ArrayType.Max);
		int qc = divs * divs;
		Vector3[] vertarray = new Vector3[vertices.Length + qc];
		for (int i = 0; i < vertices.Length; i++)
		{
			vertarray[i] = new Vector3(vertices[i].Position.X, vertices[i].Position.Y, vertices[i].Position.Z);
		}
		

		surfacearray[(int)Mesh.ArrayType.Vertex] = vertarray;
		surfacearray[(int)Mesh.ArrayType.Index] = _indices;
		surfacearray[(int)Mesh.ArrayType.TexUV] = uvs;
		arraymesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfacearray);
		var st = new SurfaceTool();
		st.CreateFrom(arraymesh, 0);
		st.GenerateNormals();
		st.Commit(arraymesh);
		this.Mesh = arraymesh;
	}

	private byte[] updateColliderCount()
	{
		_colliders.Clear();
		findColliders(GetParent());
		int colliderCount = _colliders.Count;
		int write_index = 0;
		byte[] data = new byte[colliderCount * 64];
		for (int i = 0; i < colliderCount; i++)
		{
			var colliderData = _colliders[i].PackColliderData(GlobalTransform.AffineInverse());
			var byteData = MemoryMarshal.AsBytes(colliderData.AsSpan()).ToArray();
			var offset = write_index * 64;
			for (int j = 0; j < 64; j++)
			{
				data[offset + j] = byteData[j];
			}
			write_index++;
		}
		return data;
	}

	void updateColliders()
	{
		var data = updateColliderCount();
		rd.BufferUpdate(colliders, 0, (uint)data.Length, data);
	}

	void deleteElderCollider()
	{
		var col =  _colliders[0];
		_colliders.Remove(col);
		col.GetParent().Free();
		updateColliders();
	}

	private void findColliders(Node node)
	{
		if (node is clothCollider)
		{
			_colliders.Add(node as clothCollider);
		}
		foreach (var child in node.GetChildren())
		{
			findColliders(child);
		}
		
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			doStuff(mouseEvent);
		}

		if (@event is InputEventKey keyEvent)
		{
			if (keyEvent.Keycode == Key.R)
			{
				resetPinnedVertices();
			}
			if (keyEvent.Keycode == Key.Space)
			{
				if (keyEvent.IsReleased())
				{
					shootBall();
				}
			}
		}
	}

	void shootBall()
	{
		var cam = GetViewport().GetCamera3D();
		var mousepos = GetViewport().GetMousePosition();
		var raystart = cam.ProjectRayOrigin(mousepos);
		var dir = cam.ProjectRayNormal(mousepos);
		Node ball = colliderprefab.Instantiate();
		GetParent().AddChild(ball);
		var b = ball as RigidBody3D;
		var t = ball as Node3D;
		t.Position = raystart;
		b.ApplyImpulse(dir * 40f);
		updateColliderCount();
	}

	void resetPinnedVertices()
	{
		for (int i = 0; i < bert.Length; i++)
		{
			var pos = new Vector3(bert[i].Position.X, bert[i].Position.Y, bert[i].Position.Z);
			if (!pinnedVertices.Contains(pos))
			{
				bert[i].Flags.X = 0f;
			}
		}
		updateVertexBuffer();
	}
	

	void doStuff(InputEventMouseButton pressed)
	{
		var cam = GetViewport().GetCamera3D();
		var mousepos = GetViewport().GetMousePosition();
		var raystart = cam.ProjectRayOrigin(mousepos);
		var dir = cam.ProjectRayNormal(mousepos);
		Vector3 pos = Vector3.Zero;
		var arrays = Mesh.SurfaceGetArrays(0);
		int[] inds = (int[])arrays[(int)Mesh.ArrayType.Index];
		Vector3[] verts = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
		if (pressed.IsPressed())
		{
			for (int i = 2; i < inds.Length; i += 3)
			{
				Vector3 v0 = verts[inds[i]];
				Vector3 v1 = verts[inds[i -1]];
				Vector3 v2 = verts[inds[i-2]];
				var point = Geometry3D.RayIntersectsTriangle(ToLocal(raystart), ToLocal(dir), v0, v1, v2);
				if (!point.AsBool())
				{
					continue;
				}
				Vector3 pointpos = point.AsVector3();
				var dist1 = v0.DistanceTo(pointpos);
				var dist2 = v1.DistanceTo(pointpos);
				var dist3 = v2.DistanceTo(pointpos);
				var closest = Math.Min(Math.Min(dist1, dist2), dist3);
				if (closest == dist1) selectedVert = inds[i];
				else if (closest == dist2) selectedVert = inds[i -1];
				else if (closest == dist3) selectedVert = inds[i -2];
			}
			if (selectedVert >= 0)
			{
				bert[selectedVert].Flags.X = 1f;
			}
		}
		else if(pressed.IsReleased())
		{
			if (selectedVert >= 0)
			{ 
				bert[selectedVert].Flags.X = 0f;
				selectedVert = -1;
			}
		}
		updateVertexBuffer();
		GD.Print(pos);
	}

	void updateSelection()
	{
		var cam = GetViewport().GetCamera3D();
		var mousepos = GetViewport().GetMousePosition();
		if (selectedVert >= 0)
		{
			var localpos = ToLocal((cam.ProjectPosition(mousepos,34.45f )) );
			bert[selectedVert].Position.X = localpos.X;
			bert[selectedVert].Position.Y = localpos.Y;
		}
		updateVertexBuffer();
	}

	void updateVertexBuffer()
	{
		byte[] inputbytes = MemoryMarshal.AsBytes(bert.AsSpan()).ToArray();
		rd.BufferUpdate(vertbufferin, 0,(uint)inputbytes.Length, inputbytes);
	}

	public void drawSphere(Vector3 position, float radius)
	{
		var m = new MeshInstance3D();
		var mesh = new SphereMesh();
		
		mesh.Radius = radius;
		mesh.Height = radius * 2;

		m.Mesh = mesh;
		var material = new StandardMaterial3D();
		material.AlbedoColor = Colors.Red;
		m.MaterialOverride = material;
		m.Position = position;
		AddChild(m);
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
			rd.FreeRid(colliders);
		}
	}
}

