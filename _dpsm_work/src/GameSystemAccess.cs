namespace DpsMeter;

public static class GameSystemAccess
{
	private static GameSystem _cached;

	private static bool _hasCached;

	public static GameSystem TryGet()
	{
		try
		{
			GameSystem instance = GameSystemBase<GameSystem>.Instance;
			if (instance != null)
			{
				_cached = instance;
				_hasCached = true;
				return instance;
			}
			if (_hasCached && _cached != null)
			{
				return _cached;
			}
		}
		catch
		{
		}
		return null;
	}

	public static void Invalidate()
	{
		_cached = null;
		_hasCached = false;
	}
}
