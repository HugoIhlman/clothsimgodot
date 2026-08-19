#[compute]
#version 450

layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

struct Vertex
{
	vec4 pos;
    vec4 prevPos;
	vec4 normal;
    vec4 flags;
};

layout(set = 0, binding = 0, std430) restrict buffer VertBuffer {
	Vertex verticies[];
} vertex_buffer;

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
	uint index = idx * params.w + idy;
    Vertex v = vertex_buffer.verticies[index];
    
    if(v.flags.x > 0.5)
    {
        return;
    }
    
    vec3 grav = vec3(0.0,-9.81,0.0);
    vec3 pos = v.pos.xyz;
    vec3 prev = v.prevPos.xyz;
    
    vec3 vel = (pos - prev);
    vec3 nextpos = pos + vel + (grav * params.dt * params.dt);
    
    v.prevPos.xyz = pos;
    v.pos.xyz = nextpos;
    vertex_buffer.verticies[index] = v;
    
	
	memoryBarrierShared();
	barrier();
}
