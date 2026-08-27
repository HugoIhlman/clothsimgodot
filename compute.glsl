#[compute]
#version 450

layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;

struct Vertex
{
	vec4 pos;
	vec4 prevPos;
	vec4 normal;
	vec4 flags;
};

struct NeighborOffset
{
	uint startIndex;
	uint count;
	uint pad1;
	uint pad2;
};

struct BendNeighbor
{
	uint index;
	float restDistance;
	float pad1;
	float pad2;
};

layout(set = 0, binding = 0, std430) restrict buffer VertBufferIn {
	Vertex verticies[];
} vertex_buffer_in; //vertex read buffer

layout(set = 0, binding = 1, std430) restrict buffer VertBufferOut {
	Vertex verticies[];
} vertex_buffer_out; //vertex write buffer
layout(set = 0, binding = 2, std430) restrict buffer NeighborOffsets {
	NeighborOffset offsets[];
} neighbor_offsets; //vertex adjacent neighbors
layout(set = 0, binding = 3, std430) restrict buffer NeighborData{
	BendNeighbor neighbordata[];
} neighbor_data; //neighbor data for adjacent neighbors
layout(set = 0, binding = 4, std430) restrict buffer BendOffsets{
	NeighborOffset offsets[];
} bend_offsets;//vertex opposite neighbors /n\
//                                          /   \
//                                          -----
//                                          \   /
//                                       
layout(set = 0, binding = 5, std430) restrict buffer BendData{
	BendNeighbor data[];
} bend_data;//neigbor data for opposite neighbors
layout(set = 0, binding = 6, std430) restrict buffer Colliders{
	vec4 colliders[];
} colliders;


layout(push_constant) uniform Params //input parameters
{
	uint w;
	uint h;
	float dt;
	float rd;
	uint pad;
	float wind_x;
	float wind_y;
	float wind_z;
} params;

void main() 
{
	uint idx = gl_GlobalInvocationID.x;
	uint index = idx;
	if (index >= vertex_buffer_in.verticies.length()) return; 
	Vertex v = vertex_buffer_in.verticies[index];
	int contstraintcount = 0; //stretch constraint
	vec3 corr = vec3(0,0,0);
	
	if(v.flags.x > 0.5) // if the vertex is flagged as pinned, do nothing
	{
		vertex_buffer_out.verticies[index] = v;
		return;
	}
	
	vec3 grav = vec3(0.0,-9.81,0.0);
	vec3 pos = v.pos.xyz;
	vec3 prev = v.prevPos.xyz;
	
	
	
	vec3 vel = (pos - prev) * 0.98; // velocity of v * damping
	vec3 nextpos = pos + vel + (grav * params.dt * params.dt); 
	nextpos += vec3(params.wind_x, params.wind_y, params.wind_z) * params.dt;
	
	float alpha_s = 0.000001 / (params.dt * params.dt); //stretch constraint compliance
	
	 
	NeighborOffset offsetinfo = neighbor_offsets.offsets[index];
	uint start = offsetinfo.startIndex; 
	uint totalneighbors = offsetinfo.count;

	NeighborOffset bendInfo = bend_offsets.offsets[index];
	uint bendStart = bendInfo.startIndex;
	uint bendTotal = bendInfo.count;
	

	for (int i = 0; i < totalneighbors; i++) //stretch loop
	{
		BendNeighbor n = neighbor_data.neighbordata[start + i];
		uint neighborindex = n.index;
		if (neighborindex >= vertex_buffer_in.verticies.length()) continue;
		Vertex neighbor = vertex_buffer_in.verticies[neighborindex];
		Vertex bn = vertex_buffer_in.verticies[bendStart + i];
		if(neighbor == bn) continue; //so no stretch constraints arent run on a bend-only vertex
		
		vec3 n_pos = neighbor.pos.xyz;
		vec3 n_integrated = n_pos;
		if(neighbor.flags.x < 0.5)// so neighbor isnt pinned
		{
			vec3 n_prev = neighbor.prevPos.xyz;
			vec3 n_vel = (n_pos - n_prev) * 0.98; // velocity * damping
			n_integrated = n_pos + n_vel + (grav * params.dt * params.dt); //neighbor next pos
		}

		vec3 delta_vec = n_integrated - nextpos; // delta between neighbor next pos and current next pos
		float dist = length(delta_vec);

		if (dist > 0.001) 
		{
			vec3 grad = delta_vec / dist; //normalized delta
			float diff = dist - n.restDistance; //current distance - allowed rest distance

			float w_self = 1.0; //vertex v weight
			float w_neighbor = (neighbor.flags.x > 0.5) ? 0.0 : 1.0;//neighbor weight, if is pinned it should be weightless and not influence vertex v
			float inv_mass_sum = w_self + w_neighbor; //add weights

			float deltlagrgnmlt = -diff / (inv_mass_sum + alpha_s);//delta lagrangian multiplier

			corr -= grad * deltlagrgnmlt * w_self;// add to correction vector
			contstraintcount++;//add to constraint counter
		}
		
	}
	if(contstraintcount > 0) //if constraints exist
	{
		vec3 nextposfinal = corr/ float(contstraintcount); //divide correction by amount of constraints
		nextpos += nextposfinal; //update nextpos
		
	}

	float alpha_b = 0.0001 / (params.dt * params.dt); //bend compliance
	vec3 bendcorr = vec3(0,0,0);
	int bendcount = 0;

	

	for(int i = 0; i < bendTotal; i++)//bending constraint loop
	{
		BendNeighbor bn = bend_data.data[bendStart + i];
		if(bn.index >= vertex_buffer_in.verticies.length()) continue;
		Vertex neighbor = vertex_buffer_in.verticies[bn.index];

		vec3 n_pos = neighbor.pos.xyz;
		vec3 n_integrated = n_pos;
		if(neighbor.flags.x < 0.5)
		{
			vec3 n_prev = neighbor.prevPos.xyz;
			vec3 n_vel = (n_pos - n_prev) * 0.98;
			n_integrated = n_pos + n_vel + (grav * params.dt * params.dt);
		}

		vec3 delta = n_integrated - nextpos;
		float dist = length(delta);

		if(dist > 0.0001)
		{
			vec3 grad = delta / dist;
			float diff = dist - bn.restDistance;

			float w_self = 1.0;
			float w_neighbor = (neighbor.flags.x > 0.5) ? 0.0 : 1.0;
			float inv_mass_sum = w_self + w_neighbor;

			float deltlagrgnmlt = -diff / (inv_mass_sum + alpha_b);
			bendcorr -= grad * deltlagrgnmlt * w_self;
			bendcount++;
		}
	}
	if(bendcount > 0)
	{
		nextpos += bendcorr / float(bendcount);
	}

	vec3 a = colliders.colliders[0].xyz;
	vec3 b = colliders.colliders[1].xyz;
	float radius = colliders.colliders[0].w;
	vec3 ab = b - a;
	float ab2 = dot(ab,ab);

	float t = (ab2 > 1e-12) ? clamp(dot(nextpos - a, ab) / ab2, 0.0, 1.0) : 0.0;
	vec3 closest = a + ab * t;
	vec3 difference = nextpos - closest;
	float distance = length(difference);
	if(distance < radius && distance > 1e-7){
		nextpos = closest + (difference / distance) * (radius);
	}
	
	v.prevPos.xyz = pos;
	v.pos.xyz = nextpos;
	vertex_buffer_out.verticies[index] = v;
}
