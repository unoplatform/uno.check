using Microsoft.UI.Xaml.Controls;
using UnoCheck.Gui.ViewModels;

namespace UnoCheck.Gui;

public sealed partial class MainPage : Page
{
	public DoctorViewModel ViewModel { get; }

	public MainPage()
	{
		ViewModel = new DoctorViewModel(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
		this.InitializeComponent();

		// Doctor panels diagnose on open; no reason to make the user click first.
		Loaded += (_, _) => ViewModel.RunCommand.Execute(null);
	}
}
