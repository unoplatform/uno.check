using Microsoft.UI.Xaml;

namespace UnoCheck.Gui;

public partial class App : Application
{
	private Window? _window;

	public App()
	{
		this.InitializeComponent();
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		_window = new Window
		{
			Title = "uno-check Doctor (POC)",
		};

		_window.Content = new MainPage();
		_window.Activate();
	}
}
