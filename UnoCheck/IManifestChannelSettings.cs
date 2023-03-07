namespace DotNetCheck
{
	public interface IManifestChannelSettings
	{
        bool Preview { get; set; }

        bool PreviewMajor { get; set; }

        bool Main { get; set; }
	}
}
