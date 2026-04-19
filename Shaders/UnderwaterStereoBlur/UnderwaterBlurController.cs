// UnderwaterBlurController.cs
// UdonSharp behaviour that detects when the local player's head enters the
// underwater trigger volume and smoothly fades the blur effect in or out by
// animating the "_EffectBlend" property on the blur sphere's material.
//
// SCENE SETUP:
//   ┌─ UnderwaterZone  (this script + Collider, Is Trigger = ON)
//   │    ├─ BoxCollider / SphereCollider that matches the water volume
//   │    └─ UnderwaterBlurController component
//   │         └─ blurSphereRenderer → (drag the BlurSphere renderer here)
//   │
//   └─ BlurSphere  (large sphere, ~3-5 m radius, child or separate object)
//        ├─ MeshRenderer with UnderwaterBlur material
//        └─ (NO collider – trigger lives on UnderwaterZone only)
//
// The blur sphere uses Cull Front so its inside surface is visible to the
// player when the camera is inside it.  The script enables the renderer when
// the player enters and disables it once the fade-out is complete.

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[AddComponentMenu("CozyCon/Underwater Blur Controller")]
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UnderwaterBlurController : UdonSharpBehaviour
{
	[Header("References")]
	[Tooltip("The Renderer on the sphere that has the UnderwaterBlur material.")]
	[SerializeField] private Renderer blurSphereRenderer;

	[Header("Transition")]
	[Tooltip("How quickly the effect fades IN when the player enters water.")]
	[SerializeField] private float fadeInSpeed = 3.0f;
	[Tooltip("How quickly the effect fades OUT when the player leaves water.")]
	[SerializeField] private float fadeOutSpeed = 2.0f;
	[Tooltip("Target blend value when fully underwater (maps to _EffectBlend on the material).")]
	[SerializeField][Range(0f, 1f)] private float maxEffectBlend = 1.0f;

	[Header("Head Follow")]
	[Tooltip("When enabled, the blur sphere also matches local head rotation. Position always follows head.")]
	[SerializeField] private bool followHeadRotation = false;

	[Header("Trigger Stability")]
	[Tooltip("Keeps underwater state active briefly after an exit event to avoid trigger jitter flicker.")]
	[SerializeField] private float triggerExitGraceSeconds = 0.2f;
	[Tooltip("Optional explicit water trigger collider. If empty, uses the collider on this GameObject.")]
	[SerializeField] private Collider waterTriggerCollider;

	// -----------------------------------------------------------------------
	// Private state
	// -----------------------------------------------------------------------
	private Material _mat;
	private float _currentBlend;
	private float _targetBlend;
	private bool _isUnderwater;
	private VRCPlayerApi _localPlayer;
	private Transform _sphereTransform;
	private Vector3 _lastHeadPos;
	private Quaternion _lastHeadRot;
	private bool _hasHeadSample;
	private bool _localInTrigger;
	private float _lastTriggerSignalTime;

	// -----------------------------------------------------------------------
	// Unity lifecycle
	// -----------------------------------------------------------------------
	private void Start()
	{
		if (blurSphereRenderer == null)
		{
			Debug.LogWarning("[UnderwaterBlurController] blurSphereRenderer is not assigned.");
			return;
		}

		_sphereTransform = blurSphereRenderer.transform;
		_localPlayer = Networking.LocalPlayer;
		if (waterTriggerCollider == null)
		{
			waterTriggerCollider = GetComponent<Collider>();
		}

		// Grab an instance copy so we don't modify the shared material asset.
		_mat = blurSphereRenderer.material;
		_currentBlend = 0f;
		_targetBlend = 0f;

		// Start hidden.
		_mat.SetFloat("_EffectBlend", 0f);
		blurSphereRenderer.enabled = false;
	}

	private void LateUpdate()
	{
		if (_sphereTransform == null) return;

		if (_localPlayer == null)
		{
			_localPlayer = Networking.LocalPlayer;
			if (_localPlayer == null) return;
		}

		VRCPlayerApi.TrackingData head = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

		float moveDeltaSq = (head.position - _lastHeadPos).sqrMagnitude;
		bool movedEnough = !_hasHeadSample || moveDeltaSq > 0.00000025f; // ~0.5 mm
		bool rotatedEnough = followHeadRotation && (!_hasHeadSample || Quaternion.Angle(head.rotation, _lastHeadRot) > 0.05f);

		if (!movedEnough && !rotatedEnough) return;

		if (followHeadRotation)
		{
			_sphereTransform.SetPositionAndRotation(head.position, head.rotation);
		}
		else
		{
			_sphereTransform.position = head.position;
		}

		_lastHeadPos = head.position;
		_lastHeadRot = head.rotation;
		_hasHeadSample = true;
	}

	private void Update()
	{
		if (_mat == null) return;
		if (_localPlayer == null) _localPlayer = Networking.LocalPlayer;

		bool shouldBeUnderwater;
		if (waterTriggerCollider != null)
		{
			// Strict mode: when the head leaves the collider, effect turns off immediately.
			shouldBeUnderwater = IsHeadInsideWater();
		}
		else
		{
			// Fallback mode if no collider is assigned/found.
			shouldBeUnderwater = _localInTrigger || (Time.time - _lastTriggerSignalTime) <= triggerExitGraceSeconds;
		}

		if (shouldBeUnderwater != _isUnderwater)
		{
			SetUnderwaterState(shouldBeUnderwater);
		}

		if (Mathf.Abs(_currentBlend - _targetBlend) < 0.0005f) return;

		float speed = _isUnderwater ? fadeInSpeed : fadeOutSpeed;
		_currentBlend = Mathf.MoveTowards(_currentBlend, _targetBlend, speed * Time.deltaTime);
		_mat.SetFloat("_EffectBlend", _currentBlend);

		// Once fully faded out, disable the renderer to skip GPU work entirely.
		if (!_isUnderwater && _currentBlend <= 0.0005f)
		{
			_currentBlend = 0f;
			_mat.SetFloat("_EffectBlend", 0f);
			blurSphereRenderer.enabled = false;
		}
	}

	private void SetUnderwaterState(bool value)
	{
		_isUnderwater = value;
		_targetBlend = value ? maxEffectBlend : 0f;

		if (value && blurSphereRenderer != null)
		{
			blurSphereRenderer.enabled = true;
		}
	}

	private bool IsHeadInsideWater()
	{
		if (_localPlayer == null || waterTriggerCollider == null) return false;

		VRCPlayerApi.TrackingData head = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
		Vector3 closest = waterTriggerCollider.ClosestPoint(head.position);
		return (closest - head.position).sqrMagnitude <= 0.00000001f;
	}

	// -----------------------------------------------------------------------
	// VRChat trigger callbacks
	//   These fire when any VRC Player enters/exits the trigger collider on
	//   the same GameObject as this script.  We filter to local player only.
	// -----------------------------------------------------------------------
	public override void OnPlayerTriggerEnter(VRCPlayerApi player)
	{
		if (player == null || !player.isLocal) return;
		if (blurSphereRenderer == null) return;

		_localInTrigger = true;
		_lastTriggerSignalTime = Time.time;
		SetUnderwaterState(true);
	}

	public override void OnPlayerTriggerStay(VRCPlayerApi player)
	{
		if (player == null || !player.isLocal) return;

		_localInTrigger = true;
		_lastTriggerSignalTime = Time.time;
	}

	public override void OnPlayerTriggerExit(VRCPlayerApi player)
	{
		if (player == null || !player.isLocal) return;

		_localInTrigger = false;
		// Update() applies state change after the grace window elapses.
	}
}
