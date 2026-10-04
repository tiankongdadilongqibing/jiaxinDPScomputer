using UnityEngine;

namespace DpsMeter;

public class OverlayUI : MonoBehaviour
{
	private void Awake()
	{
		try
		{
			OverlayCore.Awake();
		}
		catch
		{
		}
	}

	private void Update()
	{
		try
		{
			OverlayCore.Update();
		}
		catch
		{
		}
	}

	private void OnGUI()
	{
		try
		{
			OverlayCore.OnGUI();
		}
		catch
		{
		}
	}

	private void OnDestroy()
	{
		try
		{
			OverlayCore.OnDestroy();
		}
		catch
		{
		}
	}
}
