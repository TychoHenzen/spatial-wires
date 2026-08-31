@tool
extends EditorPlugin

const REPORT_ENVIRONMENT_VARIABLE := "SPATIAL_WIRES_EDITOR_PROBE_OUTPUT"
const PRODUCT_PLUGIN_NAME := "spatial_circuits"
const PRODUCT_PLUGIN_SCRIPT := "res://addons/spatial_circuits/SpatialCircuitsEditorPlugin.cs"
const PRODUCT_DOCK_SCRIPT := "res://addons/spatial_circuits/SpatialCircuitsDock.cs"


func _enter_tree() -> void:
	if not OS.has_environment(REPORT_ENVIRONMENT_VARIABLE):
		return
	call_deferred("_run_probe")


func _run_probe() -> void:
	await _wait_for_editor_state()

	EditorInterface.set_plugin_enabled(PRODUCT_PLUGIN_NAME, false)
	await _wait_for_editor_state()

	EditorInterface.set_plugin_enabled(PRODUCT_PLUGIN_NAME, true)
	await _wait_for_editor_state()
	var enabled_state := _observe_product_state()

	EditorInterface.set_plugin_enabled(PRODUCT_PLUGIN_NAME, false)
	await _wait_for_editor_state()
	var disabled_state := _observe_product_state()

	EditorInterface.set_plugin_enabled(PRODUCT_PLUGIN_NAME, true)
	await _wait_for_editor_state()
	var reenabled_state := _observe_product_state()

	var report := {
		"enabled": enabled_state,
		"disabled": disabled_state,
		"reenabled": reenabled_state,
	}
	_write_report(report)
	EditorInterface.get_base_control().get_tree().quit(0)


func _wait_for_editor_state() -> void:
	var tree := EditorInterface.get_base_control().get_tree()
	await tree.process_frame
	await tree.process_frame
	await tree.process_frame


func _observe_product_state() -> Dictionary:
	var editor_root := EditorInterface.get_base_control().get_tree().root
	var plugin_instances := _find_nodes_with_script(editor_root, PRODUCT_PLUGIN_SCRIPT)
	var docks := _find_nodes_with_script(editor_root, PRODUCT_DOCK_SCRIPT)
	var handler_count := 0

	for dock: Node in docks:
		for connection: Dictionary in dock.get_signal_connection_list("tree_exited"):
			var callable: Callable = connection.get("callable", Callable())
			var target: Object = callable.get_object()
			if target != null and _has_script_path(target, PRODUCT_PLUGIN_SCRIPT):
				handler_count += 1

	return {
		"plugin_enabled": EditorInterface.is_plugin_enabled(PRODUCT_PLUGIN_NAME),
		"plugin_instance_count": plugin_instances.size(),
		"dock_count": docks.size(),
		"handler_count": handler_count,
	}


func _find_nodes_with_script(root: Node, script_path: String) -> Array[Node]:
	var matches: Array[Node] = []
	var pending: Array[Node] = [root]

	while not pending.is_empty():
		var current: Node = pending.pop_back()
		if _has_script_path(current, script_path):
			matches.append(current)
		for child: Node in current.get_children():
			pending.append(child)

	return matches


func _has_script_path(value: Object, script_path: String) -> bool:
	var script := value.get_script() as Script
	return script != null and script.resource_path == script_path


func _write_report(report: Dictionary) -> void:
	var report_path := OS.get_environment(REPORT_ENVIRONMENT_VARIABLE)
	var output := FileAccess.open(report_path, FileAccess.WRITE)
	if output == null:
		push_error("Could not open lifecycle report path: %s" % report_path)
		return

	output.store_string(JSON.stringify(report))
