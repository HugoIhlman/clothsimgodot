#[compute]
#version 450

layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

struct Vertex
{
	vec4 pos;
	vec4 prevPos;
	vec4 normal;
	vec4 flags;
};

layout(set = 0, binding = 0, std430) restrict buffer VertBufferIn {
	Vertex verticies[];
} vertex_buffer_in;

layout(set = 0, binding = 1, std430) restrict buffer VertBufferOut {
	Vertex verticies[];
} vertex_buffer_out;

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
	uint idy = gl_GlobalInvocationID.y;
	uint index = idy * params.w + idx;
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
	
	vec3 vel = (pos - prev) * 0.075;
	vec3 nextpos = pos + vel + (grav * params.dt * params.dt);


	int offset_x[4] = int[](1, -1,  0,  0);
	int offset_y[4] = int[](0,  0,  1, -1);

	for (int i = 0; i < 4; i++) {
		int nx = int(idx) + offset_x[i];
		int ny = int(idy) + offset_y[i];
		
		if (nx >= 0 && nx < params.w && ny >= 0 && ny < params.h) {

			uint n_idx = uint(ny * params.w + nx);
			Vertex neighbor = vertex_buffer_in.verticies[n_idx];

			vec3 n_pos = neighbor.pos.xyz;
			vec3 n_prev = neighbor.prevPos.xyz;
			vec3 n_vel = (n_pos - n_prev) * 0.98;
			vec3 gravity = vec3(0.0, -9.81, 0.0);
			vec3 n_integrated = n_pos + n_vel + (gravity * params.dt * params.dt);

			vec3 delta_vec = n_integrated - nextpos;
			float dist = length(delta_vec);

			if (dist > 0.0001) {
				float diff = (params.rd - dist) / dist;
				
				float weight_factor = 0.5;
				if (neighbor.flags.x > 0.5) {
					weight_factor = 0.25;
				}
				
				corr -= delta_vec * weight_factor * diff * 0.15;
				contstraintcount++;
			}
		}
	}

	if(contstraintcount > 0){
		nextpos += corr / float(contstraintcount);
	}
	v.prevPos.xyz = pos;
	v.pos.xyz = nextpos;
	vertex_buffer_out.verticies[index] = v;
}
