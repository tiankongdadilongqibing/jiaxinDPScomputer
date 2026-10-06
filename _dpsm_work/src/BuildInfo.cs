namespace DpsMeter;

/// <summary>
/// Single source of the plugin version.
///
/// Everything user-visible reads it from here -- the BepInEx plugin attribute, the startup banner and
/// the version stamp in every exported JSON -- so a release only changes this one string. The csproj
/// &lt;Version&gt; is kept in sync manually (it only affects the assembly metadata).
/// </summary>
public static class BuildInfo
{
	public const string Guid = "dev.dpsmeter";
	public const string Name = "DpsMeter";
	public const string Version = "1.7.19";
}
