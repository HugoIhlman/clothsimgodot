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

layout(set = 0, binding = 0, std430) restrict buffer VertBufferIn {
	Vertex verticies[];
} vertex_buffer_in;

layout(set = 0, binding = 1, std430) restrict buffer VertBufferOut {
	Vertex verticies[];
} vertex_buffer_out;
layout(set = 0, binding = 2, std430) restrict buffer NeighborOffsets {
	NeighborOffset offsets[];
} neighbor_offsets;
layout(set = 0, binding = 3, std430) restrict buffer NeighborData{
	uint neighbordata[];
} neighbor_data;

layout(push_constant) uniform Params
{
	uint w;
	uint h;
	float dt;
	float rd;
} params;

void main() 
{
	uint idx = gl_GlobalInvocationID.x;
	uint index = idx;
	if (index >= vertex_buffer_in.verticies.length()) return;
	Vertex v = vertex_buffer_in.verticies[index];
	int contstraintcount = 0;
	vec3 corr = vec3(0,0,0);
	
	if(v.flags.x > 0.5)
	{
		vertex_buffer_out.verticies[index] = v;
		return;
	}
	
	vec3 grav = vec3(0.0,-9.81,0.0);
	vec3 pos = v.pos.xyz;
	vec3 prev = v.prevPos.xyz;
	
	vec3 vel = (pos - prev);
	vec3 nextpos = pos + vel + (grav * params.dt * params.dt);
	
	float alpha_s = 0.00001 / (params.dt * params.dt);
	

	NeighborOffset offsetinfo = neighbor_offsets.offsets[index];
	uint start = offsetinfo.startIndex;
	uint totalneighbors = offsetinfo.count;
	

	for (int i = 0; i < totalneighbors; i++) {
		uint neighborindex = neighbor_data.neighbordata[start + i];
		if (neighborindex >= vertex_buffer_in.verticies.length()) continue;
		Vertex neighbor = vertex_buffer_in.verticies[neighborindex];
		
		vec3 gravity = vec3(0.0, -9.81, 0.0);
		vec3 n_pos = neighbor.pos.xyz;
		vec3 n_integrated = n_pos;
		if(neighbor.flags.x < 0.5)
		{
			vec3 n_prev = neighbor.prevPos.xyz;
			vec3 n_vel = (n_pos - n_prev);
			n_integrated = n_pos + n_vel + (gravity * params.dt * params.dt);
		}

		vec3 delta_vec = n_integrated - nextpos;
		float dist = length(delta_vec);

		if (dist > 0.001) {
			vec3 grad = delta_vec / dist;
			float diff = dist - params.rd;

			float w_self = 1.0;
			float w_neighbor = (neighbor.flags.x > 0.5) ? 0.0 : 1.0;
			float inv_mass_sum = w_self + w_neighbor;

			float deltlagrgnmlt = -diff / (inv_mass_sum + alpha_s);

			corr -= grad * deltlagrgnmlt * w_self;
			contstraintcount++;
		}
		
	}

	if(contstraintcount > 0){
		vec3 nextposfinal = corr / float(contstraintcount);
		
		float forcemag = length(nextposfinal);
		float maxallowed = params.rd * 0.5;
		if(forcemag > maxallowed)
		{
			nextposfinal = (nextposfinal / forcemag) * maxallowed;
		}
		if (!isnan(nextposfinal.x) && !isinf(nextposfinal.x) &&
			!isnan(nextposfinal.y) && !isinf(nextposfinal.y) &&
			!isnan(nextposfinal.z) && !isinf(nextposfinal.z))
		{
			nextpos += nextposfinal;
		}
	}
	v.prevPos.xyz = pos;
	v.pos.xyz = nextpos;
	vertex_buffer_out.verticies[index] = v;
}
