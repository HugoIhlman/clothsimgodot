extends RigidBody3D
class_name Grabbable
var held_by: XRController3D = null
var offset: Transform3D = Transform3D.IDENTITY


# Called when the node enters the scene tree for the first time.
func start_grab(controller: XRController3D) -> void:
	held_by = controller
	offset = controller.global_transform.affine_inverse() * global_transform
	
func _release() -> void:
	held_by = null


# Called every frame. 'delta' is the elapsed time since the previous frame.
func _physics_process(delta: float) -> void:
	if not held_by:
		return
	
	var target_transform = held_by.global_transform * offset
	
	var target_pos = target_transform.origin
	var current_pos = global_transform.origin
	
	linear_velocity = (target_pos - current_pos) * 500.0 * delta
	
	var target_rot = target_transform.basis
	var current_rot = global_basis
	var rot_diff = target_rot * current_rot.inverse()
	var axis = rot_diff.get_rotation_quaternion().get_axis()
	var angle = rot_diff.get_rotation_quaternion().get_angle()
	
	if angle > PI:
		angle -= TAU
		
	
	angular_velocity = axis * angle * 500.0 * delta
