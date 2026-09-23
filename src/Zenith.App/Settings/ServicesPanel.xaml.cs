using System.Windows;
using System.Windows.Controls;
using Zenith.App.Registry;

namespace Zenith.App.Settings;

public partial class ServicesPanel : UserControl
{
    public ServicesPanel() : this(new ServicesViewModel(() => new JsonServiceRegistryLoader().LoadBundledAsync())) { }

    internal ServicesPanel(ServicesViewModel model)
    {
        InitializeComponent();
        Model = model;
        DataContext = model;
    }

    internal ServicesViewModel Model { get; }
    internal Task EnsureLoadedAsync() => Model.EnsureLoadedAsync();

    private void Service_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ServicePresentation service }) Model.SelectedService = service;
    }

    private async void Retry_OnClick(object sender, RoutedEventArgs e) => await Model.LoadAsync();
    private void Prepare_OnClick(object sender, RoutedEventArgs e) => Model.PrepareProposal();
    private void Continue_OnClick(object sender, RoutedEventArgs e) => Model.ContinueToVault();
    private void Optional_OnClick(object sender, RoutedEventArgs e) =>
        Model.PrepareProposal(Model.OptionalScopes.Where(s => s.IsSelected).Select(s => s.Hostname).ToArray());
}
