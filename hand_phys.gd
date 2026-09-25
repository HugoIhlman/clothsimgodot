extends CharacterBody3D


@export var controller: XRController3D
@export var follow_speed: float = 30.0
@export var push_force: float = 5.0

var grabbed_object: Grabbable = null
var grabbed: bool = false

func _ready() -> void:
	controller.button_pressed.connect(_on_button_pressed)
	controller.button_released.connect(_on_button_released)
	
func _on_button_pressed(button: String) -> void:
	if button == "grip_click":
		print("pressed")
		grabbed = true

func _on_button_released(button: String) -> void:
	if button == "grip_click":
		print("released")
		grabbed = false;
		_try_release()
		

func _physics_process(delta: float) -> void:
	# Add the gravity.
	var vel : Vector3 = (controller.global_transform.origin - global_transform.origin) * follow_speed
	velocity = vel
	rotation = controller.rotation
	move_and_slide()
	for i in get_slide_collision_count():
		var collision = get_slide_collision(i)
		var collider = collision.get_collider()
		if grabbed and collider is Grabbable:
			_try_grab(collider as Grabbable)
			
		if collider is RigidBody3D and grabbed_object == null:
			var force = -collision.get_normal() * push_force * velocity.length()
			collider.apply_force(force, collision.get_position() - collider.global_position)
			
		
func _try_grab(object: Grabbable) -> void:
	if grabbed_object:
		return
	if object.held_by:
		return
		
	grabbed_object = object
	grabbed_object.start_grab(controller)
	grabbed_object.custom_integrator = true
	grabbed = false;
	
func _try_release() -> void:
	if not grabbed_object:
		return
	grabbed_object._release()
	grabbed_object.linear_velocity = velocity
	grabbed_object.custom_integrator = false
	grabbed_object = null
		
	
	
