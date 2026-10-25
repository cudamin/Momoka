using Momoka.Shell.ViewModels;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class MoviePilotResourceFacetPicker : UserControl
{
    public static readonly DependencyProperty FacetProperty = DependencyProperty.Register(
        nameof(Facet), typeof(MoviePilotResourceFacet), typeof(MoviePilotResourceFacetPicker),
        new PropertyMetadata(null, OnFacetChanged));

    public MoviePilotResourceFacet Facet
    {
        get => (MoviePilotResourceFacet)GetValue(FacetProperty);
        set => SetValue(FacetProperty, value);
    }

    public MoviePilotResourceFacetPicker()
    {
        Facet = new MoviePilotResourceFacet("", "", "", () => { });
        InitializeComponent();
    }

    private static void OnFacetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var picker = (MoviePilotResourceFacetPicker)sender;
        if (args.OldValue is MoviePilotResourceFacet old) old.PropertyChanged -= picker.OnFacetPropertyChanged;
        if (args.NewValue is MoviePilotResourceFacet current) current.PropertyChanged += picker.OnFacetPropertyChanged;
        picker.Visibility = picker.Facet.Visibility;
        picker.Bindings?.Update();
    }

    private void OnFacetPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MoviePilotResourceFacet.Visibility)) Visibility = Facet.Visibility;
    }

    internal void ClosePopup() => ChoicesFlyout.Hide();
}
